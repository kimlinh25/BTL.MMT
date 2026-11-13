using System.Buffers.Binary;
using System.Text.Json;
using ET4230.Core.Protocol;

namespace ET4230.Core.Tests.Protocol;

public sealed class HeaderCodecTests
{
    public static IEnumerable<object[]> ProtocolVectors => LoadProtocolVectors()
        .Select(vector => new object[] { vector.Name, vector.HeaderHex, vector.Type, vector.RequestId, vector.PayloadLength });

    public static IEnumerable<object[]> MalformedHeaders => CreateMalformedHeaders()
        .Select(testCase => new object[] { testCase.Name, testCase.Bytes, testCase.ExpectedError });

    public static IEnumerable<object[]> InvalidHeadersForEncoding => CreateInvalidHeadersForEncoding()
        .Select(testCase => new object[] { testCase.Name, testCase.Header });

    [Theory]
    [MemberData(nameof(ProtocolVectors))]
    public void EncodeHeader_matches_independent_protocol_vector(
        string name,
        string expectedHex,
        byte type,
        uint requestId,
        uint payloadLength)
    {
        var header = new FrameHeader(Version: 1, Type: type, Flags: 0, RequestId: requestId, PayloadLength: payloadLength);
        var destination = new byte[HeaderCodec.HeaderSize];

        HeaderCodec.EncodeHeader(header, destination);

        var actualHex = Convert.ToHexString(destination).ToLowerInvariant();
        Assert.True(actualHex == expectedHex, $"Vector '{name}' encoded as {actualHex}, expected {expectedHex}.");
    }

    [Theory]
    [MemberData(nameof(ProtocolVectors))]
    public void TryDecodeHeader_reads_protocol_vector_fields(
        string name,
        string headerHex,
        byte expectedType,
        uint expectedRequestId,
        uint expectedPayloadLength)
    {
        var source = Convert.FromHexString(headerHex);

        var success = HeaderCodec.TryDecodeHeader(source, out var header, out var error);

        Assert.True(success, $"Vector '{name}' should decode; error was {error}.");
        Assert.Equal(HeaderDecodeError.None, error);
        Assert.Equal(new FrameHeader(1, expectedType, 0, expectedRequestId, expectedPayloadLength), header);
    }

    [Theory]
    [MemberData(nameof(MalformedHeaders))]
    public void TryDecodeHeader_rejects_malformed_headers(
        string name,
        byte[] source,
        HeaderDecodeError expectedError)
    {
        var success = HeaderCodec.TryDecodeHeader(source, out var header, out var error);

        Assert.False(success, $"Malformed case '{name}' should be rejected.");
        Assert.Equal(expectedError, error);
        Assert.Equal(default, header);
    }

