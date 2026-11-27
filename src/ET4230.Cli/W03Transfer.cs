using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Globalization;
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
        config.ValidateUpload();
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

        await ShareAsync(config, endpoint, peerId, discovery, storage, Task.CompletedTask,
            $"SEED_READY {config.Peer.AdvertiseHost}:{endpoint.Port} SHA256={manifest.FileId} manifest={manifestPath}", ct);
    }

    public static async Task RunDownloadAsync(W03Config config, CancellationToken ct)
        => await RunReceiverAsync(config, share: false, ct);

    public static async Task RunPeerAsync(W03Config config, CancellationToken ct)
        => await RunReceiverAsync(config, share: true, ct);

    private static async Task RunReceiverAsync(W03Config config, bool share, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        string manifestPath = config.ResolvePath(config.File.ManifestPath, "file.manifestPath");
        string outputPath = config.ResolvePath(config.File.OutputPath, "file.outputPath");
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
            throw new IOException($"Đường dẫn đích đã tồn tại: {outputPath}");

        FileManifest manifest = await ReadManifestAsync(manifestPath, ct);
        ValidateWireLayout(manifest);
        Guid peerId = Guid.NewGuid();
        var discovery = new TcpDiscoveryClient(config.DiscoveryOptions(peerId));
        IPEndPoint? endpoint = share ? config.SeedEndpoint() : null;
        SwarmOptions? options = share ? config.SwarmOptions() : null;
        if (share)
            config.ValidateUpload();

        string partialPath = CreateTemporaryPath(outputPath);
        int retries = 0;
        Console.WriteLine($"RECEIVER_STARTED peerId={peerId} sharing={share}");
        try
        {
            using (var storage = new FileStorage(partialPath, manifest))
            {
                ReceiverResult result;
                if (endpoint is not null)
                {
                    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var firstPiece = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Task uploading = ShareAsync(config, endpoint, peerId, discovery, storage, firstPiece.Task,
                        $"PEER_READY {config.Peer.AdvertiseHost}:{endpoint.Port} SHA256={manifest.FileId}", lifetime.Token);
                    Task<ReceiverResult> downloading = SequentialReceiver.DownloadFromPeersAsync(
                        discovery, peerId, storage, options!, (piece, _) =>
                        {
                            Console.WriteLine($"PIECE_VERIFIED peerId={peerId} source={piece.SourcePeerId} piece={piece.PieceIndex} bytes={piece.Bytes} verified={piece.VerifiedPieces}/{storage.Layout.PieceCount}");
                            firstPiece.TrySetResult();
                            return ValueTask.CompletedTask;
                        }, detail =>
                        {
                            retries++;
                            Console.Error.WriteLine($"DOWNLOAD_RETRY peerId={peerId} {detail}");
                        }, lifetime.Token);
                    try
                    {
                        if (await Task.WhenAny(downloading, uploading) == uploading)
                            await uploading;
                        result = await downloading;
                    }
                    finally
                    {
                        await lifetime.CancelAsync();
                        await JoinAfterCancelAsync(Task.WhenAll(downloading, uploading), lifetime.Token);
                    }
                }
                else
                {
                    var peers = await discovery.ListPeersAsync(manifest.FileId, ct);
                    if (peers.Count == 0)
                        throw new IOException($"Tracker chưa có seed cho SHA-256 {manifest.FileId}.");
                    PeerEndpoint source = peers[0];
                    Console.WriteLine($"DOWNLOAD_SOURCE {source.Host}:{source.Port}");
                    result = await SequentialReceiver.DownloadAsync(
                        source.Host, source.Port, peerId, storage, config.RequestTimeout(), ct);
                }
                if (result.Status != ReceiverStatus.Completed)
                {
                    throw new IOException(
                        $"{result.Status} ({result.VerifiedPieces}/{result.TotalPieces} piece). {result.Detail}");
                }
            }

            ct.ThrowIfCancellationRequested();
            File.Move(partialPath, outputPath);
            Console.WriteLine($"DOWNLOAD_OK SHA256={manifest.FileId} bytes={manifest.FileSizeBytes} path={outputPath}");
            Console.WriteLine($"DOWNLOAD_METRICS peerId={peerId} seconds={elapsed.Elapsed.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture)} retries={retries}");

            if (endpoint is not null && config.Transfer.ShareAfterCompletionSeconds > 0 && manifest.FileSizeBytes > 0)
            {
                using var completedFile = new FileStorage(outputPath, manifest, isSeed: true);
                using var shareFor = CancellationTokenSource.CreateLinkedTokenSource(ct);
                shareFor.CancelAfter(TimeSpan.FromSeconds(config.Transfer.ShareAfterCompletionSeconds));
                await JoinAfterCancelAsync(ShareAsync(config, endpoint, peerId, discovery, completedFile,
                    Task.CompletedTask, $"PEER_COMPLETE_READY {endpoint}", shareFor.Token), shareFor.Token);
            }
        }
        finally
        {
            DeleteTemporaryFile(partialPath);
        }
    }

    private static async Task ShareAsync(
        W03Config config, IPEndPoint endpoint, Guid peerId, TcpDiscoveryClient discovery,
        FileStorage storage, Task readyToAdvertise, string readyMessage, CancellationToken ct)
    {
        using var listener = new TcpListener(endpoint);
        listener.Start(config.Peer.MaxUploadSessions);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var source = new PeerUpload(new FileSeedSource(storage), config.Strategy.ApplicationUploadLimitMbps);
        Task serving = PeerUpload.ServeAsync(listener, peerId, source, config.Peer.MaxUploadSessions,
            block => Console.WriteLine($"UPLOAD_BLOCK peerId={peerId} piece={block.PieceIndex} offset={block.OffsetInPiece} bytes={block.Length}"),
            lifetime.Token);
        Task? heartbeat = null;
        try
        {
            Task ready = readyToAdvertise.WaitAsync(lifetime.Token);
            if (await Task.WhenAny(ready, serving) == serving)
                await serving;
            await ready;
            await discovery.RegisterAsync(storage.Manifest.FileId, lifetime.Token);
            Console.WriteLine($"{readyMessage} peerId={peerId}");
            heartbeat = discovery.RunHeartbeatLoopAsync(storage.Manifest.FileId, lifetime.Token);
            Task finished = await Task.WhenAny(serving, heartbeat);
            await finished;
        }
        finally
        {
            await lifetime.CancelAsync();
            Task[] jobs = heartbeat is null ? [serving] : [serving, heartbeat];
            await JoinAfterCancelAsync(Task.WhenAll(jobs), lifetime.Token);
        }
    }

    private static async Task JoinAfterCancelAsync(Task task, CancellationToken token)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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
