using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ET4230.Core.Scheduling;
using ET4230.Core.Storage;
using ET4230.Core.Transport;

namespace ET4230.Core.Tests.Scheduling;

public sealed class SequentialReceiverTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("et4230-receiver-").FullName;

    [Theory]
    [InlineData(19)]
    [InlineData(0)]
    public async Task DownloadsOverTcpAndVerifiesWholeFileIncludingEmptyFile(int fileLength)
    {
        byte[] bytes = Enumerable.Range(0, fileLength).Select(i => (byte)i).ToArray();
        string input = Path.Combine(_directory, "seed.bin");
        await File.WriteAllBytesAsync(input, bytes);
        var manifest = await FileManifest.CreateFromFileAsync(input, 8, 4);

        using var seed = new FileStorage(input, manifest, isSeed: true);
        int[] indices = fileLength == 0 ? [] : [0, 1, 2];
        var source = new StorageSeed(seed, indices);
        string output = Path.Combine(_directory, "receiver.bin");
        using var storage = new FileStorage(output, manifest);

        ReceiverResult result = await RunAsync(storage,
            (stream, ct) => SeedSession.RunAsync(stream, Guid.NewGuid(), source, ct));

        Assert.Equal(ReceiverStatus.Completed, result.Status);
        Assert.Equal(indices.Length, result.VerifiedPieces);
        Assert.Equal(indices.Length, result.TotalPieces);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(output));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), manifest.FileId);
        BlockRequest[] expected = fileLength == 0 ? [] :
            [new(0, 0, 4), new(0, 4, 4), new(1, 0, 4), new(1, 4, 4), new(2, 0, 3)];
        Assert.Equal(expected, source.Requests);
    }

    [Fact]
    public async Task ReportsMissingSourceAfterDownloadingAllAvailablePieces()
    {
        byte[] bytes = Enumerable.Range(0, 19).Select(i => (byte)i).ToArray();
        string input = Path.Combine(_directory, "partial-source.bin");
        await File.WriteAllBytesAsync(input, bytes);
        var manifest = await FileManifest.CreateFromFileAsync(input, 8, 4);
        using var seed = new FileStorage(input, manifest, isSeed: true);
        var source = new StorageSeed(seed, [0, 2]);
        using var storage = new FileStorage(Path.Combine(_directory, "partial.bin"), manifest);

        ReceiverResult result = await RunAsync(storage,
            (stream, ct) => SeedSession.RunAsync(stream, Guid.NewGuid(), source, ct));

        Assert.Equal(ReceiverStatus.MissingSource, result.Status);
        Assert.Equal(2, result.VerifiedPieces);
        Assert.Equal(3, result.TotalPieces);
        Assert.False(storage.IsPieceVerified(1));
        Assert.Equal(new BlockRequest[] { new(0, 0, 4), new(0, 4, 4), new(2, 0, 3) }, source.Requests);
    }

    [Theory]
    [InlineData("id", ReceiverStatus.ProtocolError)]
    [InlineData("piece", ReceiverStatus.ProtocolError)]
    [InlineData("offset", ReceiverStatus.ProtocolError)]
    [InlineData("length", ReceiverStatus.ProtocolError)]
    [InlineData("payload-length", ReceiverStatus.ProtocolError)]
    [InlineData("eof", ReceiverStatus.Disconnected)]
    [InlineData("timeout", ReceiverStatus.TimedOut)]
    [InlineData("error", ReceiverStatus.PeerError)]
    [InlineData("hash", ReceiverStatus.IntegrityFailed)]
    public async Task StopsOnFailedBlockAndDoesNotVerifyPiece(string scenario, ReceiverStatus expected)
    {
        string input = Path.Combine(_directory, "seed.bin");
        await File.WriteAllBytesAsync(input, "ABCD"u8.ToArray());
        var manifest = await FileManifest.CreateFromFileAsync(input, 4, 4);
        using var storage = new FileStorage(Path.Combine(_directory, "failure.bin"), manifest);

        ReceiverResult result = await RunAsync(storage, ServeAsync,
            scenario == "timeout" ? TimeSpan.FromSeconds(1) : null);

        Assert.Equal(expected, result.Status);
        Assert.Equal(0, result.VerifiedPieces);
        Assert.False(storage.IsPieceVerified(0));
        Assert.False(storage.IsPieceFullyWritten(0));

        async Task ServeAsync(NetworkStream stream, CancellationToken ct)
        {
            Frame hello = Assert.IsType<Frame>(await FrameTransport.ReadAsync(stream, ct));
            var helloOk = JsonSerializer.SerializeToUtf8Bytes(
                new { peerId = Guid.NewGuid().ToString("D"), fileId = manifest.FileId });
            await FrameTransport.WriteAsync(stream, 2, hello.Header.RequestId, helloOk, ct);
            await FrameTransport.WriteAsync(stream, 3, 0, new byte[] { 0x80 }, ct);

            Frame request = Assert.IsType<Frame>(await FrameTransport.ReadAsync(stream, ct));
            Assert.Equal(5, request.Header.Type);
            if (scenario == "eof")
                return;
            if (scenario == "timeout")
            {
                Assert.Null(await FrameTransport.ReadAsync(stream, ct));
                return;
            }
            if (scenario == "error")
            {
                await FrameTransport.WriteAsync(stream, 8, request.Header.RequestId,
                    """{"code":"PIECE_UNAVAILABLE","message":"Piece is no longer available."}"""u8.ToArray(), ct);
                return;
            }

            var payload = new byte[scenario == "payload-length" ? 17 : 16];
            request.Payload.CopyTo(payload);
            "XBCD"u8.CopyTo(payload.AsSpan(12));
            switch (scenario)
            {
                case "piece": payload[3] = 1; break;
                case "offset": payload[7] = 1; break;
                case "length": payload[11] = 3; break;
            }
            uint id = scenario == "id" ? request.Header.RequestId + 1 : request.Header.RequestId;
            await FrameTransport.WriteAsync(stream, 6, id, payload, ct);
            // Không có HAVE hoặc yêu cầu mới sau block bị từ chối.
            Assert.Null(await FrameTransport.ReadAsync(stream, ct));
        }
    }

    private static async Task<ReceiverResult> RunAsync(
        FileStorage storage, Func<NetworkStream, CancellationToken, Task> serve, TimeSpan? networkTimeout = null)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task seed = ServeAsync();
        Task<ReceiverResult> receiver = SequentialReceiver.DownloadAsync(
            "127.0.0.1", port, Guid.NewGuid(), storage, networkTimeout ?? TimeSpan.FromSeconds(5), limit.Token);
        await Task.WhenAll(seed, receiver);
        return await receiver;

        async Task ServeAsync()
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(limit.Token);
            await serve(client.GetStream(), limit.Token);
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class StorageSeed(FileStorage storage, int[] available) : ISeedSource
    {
        public string FileId => storage.Manifest.FileId;
        public int PieceCount => storage.Layout.PieceCount;
        public List<BlockRequest> Requests { get; } = [];
        public bool IsVerified(int pieceIndex) => available.Contains(pieceIndex);

        public async ValueTask<BlockReadResult> ReadBlockAsync(BlockRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            int piece = checked((int)request.PieceIndex);
            int block = checked((int)request.OffsetInPiece) / storage.Layout.BlockLength;
            var range = storage.Layout.GetBlockRange(piece, block);
            if (range.OffsetInPiece != request.OffsetInPiece || range.Length != request.Length)
                return new BlockReadResult(BlockReadStatus.InvalidRange, ReadOnlyMemory<byte>.Empty);
            if (!IsVerified(piece))
                return new BlockReadResult(BlockReadStatus.PieceUnavailable, ReadOnlyMemory<byte>.Empty);

            var (result, data) = await storage.ReadBlockAsync(piece, block, cancellationToken);
            if (!result.IsSuccess)
                throw new IOException(result.ErrorMessage);
            return new BlockReadResult(BlockReadStatus.Available, data);
        }
    }
}
