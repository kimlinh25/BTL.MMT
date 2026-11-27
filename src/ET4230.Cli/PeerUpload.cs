using System.Diagnostics;
using System.Net.Sockets;
using ET4230.Core.Transport;

namespace ET4230.Cli;

internal sealed class PeerUpload(ISeedSource source, double megabitsPerSecond) : ISeedSource, IDisposable
{
    private readonly SemaphoreSlim _pacer = new(1, 1);
    private long _nextSendAt;

    public string FileId => source.FileId;
    public int PieceCount => source.PieceCount;
    public bool IsVerified(int pieceIndex) => source.IsVerified(pieceIndex);
    public bool[] SnapshotVerifiedPieces() => source.SnapshotVerifiedPieces();

    public async ValueTask<BlockReadResult> ReadBlockAsync(BlockRequest request, CancellationToken ct)
    {
        BlockReadResult block = await source.ReadBlockAsync(request, ct);
        if (block.Status != BlockReadStatus.Available || megabitsPerSecond == 0)
            return block;

        await _pacer.WaitAsync(ct);
        try
        {
            long now = Stopwatch.GetTimestamp();
            long blockTicks = (long)Math.Ceiling(
                block.Data.Length * 8d * Stopwatch.Frequency / (megabitsPerSecond * 1_000_000d));
            // Hạn chế tích lũy phần thời gian bị hệ điều hành đánh thức muộn.
            long previous = _nextSendAt == 0 ? now : Math.Max(_nextSendAt, now - Stopwatch.Frequency / 50);
            long next = previous + blockTicks;
            if (next > now)
            {
                double milliseconds = (next - now) * 1000d / Stopwatch.Frequency;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(milliseconds)), ct);
            }
            _nextSendAt = next;
        }
        finally
        {
            _pacer.Release();
        }
        return block;
    }

    public void Dispose() => _pacer.Dispose();

    public static async Task ServeAsync(
        TcpListener listener, Guid peerId, ISeedSource source, int parallelSessions,
        Action<BlockRequest> onBlockSent, CancellationToken ct)
    {
        using var workers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await Task.WhenAll(Enumerable.Range(0, parallelSessions).Select(_ => AcceptAsync()));

        async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(workers.Token);
                    await ServeClientAsync(client, peerId, source, onBlockSent, workers.Token);
                }
            }
            catch
            {
                await workers.CancelAsync();
                throw;
            }
        }
    }

    private static async Task ServeClientAsync(
        TcpClient client, Guid peerId, ISeedSource source, Action<BlockRequest> onBlockSent,
        CancellationToken ct)
    {
        try
        {
            using NetworkStream stream = client.GetStream();
            await SeedSession.RunAsync(stream, peerId, source, ct, onBlockSent, TimeSpan.FromSeconds(60));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Console.Error.WriteLine("Đóng phiên upload do hết thời gian chờ.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or SocketException)
        {
            Console.Error.WriteLine($"Đóng phiên upload: {error.Message}");
        }
    }
}
