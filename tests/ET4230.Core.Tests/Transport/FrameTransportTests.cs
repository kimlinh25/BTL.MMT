using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ET4230.Core.Protocol;
using ET4230.Core.Transport;

namespace ET4230.Core.Tests.Transport;

public sealed class FrameTransportTests
{
    [Fact]
    public async Task ReadsTwoFramesFromShortReadsAndThenCleanEof()
    {
        using var stream = new ShortReadStream(Convert.FromHexString(
            "4534323301040000000000000000000400000002" +
            "4534323301050000000000070000000c000000020000000000004000"));

        Frame have = Assert.IsType<Frame>(await FrameTransport.ReadAsync(stream));
        Assert.Equal(new FrameHeader(1, 4, 0, 0, 4), have.Header);
        Assert.Equal("00000002", Convert.ToHexString(have.Payload.Span));

        Frame request = Assert.IsType<Frame>(await FrameTransport.ReadAsync(stream));
        Assert.Equal(new FrameHeader(1, 5, 0, 7, 12), request.Header);
        Assert.Equal("000000020000000000004000", Convert.ToHexString(request.Payload.Span));
        Assert.Null(await FrameTransport.ReadAsync(stream));
    }

    [Theory]
    [InlineData("4534323301")]
    [InlineData("453432330104000000000000000000040000")]
    public async Task RejectsEofInsideAFrame(string hex)
    {
        using var stream = new ShortReadStream(Convert.FromHexString(hex));
        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => { await FrameTransport.ReadAsync(stream); });
    }

    [Fact]
    public async Task RejectsInvalidHeaderBeforeReadingPayload()
    {
        using var stream = new MemoryStream(Convert.FromHexString(
            "45343233010300000000000000100001"));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => { await FrameTransport.ReadAsync(stream); });
        Assert.Equal(16L, stream.Position);
    }

    [Fact]
    public async Task RejectsPayloadAboveCallerLimitWithoutReadingIt()
    {
        using var stream = new MemoryStream(Convert.FromHexString(
            "453432330110000000000001000000047b7d2020"));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => { await FrameTransport.ReadAsync(stream, 3, CancellationToken.None); });
        Assert.Equal(16L, stream.Position);
    }

    [Fact]
    public async Task SendsAnEmptyCancelPayloadWithTheExpectedWireBytes()
    {
        using var stream = new MemoryStream();
        await FrameTransport.WriteAsync(stream, 7, 7, ReadOnlyMemory<byte>.Empty);
        Assert.Equal("45343233010700000000000700000000", Convert.ToHexString(stream.ToArray()));

        stream.Position = 0;
        Frame frame = Assert.IsType<Frame>(await FrameTransport.ReadAsync(stream));
        Assert.Empty(frame.Payload.ToArray());
        Assert.Null(await FrameTransport.ReadAsync(stream));
    }

    [Fact]
    public async Task SeedReturnsAvailabilityAndBlockOverTcp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var source = new TestSeedSource();

        await Task.WhenAll(ServeAsync(), ReceiveAsync());

        async Task ServeAsync()
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(timeout.Token);
            await SeedSession.RunAsync(client.GetStream(), Guid.NewGuid(), source, timeout.Token);
        }

        async Task ReceiveAsync()
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            NetworkStream stream = client.GetStream();
            byte[] hello = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new { peerId = Guid.NewGuid().ToString("D"), fileId = source.FileId });

            await FrameTransport.WriteAsync(stream, 1, 1, hello, timeout.Token);
            Frame helloOk = Assert.IsType<Frame>(await FrameTransport.ReadAsync(stream, timeout.Token));
            Assert.Equal((byte)2, helloOk.Header.Type);
            Assert.Equal(1u, helloOk.Header.RequestId);

            Frame bitfield = Assert.IsType<Frame>(await FrameTransport.ReadAsync(stream, timeout.Token));
            Assert.Equal(new FrameHeader(1, 3, 0, 0, 1), bitfield.Header);
            Assert.Equal("80", Convert.ToHexString(bitfield.Payload.Span));

            await FrameTransport.WriteAsync(stream, 5, 2,
                Convert.FromHexString("000000000000000000000004"), timeout.Token);
            Frame block = Assert.IsType<Frame>(await FrameTransport.ReadAsync(stream, timeout.Token));
            Assert.Equal(new FrameHeader(1, 6, 0, 2, 16), block.Header);
            Assert.Equal("00000000000000000000000441424344",
                Convert.ToHexString(block.Payload.Span));
        }
    }

    private sealed class ShortReadStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
    }

    private sealed class TestSeedSource : ISeedSource
    {
        public string FileId { get; } = Convert.ToHexStringLower(SHA256.HashData("ABCD"u8));
        public int PieceCount => 1;
        public bool IsVerified(int pieceIndex) => pieceIndex == 0;

        public ValueTask<BlockReadResult> ReadBlockAsync(
            BlockRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(request == new BlockRequest(0, 0, 4)
                ? new BlockReadResult(BlockReadStatus.Available, "ABCD"u8.ToArray())
                : new BlockReadResult(BlockReadStatus.InvalidRange, ReadOnlyMemory<byte>.Empty));
        }
    }
}
