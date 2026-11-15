using ET4230.Core.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace ET4230.Core.Transport;

// Hàm đọc một frame từ stream, bao gồm header và payload.
public sealed record Frame(FrameHeader Header, ReadOnlyMemory<byte> Payload);

public static class FrameTransport
{
    public static async ValueTask<Frame?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        // Cấp phát một mảng byte có kích thước bằng HeaderSize để lưu trữ header.
        var headerBytes = new byte[HeaderCodec.HeaderSize];
        // Đọc header từ luồng stream vào mảng headerBytes.
        int headerCount = await FillAsync(stream, headerBytes, cancellationToken);

        // Nếu không đọc được byte nào, trả về null (stream kết thúc).
        if (headerCount == 0)
            return null;

        // Nếu số byte đọc được không bằng kích thước header, ném ngoại lệ EndOfStreamException.
        if (headerCount != headerBytes.Length)
            throw new EndOfStreamException("TCP kết thúc giữa header.");

        // Giải mã header từ mảng byte headerBytes.
        if (!HeaderCodec.TryDecodeHeader(headerBytes, out var header, out var error))
            throw new InvalidDataException($"Header v1 lỗi {error}.");

        // Kiểm tra xem payloadlength có bị vượt quá giới hạn cho phép hay không.
        int length = checked((int)header.PayloadLength);

        byte[] payload = length == 0 ? Array.Empty<byte>() : new byte[length];

        // Đọc payload từ luồng stream vào mảng payload.
        int payloadCount = await FillAsync(stream, payload, cancellationToken);

        // Nếu số byte đọc được không bằng kích thước payload, ném ngoại lệ EndOfStreamException.
        if (payloadCount != payload.Length)
            throw new EndOfStreamException("TCP kết thúc giữa payload.");

        return new Frame(header, payload);
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        byte type,
        uint requestId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        var header = new FrameHeader(
            Version: 1, Type: type, Flags: 0,
            RequestId: requestId, PayloadLength: (uint)payload.Length);

        var headerBytes = new byte[HeaderCodec.HeaderSize];
        HeaderCodec.EncodeHeader(header, headerBytes);

        await stream.WriteAsync(headerBytes, cancellationToken);

        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, cancellationToken);
    }

    // Trả về số byte đã đọc được từ stream, hoặc 0 nếu stream kết thúc.
    private static async ValueTask<int> FillAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int total = 0;
        
        while (total < destination.Length)
        {
            int count = await stream.ReadAsync(destination[total..], cancellationToken);
            if (count == 0)
                break;

            total += count;
        }

        // Trả về tổng số byte đã đọc được từ stream.
        return total;
    }
}
