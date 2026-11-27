using System.Net;
using System.Net.Sockets;
using ET4230.Core.Discovery;
using ET4230.Core.Scheduling;
using ET4230.Core.Storage;
using ET4230.Core.Transport;

namespace ET4230.Core.Tests.Scheduling;

public sealed class SwarmReceiverTests
{
    [Fact]
    public async Task ReturnsMissingSourceAfterConfiguredDiscoveryRounds()
    {
        var directory = Directory.CreateTempSubdirectory("et4230-no-sources-");
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string input = Path.Combine(directory.FullName, "seed.bin");
            await File.WriteAllBytesAsync(input, "ABCD"u8.ToArray(), limit.Token);
            var manifest = await FileManifest.CreateFromFileAsync(input, 4, 4, limit.Token);
            using var storage = new FileStorage(Path.Combine(directory.FullName, "download.part"), manifest);
            var discovery = new StaticDiscovery();
            ReceiverResult result = await SequentialReceiver.DownloadFromPeersAsync(discovery,
                Guid.NewGuid(), storage,
                new(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), MaxIdleRounds: 2), ct: limit.Token);

            Assert.Equal(ReceiverStatus.MissingSource, result.Status);
            Assert.Equal(0, result.VerifiedPieces);
            Assert.Equal(3, discovery.ListCalls);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SharesVerifiedPieceAndRecoversWhenThatSourceClosesMidPiece()
    {
        var directory = Directory.CreateTempSubdirectory("et4230-swarm-");
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            CancellationToken ct = limit.Token;
            byte[] bytes = "ABCDEFGHIJKLMNOPQRST"u8.ToArray();
            string input = Path.Combine(directory.FullName, "seed.bin");
            await File.WriteAllBytesAsync(input, bytes, ct);
            var manifest = await FileManifest.CreateFromFileAsync(input, 8, 4, ct);
            using var complete = new FileStorage(input, manifest, isSeed: true);
            using var downloading = new FileStorage(Path.Combine(directory.FullName, "peer-b.part"), manifest);
            using var output = new FileStorage(Path.Combine(directory.FullName, "peer-c.part"), manifest);

            for (int piece = 0; piece <= 1; piece++)
            {
                for (int block = 0; block < 2; block++)
                    Assert.Equal(StorageStatus.Ok,
                        (await downloading.WriteBlockAsync(piece, block, bytes.AsMemory(piece * 8 + block * 4, 4), ct)).Status);
                Assert.Equal(StorageStatus.Verified, (await downloading.VerifyPieceAsync(piece, ct)).Status);
            }

            var partial = new FileSeedSource(downloading);
            Assert.Equal(new[] { true, true, false }, partial.SnapshotVerifiedPieces());
            Assert.Equal(BlockReadStatus.PieceUnavailable,
                (await partial.ReadBlockAsync(new(2, 0, 4), ct)).Status);

            using var seedListener = new TcpListener(IPAddress.Loopback, 0);
            using var partialListener = new TcpListener(IPAddress.Loopback, 0);
            seedListener.Start();
            partialListener.Start();
            Guid seedId = Guid.NewGuid();
            Guid partialId = Guid.NewGuid();
            Task seedTask = ServeAsync(seedListener, seedId, new FileSeedSource(complete), ct);
            Task partialTask = ServeAsync(partialListener, partialId, new DisconnectingSource(partial), ct);
            var discovery = new StaticDiscovery(Endpoint(seedListener, seedId), Endpoint(partialListener, partialId));
            var received = new List<PieceReceived>();
            var retries = new List<string>();
            ReceiverResult result;
            try
            {
                result = await SequentialReceiver.DownloadFromPeersAsync(discovery, Guid.NewGuid(), output,
                    new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1)),
                    (piece, _) =>
                    {
                        received.Add(piece);
                        return ValueTask.CompletedTask;
                    }, retries.Add, ct);
            }
            finally
            {
                await limit.CancelAsync();
                await Task.WhenAll(seedTask, partialTask);
            }

            Assert.Equal(ReceiverStatus.Completed, result.Status);
            Assert.Equal(3, result.VerifiedPieces);
            Assert.Equal(new[] { partialId, seedId, seedId }, received.Select(p => p.SourcePeerId));
            Assert.Single(retries);
            output.Dispose();
            Assert.Equal(bytes, await File.ReadAllBytesAsync(output.TargetFilePath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static PeerEndpoint Endpoint(TcpListener listener, Guid id) =>
        new(id.ToString("D"), "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);

    private static async Task ServeAsync(TcpListener listener, Guid id, ISeedSource source, CancellationToken ct)
    {
        try
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(ct);
            await SeedSession.RunAsync(client.GetStream(), id, source, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (IOException) when (source is DisconnectingSource) { }
    }

    private sealed class StaticDiscovery(params PeerEndpoint[] peers) : IDiscoveryClient
    {
        public int ListCalls { get; private set; }
        public Task RegisterAsync(string fileId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HeartbeatAsync(string fileId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<PeerEndpoint>> ListPeersAsync(string fileId, CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<PeerEndpoint>>(peers);
        }
    }

    private sealed class DisconnectingSource(FileSeedSource source) : ISeedSource
    {
        public string FileId => source.FileId;
        public int PieceCount => source.PieceCount;
        public bool IsVerified(int pieceIndex) => source.IsVerified(pieceIndex);

        public ValueTask<BlockReadResult> ReadBlockAsync(BlockRequest request, CancellationToken ct) =>
            request.PieceIndex == 1 && request.OffsetInPiece > 0
                ? throw new IOException("Nguồn đóng phiên giữa piece.")
                : source.ReadBlockAsync(request, ct);
    }
}
