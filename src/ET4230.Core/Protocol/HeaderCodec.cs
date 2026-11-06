using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace ET4230.Core.Protocol;

public static class HeaderCodec
{
    public const int HeaderSize = 16; // Size of the header in bytes

    private const byte CurrentVersion = 1; // Current version of the protocol
    private const uint MaxPayloadLength = 1024 * 1024; // Maximum payload length (1 MB)
    private const uint MaxControlJsonLength = 64 * 1024; // Maximum control JSON length (64 KB)
    private const uint BlockMetadataSize = 12; // Size of the block metadata in bytes

    private const byte Bitfield = 3; // Thông báo Peer đang sở hữu những piece nào
    private const byte Have = 4; // Thông báo Peer vừa có thêm một piece
    private const byte RequestBlock = 5; // Yêu cầu Peer khác gửi một block dữ liệu
    private const byte Block = 6; // Gửi block dữ liệu được yêu cầu
    private const byte Cancel = 7; // Hủy một yêu cầu tải block
    private const byte Error = 8; // Thông báo lỗi xảy ra trong quá trình giao tiếp
    
    // Decodes a header from a byte array and returns the FrameHeader and any error encountered during decoding.
    public static bool TryDecodeHeader(
        ReadOnlySpan<byte> source,
        out FrameHeader header,
        out HeaderDecodeError error)
    {
        header = default;

        if(source.Length != HeaderSize)
        {
            error = HeaderDecodeError.InvalidHeaderSize;
            return false;
        }

        // [..4] là một slice của mảng byte source, lấy 4 byte đầu tiên từ mảng.

        if (!source[..4].SequenceEqual("E423"u8))
        {
            error = HeaderDecodeError.InvalidMagic;
            return false;
        }

        var candidate = new FrameHeader(
            source[4], // Version
            source[5], // Type
            BinaryPrimitives.ReadUInt16BigEndian(source[6..8]), // Flags
            BinaryPrimitives.ReadUInt32BigEndian(source[8..12]), // RequestId
            BinaryPrimitives.ReadUInt32BigEndian(source[12..16])); // PayloadLength

        error = Validate(candidate);
        if(error != HeaderDecodeError.None)
            return false;

        header = candidate;
        return true;
    }

    public static void EncodeHeader(FrameHeader header, Span<byte> destination)
    {
        if(destination.Length != HeaderSize)
            throw new ArgumentException($"Destination span must be {HeaderSize} bytes long.", nameof(destination));

        var error = Validate(header);
        if(error != HeaderDecodeError.None)
            throw new ArgumentException($"Invalid header: {error}", nameof(header));

        "E423"u8.CopyTo(destination[..4]);

        destination[4] = header.Version;
        destination[5] = header.Type;
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], header.Flags);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], header.RequestId);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], header.PayloadLength);
    }

    private static HeaderDecodeError Validate(FrameHeader header)
    {
        if(header.Version != CurrentVersion)
            return HeaderDecodeError.UnsupportedVersion;

        if (header.Type is not (>= 1 and <= 8 or >= 16 and <= 20))
            return HeaderDecodeError.UnknownType;

        if(header.Flags != 0)
            return HeaderDecodeError.InvalidFlags;

        if(header.PayloadLength > MaxPayloadLength)
            return HeaderDecodeError.InvalidPayloadLength;

        // Khai báo một biến boolean để kiểm tra tính hợp lệ của RequestId dựa trên Type của header.
        bool validRequestId = header.Type switch
        {
            Bitfield or Have => header.RequestId == 0,
            Error => true,
            _ => header.RequestId != 0
        };

        if (!validRequestId)
            return HeaderDecodeError.InvalidRequestId;

        // Khai báo một biến boolean để kiểm tra tính hợp lệ của PayloadLength dựa trên Type của header.        
        bool validLength = header.Type switch
        {
            Bitfield => true,
            Have => header.PayloadLength == 4,
            RequestBlock => header.PayloadLength == 12,
            Block => header.PayloadLength > BlockMetadataSize,
            Cancel => header.PayloadLength == 0,
            _ => header.PayloadLength is > 0 and <= MaxControlJsonLength
        };

        return validLength ? HeaderDecodeError.None : HeaderDecodeError.InvalidPayloadLength;
    }
}
