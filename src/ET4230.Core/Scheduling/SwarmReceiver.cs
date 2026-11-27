using System.Net.Sockets;
using System.Text.Json;
using ET4230.Core.Discovery;
using ET4230.Core.Storage;
using ET4230.Core.Transport;

namespace ET4230.Core.Scheduling;

public sealed record SwarmOptions(
    TimeSpan RequestTimeout, TimeSpan DiscoveryInterval, int MaxSources = 4,
    int MaxIdleRounds = 3, bool RarestFirst = false);

public sealed record PieceReceived(Guid SourcePeerId, int PieceIndex, int Bytes, int VerifiedPieces);

public static partial class SequentialReceiver
{
    /// <summary>Điều phối một lượt tải; mỗi lần chỉ có một yêu cầu BLOCK đang chờ.</summary>
    public static async Task<ReceiverResult> DownloadFromPeersAsync(
        IDiscoveryClient discovery,
        Guid localPeerId,
        FileStorage storage,
        SwarmOptions options,
        Func<PieceReceived, CancellationToken, ValueTask>? onPiece = null,
        Action<string>? onRetry = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(options);
        if (localPeerId == Guid.Empty || options.MaxSources is < 1 or > 16 ||
            options.MaxIdleRounds is < 1 or > 30 || options.RequestTimeout <= TimeSpan.Zero ||
            options.RequestTimeout.TotalMilliseconds > uint.MaxValue - 1d ||
            options.DiscoveryInterval <= TimeSpan.Zero ||
            options.DiscoveryInterval.TotalMilliseconds > uint.MaxValue - 1d)
            throw new ArgumentException("PeerId hoặc cấu hình tải P2P không hợp lệ.");

        PieceLayout layout = storage.Layout;
        int count = layout.PieceCount;
        int bitfieldLength = (int)(((long)count + 7) / 8);
        if (bitfieldLength > MaxPayloadSize || layout.BlockLength > MaxPayloadSize - BlockFieldsSize)
            throw new ArgumentException("Layout vượt giới hạn payload v1.", nameof(storage));

        bool[] verified = storage.SnapshotVerifiedPieces();
        int finished = verified.Count(piece => piece);
        var sessions = new List<SwarmSession>(options.MaxSources);
        var failed = new HashSet<Guid>();
        int idleRounds = 0;
        string? lastFailure = null;
        long lastDiscovery = 0;

        try
        {
            while (finished < count)
            {
                ct.ThrowIfCancellationRequested();
                var (source, piece) = SelectSource(sessions, verified, options.RarestFirst);
                if (lastDiscovery == 0 ||
                    System.Diagnostics.Stopwatch.GetElapsedTime(lastDiscovery) >= options.DiscoveryInterval ||
                    source is null)
                {
                    if (source is null && lastDiscovery != 0)
                    {
                        if (++idleRounds > options.MaxIdleRounds)
                            return new(ReceiverStatus.MissingSource, finished, count,
                                lastFailure ?? "Không còn peer phục vụ piece còn thiếu.");
                        CloseSessions(sessions);
                        failed.Clear();
                        await Task.Delay(options.DiscoveryInterval, ct);
                    }

                    lastDiscovery = System.Diagnostics.Stopwatch.GetTimestamp();
                    IReadOnlyList<PeerEndpoint> peers;
                    try
                    {
                        peers = await discovery.ListPeersAsync(storage.Manifest.FileId, ct);
                    }
                    catch (Exception error) when (!ct.IsCancellationRequested && IsPeerFailure(error))
                    {
                        lastFailure = error.Message;
                        peers = [];
                    }

                    foreach (PeerEndpoint peer in peers)
                    {
                        if (sessions.Count == options.MaxSources)
                            break;
                        if (!Guid.TryParseExact(peer.PeerId, "D", out Guid id) || id == Guid.Empty ||
                            id == localPeerId || failed.Contains(id) || sessions.Any(x => x.Id == id))
                            continue;
                        try
                        {
                            var session = await SwarmSession.ConnectAsync(
                                peer, id, localPeerId, storage.Manifest.FileId, count, options.RequestTimeout, ct);
                            if (session.Offered.Where((offered, i) => offered && !verified[i]).Any())
                                sessions.Add(session);
                            else
                                session.Dispose();
                        }
                        catch (Exception error) when (!ct.IsCancellationRequested && IsPeerFailure(error))
                        {
                            failed.Add(id);
                            lastFailure = $"Peer {id}: {error.Message}";
                            onRetry?.Invoke(lastFailure);
                        }
                    }
                    (source, piece) = SelectSource(sessions, verified, options.RarestFirst);
                }

                if (source is null)
                    continue;

                try
                {
                    // Nếu phiên lỗi giữa piece, nguồn sau bắt đầu lại từ block 0.
                    for (int block = 0; block < layout.GetBlockCountInPiece(piece); block++)
                    {
                        var range = layout.GetBlockRange(piece, block);
                        ReadOnlyMemory<byte> data = await source.ReadBlockAsync(
                            piece, range.OffsetInPiece, range.Length, options.RequestTimeout, ct);
                        StorageResult written = await storage.WriteBlockAsync(piece, block, data, ct);
                        if (written.Status != StorageStatus.Ok)
                            return new(ReceiverStatus.StorageError, finished, count, written.ErrorMessage);
                    }
                }
                catch (Exception error) when (!ct.IsCancellationRequested && IsPeerFailure(error))
                {
                    RejectSource(source, error.Message);
                    continue;
                }

                StorageResult check = await storage.VerifyPieceAsync(piece, ct);
                if (check.Status == StorageStatus.HashMismatch)
                {
                    RejectSource(source, check.ErrorMessage ?? "Sai SHA-256 của piece.");
                    continue;
                }
                if (check.Status != StorageStatus.Verified)
                    return new(ReceiverStatus.StorageError, finished, count, check.ErrorMessage);

                verified[piece] = true;
                finished++;
                idleRounds = 0;
                if (onPiece is not null)
                    await onPiece(new(source.Id, piece, layout.GetPieceLength(piece), finished), ct);

                // Luân phiên các peer có cùng mức sẵn có.
                sessions.Remove(source);
                if (source.Offered.Where((offered, i) => offered && !verified[i]).Any())
                    sessions.Add(source);
                else
                    source.Dispose();
            }

            StorageResult full = await storage.VerifyFullFileAsync(ct);
            return full.Status == StorageStatus.Verified
                ? new(ReceiverStatus.Completed, finished, count)
                : new(StorageFailure(full), finished, count, full.ErrorMessage);
        }
        finally
        {
            CloseSessions(sessions);
        }

        void RejectSource(SwarmSession source, string detail)
        {
            sessions.Remove(source);
            source.Dispose();
            failed.Add(source.Id);
            lastFailure = $"Peer {source.Id}: {detail}";
            onRetry?.Invoke(lastFailure);
        }
    }

