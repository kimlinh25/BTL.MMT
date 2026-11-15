using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ET4230.Core.Transport;

public readonly record struct BlockRequest(uint PieceIndex, uint OffsetInPiece, uint Length);

public enum BlockReadStatus
{
    InvalidRange,
    PieceUnavailable,
    Available
}

public readonly record struct BlockReadResult(
    BlockReadStatus Status, ReadOnlyMemory<byte> Data);

public interface ISeedSource
{
    string FileId { get; }
    int PieceCount { get; }
    bool IsVerified(int pieceIndex);

    ValueTask<BlockReadResult> ReadBlockAsync(
        BlockRequest request, CancellationToken cancellationToken);
}

public static class SeedSession
{
    private const byte Hello = 1;
    private const byte HelloOk = 2;
    private const byte Bitfield = 3;
    private const byte Have = 4;
    private const byte RequestBlock = 5;
    private const byte Block = 6;
    private const byte Cancel = 7;
    private const byte Error = 8;
    private const int BlockFieldsSize = 12;
    private const int MaxPayloadSize = 1024 * 1024;

    public static async Task RunAsync(
        Stream stream, Guid localPeerId, ISeedSource source,
        CancellationToken cancellationToken = default)
    {
        if (localPeerId == Guid.Empty || !IsFileId(source.FileId))
            throw new ArgumentException("Seed cần peerId và fileId hợp lệ.");

        // Đọc frame đầu tiên từ stream
        Frame? first = await FrameTransport.ReadAsync(stream, cancellationToken);
        if (first is null) 
            return;

        // Kiểm tra loại frame đầu tiên 
        string requestedFileId = ReadHelloFileId(first);
        if (!string.Equals(requestedFileId, source.FileId, StringComparison.Ordinal))
        {
            await SendErrorAsync(stream, first.Header.RequestId, "FILE_MISMATCH",
                "Seed đang phục vụ một fileId khác.", cancellationToken);
            return;
        }

        byte[] bitmap = BuildBitfield(source);
        byte[] helloOk = JsonSerializer.SerializeToUtf8Bytes(
            new { peerId = localPeerId.ToString("D"), fileId = source.FileId });
        await FrameTransport.WriteAsync(
            stream, HelloOk, first.Header.RequestId, helloOk, cancellationToken);
        await FrameTransport.WriteAsync(stream, Bitfield, 0, bitmap, cancellationToken);

        while (await FrameTransport.ReadAsync(stream, cancellationToken) is { } frame)
        {
            if (frame.Header.Type == Cancel)
                continue;

            if (frame.Header.Type == Have)
            {
                uint pieceIndex = BinaryPrimitives.ReadUInt32BigEndian(frame.Payload.Span);
                if (pieceIndex >= (uint)source.PieceCount)
                    throw new InvalidDataException("Chỉ số HAVE vượt số piece.");
                continue;
            }

            if (frame.Header.Type != RequestBlock)
                throw new InvalidDataException("Message không phù hợp với session seed W03.");

            await ServeBlockAsync(stream, source, frame, cancellationToken);
        }
    }

    private static async Task ServeBlockAsync(
        Stream stream, ISeedSource source, Frame frame, CancellationToken cancellationToken)
    {
        BlockRequest request = ReadBlockRequest(frame.Payload.Span);
        uint requestId = frame.Header.RequestId;

        if (request.PieceIndex >= (uint)source.PieceCount ||
            request.Length == 0 || request.Length > MaxPayloadSize - BlockFieldsSize)
        {
            await SendErrorAsync(stream, requestId, "INVALID_REQUEST",
                "Piece index hoặc length không hợp lệ.", cancellationToken);
            return;
        }

        BlockReadResult result = await source.ReadBlockAsync(request, cancellationToken);

        if (result.Status == BlockReadStatus.InvalidRange)
        {
            await SendErrorAsync(stream, requestId, "INVALID_REQUEST",
                "Offset hoặc length không khớp layout tệp.", cancellationToken);
            return;
        }

        if (result.Status == BlockReadStatus.PieceUnavailable)
        {
            await SendErrorAsync(stream, requestId, "PIECE_UNAVAILABLE",
                "Piece chưa Verified hoặc không còn khả dụng.", cancellationToken);
            return;
        }

        if (result.Status != BlockReadStatus.Available ||
            (uint)result.Data.Length != request.Length)
            throw new IOException("Storage trả kết quả block không đúng hợp đồng.");

        var blockPayload = new byte[BlockFieldsSize + result.Data.Length];
        frame.Payload.CopyTo(blockPayload);
        result.Data.CopyTo(blockPayload.AsMemory(BlockFieldsSize));
        await FrameTransport.WriteAsync(stream, Block, requestId, blockPayload, cancellationToken);
    }

    private static BlockRequest ReadBlockRequest(ReadOnlySpan<byte> payload) => new(
        BinaryPrimitives.ReadUInt32BigEndian(payload[..4]),
        BinaryPrimitives.ReadUInt32BigEndian(payload[4..8]),
        BinaryPrimitives.ReadUInt32BigEndian(payload[8..12]));

    private static string ReadHelloFileId(Frame frame)
    {
        if (frame.Header.Type != Hello)
            throw new InvalidDataException("Frame đầu tiên của kết nối seed phải là HELLO.");

        try
        {
            var hello = JsonSerializer.Deserialize<HelloMessage>(frame.Payload.Span);
            if (hello is not { PeerId: string peerId, FileId: string fileId } ||
                !Guid.TryParseExact(peerId, "D", out Guid id) || id == Guid.Empty ||
                !IsFileId(fileId))
                throw new InvalidDataException("HELLO thiếu peerId hoặc fileId hợp lệ.");

            return fileId;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Payload HELLO phải là JSON UTF-8 hợp lệ.", error);
        }
    }

    private static byte[] BuildBitfield(ISeedSource source)
    {
        int count = source.PieceCount;
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(source), "Số piece phải không âm.");

        long length = ((long)count + 7) / 8;
        if (length > MaxPayloadSize)
            throw new ArgumentException("BITFIELD vượt giới hạn v1.", nameof(source));

        var bitmap = new byte[(int)length];
        for (int i = 0; i < count; i++)
        {
            if (source.IsVerified(i))
                bitmap[i / 8] |= (byte)(1 << (7 - i % 8));
        }

        return bitmap;
    }

    private static bool IsFileId(string fileId) =>
        fileId.Length == 64 &&
        fileId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ValueTask SendErrorAsync(
        Stream stream, uint requestId, string code, string message,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { code, message });
        return FrameTransport.WriteAsync(stream, Error, requestId, payload, cancellationToken);
    }

    private sealed record HelloMessage(
        [property: JsonPropertyName("peerId")] string? PeerId,
        [property: JsonPropertyName("fileId")] string? FileId);
}
