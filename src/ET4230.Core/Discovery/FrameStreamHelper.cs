using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ET4230.Core.Protocol;

namespace ET4230.Core.Discovery;

internal sealed record FrameMessage(FrameHeader Header, byte[] Payload);

/// <summary>
/// Adapter đọc/ghi frame theo <see cref="HeaderCodec"/> qua <see cref="NetworkStream"/>
/// (dùng tạm trong lúc chờ lớp TCP transport chung hoàn thiện).
/// </summary>
internal static class FrameStreamHelper
{
    public static async Task<FrameMessage?> ReadFrameAsync(
        Stream stream,
        int maxPayloadBytes,
        CancellationToken cancellationToken)
    {
        var headerBuffer = new byte[HeaderCodec.HeaderSize];
        int firstRead = await stream.ReadAsync(headerBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (firstRead == 0)
            return null; // Đóng kết nối sạch trước frame mới

        await ReadExactAsync(stream, headerBuffer.AsMemory(1), cancellationToken).ConfigureAwait(false);

        if (!HeaderCodec.TryDecodeHeader(headerBuffer, out var header, out var error))
            throw new InvalidDataException($"Frame header không hợp lệ: {error}.");

        if (header.PayloadLength > (uint)maxPayloadBytes)
            throw new InvalidDataException($"Payload length {header.PayloadLength} vượt giới hạn {maxPayloadBytes} byte.");

        var payload = new byte[(int)header.PayloadLength];
        if (payload.Length > 0)
            await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);

        return new FrameMessage(header, payload);
    }

    public static async Task WriteFrameAsync(
        Stream stream,
        FrameHeader header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (header.PayloadLength != (uint)payload.Length)
            throw new ArgumentException("Header PayloadLength không khớp kích thước payload thực tế.");

        var headerBytes = new byte[HeaderCodec.HeaderSize];
        HeaderCodec.EncodeHeader(header, headerBytes);

        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Gặp EOF giữa chừng khi đang đọc frame.");

            offset += read;
        }
    }
}