    private static (SwarmSession? Source, int Piece) SelectSource(
        List<SwarmSession> sessions, bool[] verified, bool rarestFirst)
    {
        int selected = -1;
        int bestCount = int.MaxValue;
        for (int piece = 0; piece < verified.Length; piece++)
        {
            if (verified[piece])
                continue;
            int owners = sessions.Count(session => session.Offered[piece]);
            if (owners == 0 || owners >= bestCount)
                continue;
            selected = piece;
            bestCount = owners;
            if (!rarestFirst || owners == 1)
                break;
        }

        if (selected < 0)
            return (null, -1);
        // Ưu tiên peer còn thiếu dữ liệu, giảm lượng tải trên seed ban đầu.
        return (sessions.Where(session => session.Offered[selected])
            .MinBy(session => session.AvailablePieces), selected);
    }

    private static void CloseSessions(List<SwarmSession> sessions)
    {
        foreach (SwarmSession session in sessions)
            session.Dispose();
        sessions.Clear();
    }

    private static bool IsPeerFailure(Exception error) =>
        error is IOException or SocketException or InvalidDataException or SeedErrorException
        or OperationCanceledException or TrackerRequestException or ArgumentException;

    private sealed class SwarmSession(TcpClient client, Guid id, bool[] offered) : IDisposable
    {
        private uint _requestId = 1;
        public Guid Id { get; } = id;
        public bool[] Offered { get; } = offered;
        public int AvailablePieces { get; } = offered.Count(piece => piece);

        public static async Task<SwarmSession> ConnectAsync(
            PeerEndpoint peer, Guid expectedId, Guid localId, string fileId,
            int pieceCount, TimeSpan timeout, CancellationToken ct)
        {
            var client = new TcpClient();
            try
            {
                using var deadline = CreateDeadline(timeout, ct);
                await client.ConnectAsync(peer.Host, peer.Port, deadline.Token);
                NetworkStream stream = client.GetStream();
                byte[] hello = JsonSerializer.SerializeToUtf8Bytes(new { peerId = localId.ToString("D"), fileId });
                await FrameTransport.WriteAsync(stream, Hello, 1, hello, deadline.Token);
                Frame reply = await ReadRequiredAsync(stream, deadline.Token);
                CheckPeerError(reply, 1);
                if (ValidateHello(reply, 1, fileId) != expectedId)
                    throw new InvalidDataException("PeerId trả về không khớp endpoint tracker.");
                Frame bitmap = await ReadRequiredAsync(stream, deadline.Token);
                CheckPeerError(bitmap, 1);
                var offered = new bool[pieceCount];
                ReadBitfield(bitmap, offered, (int)(((long)pieceCount + 7) / 8));
                return new(client, expectedId, offered);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public async Task<ReadOnlyMemory<byte>> ReadBlockAsync(
            int piece, int offset, int length, TimeSpan timeout, CancellationToken ct)
        {
            _requestId = _requestId == uint.MaxValue ? 1 : _requestId + 1;
            using var deadline = CreateDeadline(timeout, ct);
            return await RequestAsync(client.GetStream(), _requestId, piece, offset, length, Offered, deadline.Token);
        }

        public void Dispose() => client.Dispose();
    }
}
