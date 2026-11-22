using System.Net;
using System.Net.Sockets;
using System.Text;
using ET4230.Core.Discovery;
using ET4230.Core.Scheduling;
using ET4230.Core.Storage;
using ET4230.Core.Transport;

namespace ET4230.Cli;

internal static class W03Transfer
{
    public static async Task RunTrackerAsync(W03Config config, CancellationToken ct)
    {
        TrackerOptions options = config.TrackerOptions();
        await using var tracker = new TrackerServer(options);
        tracker.Start();
        Console.WriteLine($"TRACKER_READY {options.BindHost}:{tracker.BoundPort}");
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    public static async Task RunSeedAsync(W03Config config, CancellationToken ct)
    {
        Guid peerId = Guid.NewGuid();
        IPEndPoint endpoint = config.SeedEndpoint();
        var discovery = new TcpDiscoveryClient(config.DiscoveryOptions(peerId));
        string sourcePath = config.ResolvePath(config.File.SourcePath, "file.sourcePath");
        string manifestPath = config.ResolvePath(config.File.ManifestPath, "file.manifestPath");
        bool createManifest = !File.Exists(manifestPath);
        if (createManifest)
            config.ValidateNewManifestLayout();
        FileManifest manifest = createManifest
            ? await FileManifest.CreateFromFileAsync(
                sourcePath, config.Transfer.PieceSizeBytes, config.Transfer.BlockSizeBytes, ct)
            : await ReadManifestAsync(manifestPath, ct);
        ValidateWireLayout(manifest);

        using var storage = new FileStorage(sourcePath, manifest, isSeed: true);
        StorageResult verify = await storage.VerifyFullFileAsync(ct);
        if (verify.Status != StorageStatus.Verified)
            throw new InvalidDataException($"Tệp nguồn không khớp manifest. {verify.ErrorMessage ?? verify.Status.ToString()}");

        if (createManifest)
            await WriteManifestAsync(manifestPath, manifest, ct);

        using var listener = new TcpListener(endpoint);
        listener.Start();
        await discovery.RegisterAsync(manifest.FileId, ct);
        Console.WriteLine($"SEED_READY {config.Peer.AdvertiseHost}:{endpoint.Port} SHA256={manifest.FileId} manifest={manifestPath}");

        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task serve = ServeAsync(listener, peerId, new FileSeedSource(storage), session.Token);
        Task heartbeat = discovery.RunHeartbeatLoopAsync(manifest.FileId, session.Token);
        await Task.WhenAny(serve, heartbeat);
        session.Cancel();
        await Task.WhenAll(serve, heartbeat);
    }

    public static async Task RunDownloadAsync(W03Config config, CancellationToken ct)
    {
        string manifestPath = config.ResolvePath(config.File.ManifestPath, "file.manifestPath");
        string outputPath = config.ResolvePath(config.File.OutputPath, "file.outputPath");
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
            throw new IOException($"Đường dẫn đích đã tồn tại: {outputPath}");

        FileManifest manifest = await ReadManifestAsync(manifestPath, ct);
        ValidateWireLayout(manifest);
        Guid peerId = Guid.NewGuid();
        var discovery = new TcpDiscoveryClient(config.DiscoveryOptions(peerId));
        var peers = await discovery.ListPeersAsync(manifest.FileId, ct);
        if (peers.Count == 0)
            throw new IOException($"Tracker chưa có seed cho SHA-256 {manifest.FileId}.");

        PeerEndpoint source = peers[0];
        Console.WriteLine($"DOWNLOAD_SOURCE {source.Host}:{source.Port}");
        string partialPath = CreateTemporaryPath(outputPath);
        try
        {
            using (var storage = new FileStorage(partialPath, manifest))
            {
                ReceiverResult result = await SequentialReceiver.DownloadAsync(
                    source.Host, source.Port, peerId, storage, config.RequestTimeout(), ct);
                if (result.Status != ReceiverStatus.Completed)
                {
                    throw new IOException(
                        $"{result.Status} ({result.VerifiedPieces}/{result.TotalPieces} piece). {result.Detail}");
                }
            }

            ct.ThrowIfCancellationRequested();
            File.Move(partialPath, outputPath);
            Console.WriteLine($"DOWNLOAD_OK SHA256={manifest.FileId} bytes={manifest.FileSizeBytes} path={outputPath}");
        }
        finally
        {
            DeleteTemporaryFile(partialPath);
        }
    }

    private static async Task ServeAsync(
        TcpListener listener, Guid peerId, ISeedSource source, CancellationToken ct)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(ct);
            try
            {
                using NetworkStream stream = client.GetStream();
                await SeedSession.RunAsync(stream, peerId, source, ct);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or SocketException)
            {
                Console.Error.WriteLine($"Đóng phiên seed: {error.Message}");
            }
        }
    }

    private static async Task<FileManifest> ReadManifestAsync(string path, CancellationToken ct) =>
        FileManifest.FromJson(await File.ReadAllTextAsync(path, ct));

    private static async Task WriteManifestAsync(string path, FileManifest manifest, CancellationToken ct)
    {
        string temporaryPath = CreateTemporaryPath(path);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, manifest.ToJson(), Encoding.UTF8, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static void ValidateWireLayout(FileManifest manifest)
    {
        PieceLayout layout = manifest.CreateLayout();
        if (layout.BlockLength > 1024 * 1024 - 12 || ((long)layout.PieceCount + 7) / 8 > 1024 * 1024)
            throw new InvalidDataException("Layout vượt giới hạn payload v1.");
    }

    private static string CreateTemporaryPath(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".et4230-{Guid.NewGuid():N}.part");
        using (File.Open(temporaryPath, FileMode.CreateNew)) { }
        return temporaryPath;
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Không dọn được tệp tạm {path}: {error.Message}");
        }
    }
}