    [Theory]
    [MemberData(nameof(InvalidHeadersForEncoding))]
    public void EncodeHeader_rejects_invalid_header_fields(string name, FrameHeader header)
    {
        var destination = new byte[HeaderCodec.HeaderSize];

        var exception = Record.Exception(() => HeaderCodec.EncodeHeader(header, destination));
        Assert.True(exception is ArgumentException,
            $"Invalid header case '{name}' should throw ArgumentException; got {exception?.GetType().Name ?? "no exception"}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void EncodeHeader_requires_exactly_sixteen_destination_bytes(int destinationLength)
    {
        var validHeader = new FrameHeader(1, 4, 0, 0, 4);

        Assert.Throws<ArgumentException>(() => HeaderCodec.EncodeHeader(validHeader, new byte[destinationLength]));
    }

    private static IEnumerable<ProtocolVector> LoadProtocolVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "protocol-vectors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var headerHex = vector.GetProperty("headerHex").GetString()!;
            var header = Convert.FromHexString(headerHex);

            yield return new ProtocolVector(
                vector.GetProperty("name").GetString()!,
                headerHex,
                header[5],
                BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12, 4)));
        }
    }

    private static IEnumerable<MalformedHeaderCase> CreateMalformedHeaders()
    {
        yield return new("short header", new byte[15], HeaderDecodeError.InvalidHeaderSize);
        yield return new("long header", new byte[17], HeaderDecodeError.InvalidHeaderSize);

        var validHaveHeader = Convert.FromHexString("45343233010400000000000000000004");

        var invalidMagic = (byte[])validHaveHeader.Clone();
        invalidMagic[0] = (byte)'X';
        yield return new("wrong magic", invalidMagic, HeaderDecodeError.InvalidMagic);

        var unsupportedVersion = (byte[])validHaveHeader.Clone();
        unsupportedVersion[4] = 2;
        yield return new("unsupported version", unsupportedVersion, HeaderDecodeError.UnsupportedVersion);

        var unknownType = (byte[])validHaveHeader.Clone();
        unknownType[5] = 9;
        yield return new("unknown type", unknownType, HeaderDecodeError.UnknownType);

        var invalidFlags = (byte[])validHaveHeader.Clone();
        invalidFlags[7] = 1;
        yield return new("nonzero flags", invalidFlags, HeaderDecodeError.InvalidFlags);

        var zeroRequestId = CreateRawHeader(version: 1, type: 5, flags: 0, requestId: 0, payloadLength: 12);
        yield return new("request message with request ID zero", zeroRequestId, HeaderDecodeError.InvalidRequestId);

        var notificationWithRequestId = CreateRawHeader(version: 1, type: 4, flags: 0, requestId: 1, payloadLength: 4);
        yield return new("HAVE notification with nonzero request ID", notificationWithRequestId, HeaderDecodeError.InvalidRequestId);

        yield return new("HAVE payload too short", CreateRawHeader(1, 4, 0, 0, 3), HeaderDecodeError.InvalidPayloadLength);
        yield return new("REQUEST_BLOCK payload has wrong size", CreateRawHeader(1, 5, 0, 1, 11), HeaderDecodeError.InvalidPayloadLength);
        yield return new("BLOCK has metadata but no data", CreateRawHeader(1, 6, 0, 1, 12), HeaderDecodeError.InvalidPayloadLength);
        yield return new("CANCEL has a payload", CreateRawHeader(1, 7, 0, 1, 1), HeaderDecodeError.InvalidPayloadLength);
        yield return new("control payload is empty", CreateRawHeader(1, 1, 0, 1, 0), HeaderDecodeError.InvalidPayloadLength);
        yield return new("control payload exceeds 64 KiB", CreateRawHeader(1, 1, 0, 1, 65_537), HeaderDecodeError.InvalidPayloadLength);
        yield return new("payload exceeds 1 MiB", CreateRawHeader(1, 3, 0, 0, 1_048_577), HeaderDecodeError.InvalidPayloadLength);
    }

    private static IEnumerable<InvalidHeaderCase> CreateInvalidHeadersForEncoding()
    {
        yield return new("unsupported version", new FrameHeader(2, 4, 0, 0, 4));
        yield return new("unknown type", new FrameHeader(1, 9, 0, 1, 1));
        yield return new("nonzero flags", new FrameHeader(1, 4, 1, 0, 4));
        yield return new("notification with request ID", new FrameHeader(1, 4, 0, 1, 4));
        yield return new("request without request ID", new FrameHeader(1, 5, 0, 0, 12));
        yield return new("oversized payload", new FrameHeader(1, 3, 0, 0, 1_048_577));
        yield return new("invalid type-specific payload length", new FrameHeader(1, 4, 0, 0, 3));
    }

    private static byte[] CreateRawHeader(byte version, byte type, ushort flags, uint requestId, uint payloadLength)
    {
        var bytes = new byte[HeaderCodec.HeaderSize];
        "E423"u8.CopyTo(bytes);
        bytes[4] = version;
        bytes[5] = type;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6, 2), flags);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), requestId);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12, 4), payloadLength);
        return bytes;
    }

    private sealed record ProtocolVector(string Name, string HeaderHex, byte Type, uint RequestId, uint PayloadLength);

    private sealed record MalformedHeaderCase(string Name, byte[] Bytes, HeaderDecodeError ExpectedError);

    private sealed record InvalidHeaderCase(string Name, FrameHeader Header);
}
