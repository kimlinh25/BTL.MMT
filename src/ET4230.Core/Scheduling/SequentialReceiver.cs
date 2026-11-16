using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using ET4230.Core.Storage;
using ET4230.Core.Transport;

namespace ET4230.Core.Scheduling;

public enum ReceiverStatus
{
    Completed,
    MissingSource,
    PeerError,
    ProtocolError,
    Disconnected,
    TimedOut,
    StorageError,
    IntegrityFailed,
    TransportError
}

public sealed record ReceiverResult(
    ReceiverStatus Status, int VerifiedPieces, int TotalPieces, string? Detail = null);

public static class SequentialReceiver
{
    private const byte Hello = 1;
    private const byte HelloOk = 2;
    private const byte Bitfield = 3;
    private const byte Have = 4;
    private const byte RequestBlock = 5;
    private const byte Block = 6;
    private const byte Error = 8;
    private const int BlockFieldsSize = 12;
    private const int MaxPayloadSize = 1024 * 1024;

    /// <summary>
    /// Tải qua một kết nối TCP tới nguồn được chọn trong config.
    /// Caller giữ FileStorage sống và dành riêng cho phiên cho đến khi task kết thúc.
    /// Hủy từ caller truyền OperationCanceledException. Timeout mạng có kết quả TimedOut.
    /// </summary>
    public static async Task<ReceiverResult> DownloadAsync(
        string host,
        int port,
        Guid localPeerId,
        FileStorage storage,
        TimeSpan networkTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentNullException.ThrowIfNull(storage);
        if (localPeerId == Guid.Empty)
            throw new ArgumentException("Receiver cần peerId hợp lệ.", nameof(localPeerId));
        if (networkTimeout <= TimeSpan.Zero || networkTimeout.TotalMilliseconds > uint.MaxValue - 1d)
            throw new ArgumentOutOfRangeException(nameof(networkTimeout));

        cancellationToken.ThrowIfCancellationRequested();
        PieceLayout layout = storage.Layout;
        int pieceCount = layout.PieceCount;
        int bitfieldLength = (int)(((long)pieceCount + 7) / 8);
        if (bitfieldLength > MaxPayloadSize || layout.BlockLength > MaxPayloadSize - BlockFieldsSize)
            throw new ArgumentException("Layout vượt giới hạn payload v1.", nameof(storage));

        var verified = new bool[pieceCount];
        var inFlight = new bool[pieceCount];
        int verifiedCount = 0;
        for (int i = 0; i < pieceCount; i++)
        {
            verified[i] = storage.IsPieceVerified(i);
            if (verified[i])
                verifiedCount++;
        }

        ReceiverResult Result(ReceiverStatus status, string? detail = null) =>
            new(status, verifiedCount, pieceCount, detail);

        try
        {
            using var client = new TcpClient();
            using (var deadline = CreateDeadline(networkTimeout, cancellationToken))
            {
                await client.ConnectAsync(host, port, deadline.Token);
            }

            using NetworkStream stream = client.GetStream();
            uint requestId = 1;
            var offered = new bool[pieceCount];
            IReadOnlyList<IReadOnlyList<bool>> sources = [offered];

            using (var deadline = CreateDeadline(networkTimeout, cancellationToken))
            {
                string fileId = storage.Manifest.FileId.ToLowerInvariant();
                byte[] hello = JsonSerializer.SerializeToUtf8Bytes(
                    new { peerId = localPeerId.ToString("D"), fileId });
                await FrameTransport.WriteAsync(stream, Hello, requestId, hello, deadline.Token);

                Frame response = await ReadRequiredAsync(stream, deadline.Token);
                CheckPeerError(response, requestId);
                ValidateHello(response, requestId, fileId);

                Frame bitmap = await ReadRequiredAsync(stream, deadline.Token);
                CheckPeerError(bitmap, requestId);
                ReadBitfield(bitmap, offered, bitfieldLength);
            }

            while (verifiedCount < pieceCount)
            {
                int? next = SequentialPieceSelector.SelectSequentialPiece(
                    pieceCount, sources, verified, inFlight);
                if (next is not int pieceIndex)
                    return Result(ReceiverStatus.MissingSource, "Nguồn hiện tại không có piece còn thiếu.");

                inFlight[pieceIndex] = true;
                try
                {
                    int blockCount = layout.GetBlockCountInPiece(pieceIndex);
                    for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
                    {
                        var range = layout.GetBlockRange(pieceIndex, blockIndex);
                        requestId = requestId == uint.MaxValue ? 1 : requestId + 1;

                        ReadOnlyMemory<byte> data;
                        using (var deadline = CreateDeadline(networkTimeout, cancellationToken))
                        {
                            data = await RequestAsync(stream, requestId, pieceIndex,
                                range.OffsetInPiece, range.Length, offered, deadline.Token);
                        }

                        StorageResult write = await storage.WriteBlockAsync(
                            pieceIndex, blockIndex, data, cancellationToken);
                        if (write.Status != StorageStatus.Ok)
                            return Result(ReceiverStatus.StorageError, write.ErrorMessage ?? write.Status.ToString());
                    }

                    StorageResult check = await storage.VerifyPieceAsync(pieceIndex, cancellationToken);
                    if (check.Status != StorageStatus.Verified)
                        return Result(StorageFailure(check), check.ErrorMessage ?? check.Status.ToString());

                    verified[pieceIndex] = true;
                    verifiedCount++;

                    var have = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(have, (uint)pieceIndex);
                    using var haveDeadline = CreateDeadline(networkTimeout, cancellationToken);
                    await FrameTransport.WriteAsync(stream, Have, 0, have, haveDeadline.Token);
                }
                finally
                {
                    inFlight[pieceIndex] = false;
                }
            }

            StorageResult full = await storage.VerifyFullFileAsync(cancellationToken);
            return full.Status == StorageStatus.Verified
                ? Result(ReceiverStatus.Completed)
                : Result(StorageFailure(full), full.ErrorMessage ?? full.Status.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(ReceiverStatus.TimedOut, "Kết nối hoặc trao đổi frame vượt thời gian chờ.");
        }
        catch (SeedErrorException ex)
        {
            return Result(ReceiverStatus.PeerError, ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return Result(ReceiverStatus.ProtocolError, ex.Message);
        }
        catch (EndOfStreamException ex)
        {
            return Result(ReceiverStatus.Disconnected, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            return Result(ReceiverStatus.TransportError, ex.Message);
        }
    }

    private static ReceiverStatus StorageFailure(StorageResult result) =>
        result.Status == StorageStatus.HashMismatch
            ? ReceiverStatus.IntegrityFailed
            : ReceiverStatus.StorageError;

    private static CancellationTokenSource CreateDeadline(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        return deadline;
    }

    private static async ValueTask<Frame> ReadRequiredAsync(Stream stream, CancellationToken cancellationToken) =>
        await FrameTransport.ReadAsync(stream, cancellationToken)
        ?? throw new EndOfStreamException("Nguồn đóng kết nối khi receiver đang chờ frame.");

    private static void ValidateHello(Frame frame, uint requestId, string fileId)
    {
        if (frame.Header.Type != HelloOk || frame.Header.RequestId != requestId)
            throw new InvalidDataException("HELLO_OK không khớp request HELLO.");

        try
        {
            var hello = JsonSerializer.Deserialize<HelloResponse>(frame.Payload.Span);
            if (hello is null || !Guid.TryParseExact(hello.PeerId, "D", out Guid id) ||
                id == Guid.Empty || !string.Equals(hello.FileId, fileId, StringComparison.Ordinal))
                throw new InvalidDataException("HELLO_OK thiếu peerId hợp lệ hoặc sai fileId.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Payload HELLO_OK không hợp lệ.", ex);
        }
    }

    private static void ReadBitfield(Frame frame, bool[] offered, int expectedLength)
    {
        ReadOnlySpan<byte> bitmap = frame.Payload.Span;
        if (frame.Header.Type != Bitfield || bitmap.Length != expectedLength)
            throw new InvalidDataException("BITFIELD không khớp số lượng piece.");

        int bitsUsed = offered.Length % 8;
        if (bitsUsed != 0 && (bitmap[^1] & ((1 << (8 - bitsUsed)) - 1)) != 0)
            throw new InvalidDataException("BITFIELD có bit thừa khác 0.");

        for (int i = 0; i < offered.Length; i++)
            offered[i] = (bitmap[i / 8] & (1 << (7 - i % 8))) != 0;
    }

    private static async Task<ReadOnlyMemory<byte>> RequestAsync(
        Stream stream, uint requestId, int pieceIndex, int offset, int length,
        bool[] offered, CancellationToken cancellationToken)
    {
        var request = new byte[BlockFieldsSize];
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(0, 4), (uint)pieceIndex);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4, 4), (uint)offset);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8, 4), (uint)length);
        await FrameTransport.WriteAsync(stream, RequestBlock, requestId, request, cancellationToken);

        while (true)
        {
            Frame response = await ReadRequiredAsync(stream, cancellationToken);
            if (response.Header.Type == Have)
            {
                uint index = BinaryPrimitives.ReadUInt32BigEndian(response.Payload.Span);
                if (index >= (uint)offered.Length)
                    throw new InvalidDataException("Chỉ số HAVE vượt số piece.");
                offered[(int)index] = true;
                continue;
            }

            CheckPeerError(response, requestId);
            if (response.Header.Type != Block || response.Header.RequestId != requestId ||
                response.Payload.Length != BlockFieldsSize + length ||
                !response.Payload.Span[..BlockFieldsSize].SequenceEqual(request))
                throw new InvalidDataException("BLOCK không khớp ID, piece, offset hoặc length đang chờ.");

            return response.Payload[BlockFieldsSize..];
        }
    }

    private static void CheckPeerError(Frame frame, uint requestId)
    {
        if (frame.Header.Type != Error)
            return;
        if (frame.Header.RequestId != 0 && frame.Header.RequestId != requestId)
            throw new InvalidDataException("ERROR không khớp request đang chờ.");

        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(frame.Payload.Span);
            if (error is null || string.IsNullOrWhiteSpace(error.Code) || error.Message is null)
                throw new InvalidDataException("Payload ERROR thiếu code hoặc message.");

            throw new SeedErrorException($"{error.Code}: {error.Message}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Payload ERROR không hợp lệ.", ex);
        }
    }

    private sealed record HelloResponse(
        [property: JsonPropertyName("peerId")] string? PeerId,
        [property: JsonPropertyName("fileId")] string? FileId);

    private sealed record ErrorResponse(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message);

    private sealed class SeedErrorException(string message) : Exception(message);
}
