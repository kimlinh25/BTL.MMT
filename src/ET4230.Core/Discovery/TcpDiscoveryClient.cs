using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ET4230.Core.Protocol;

namespace ET4230.Core.Discovery;

public interface IDiscoveryClient
{
    Task RegisterAsync(string fileId, CancellationToken cancellationToken = default);
    Task<bool> HeartbeatAsync(string fileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PeerEndpoint>> ListPeersAsync(string fileId, CancellationToken cancellationToken = default);
}

public sealed class TrackerRequestException : Exception
{
    public string ErrorCode { get; }

    public TrackerRequestException(string errorCode, string message)
        : base($"Tracker error [{errorCode}]: {message}")
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Discovery Client kết nối TCP tới Tracker theo Giao-thuc-v1.md để REGISTER,
/// HEARTBEAT và LIST_PEERS (tự lọc chính mình và các endpoint không hợp lệ).
/// </summary>
public sealed class TcpDiscoveryClient : IDiscoveryClient
{
    private readonly DiscoveryClientOptions _options;
    private uint _nextRequestId;

    public TcpDiscoveryClient(DiscoveryClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task RegisterAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("FileId không được rỗng.", nameof(fileId));

        var payload = TrackerProtocol.SerializeJson(new RegisterPayload(
            _options.SelfPeerId.Trim(),
            _options.AdvertisedHost.Trim(),
            _options.AdvertisedPort,
            fileId.Trim()));

        var response = await SendRequestAsync(TrackerProtocol.TypeRegister, payload, cancellationToken)
            .ConfigureAwait(false);

        EnsureAckResponse(response);
    }

    public async Task<bool> HeartbeatAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("FileId không được rỗng.", nameof(fileId));

        var payload = TrackerProtocol.SerializeJson(new HeartbeatPayload(
            _options.SelfPeerId.Trim(),
            fileId.Trim()));

        var response = await SendRequestAsync(TrackerProtocol.TypeHeartbeat, payload, cancellationToken)
            .ConfigureAwait(false);

        if (response.Header.Type == TrackerProtocol.TypeError)
        {
            if (TrackerProtocol.TryDeserializeJson<ErrorPayload>(response.Payload, out var err) &&
                err is not null &&
                string.Equals(err.Code, TrackerProtocol.ErrorPeerNotFound, StringComparison.Ordinal))
            {
                return false;
            }

            ThrowTrackerError(response.Payload);
        }

        EnsureAckResponse(response);
        return true;
    }

    public async Task<IReadOnlyList<PeerEndpoint>> ListPeersAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("FileId không được rỗng.", nameof(fileId));

        var payload = TrackerProtocol.SerializeJson(new ListPeersPayload(fileId.Trim()));

        var response = await SendRequestAsync(TrackerProtocol.TypeListPeers, payload, cancellationToken)
            .ConfigureAwait(false);

        if (response.Header.Type == TrackerProtocol.TypeError)
            ThrowTrackerError(response.Payload);

        if (response.Header.Type != TrackerProtocol.TypePeerList)
            throw new InvalidDataException($"Phản hồi không đúng type PEER_LIST (nhận type {response.Header.Type}).");

        if (!TrackerProtocol.TryDeserializeJson<PeerListPayload>(response.Payload, out var peerList) ||
            peerList is null ||
            peerList.Peers is null)
        {
            return Array.Empty<PeerEndpoint>();
        }

        return FilterCandidatePeers(peerList.Peers, _options.SelfPeerId);
    }

    /// <summary>
    /// Chạy vòng lặp gửi HEARTBEAT định kỳ cho tới khi bị hủy qua <paramref name="cancellationToken"/>.
    /// Nếu tracker báo PEER_NOT_FOUND (hết hạn hoặc tracker restart), tự động thử Register lại.
    /// </summary>
    public async Task RunHeartbeatLoopAsync(string fileId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);

            bool renewed = await HeartbeatAsync(fileId, cancellationToken).ConfigureAwait(false);
            if (!renewed)
            {
                await RegisterAsync(fileId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static IReadOnlyList<PeerEndpoint> FilterCandidatePeers(
        IEnumerable<PeerEndpoint> rawPeers,
        string selfPeerId)
    {
        var selfId = selfPeerId.Trim();
        var result = new List<PeerEndpoint>();

        foreach (var peer in rawPeers)
        {
            if (peer is null)
                continue;
            if (string.IsNullOrWhiteSpace(peer.PeerId) || string.IsNullOrWhiteSpace(peer.Host))
                continue;
            if (string.Equals(peer.PeerId.Trim(), selfId, StringComparison.Ordinal))
                continue;
            if (string.Equals(peer.Host.Trim(), "0.0.0.0", StringComparison.Ordinal))
                continue;
            if (peer.Port <= 0 || peer.Port > 65535)
                continue;

            result.Add(peer);
        }

        return result;
    }

    private async Task<FrameMessage> SendRequestAsync(
        byte messageType,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.RequestTimeout);
        var ct = timeoutCts.Token;

        uint requestId = Interlocked.Increment(ref _nextRequestId);
        if (requestId == 0)
            requestId = Interlocked.Increment(ref _nextRequestId);

        var header = new FrameHeader(
            TrackerProtocol.Version,
            messageType,
            TrackerProtocol.DefaultFlags,
            requestId,
            (uint)payload.Length);

        using var client = new TcpClient();
        await client.ConnectAsync(_options.TrackerHost.Trim(), _options.TrackerPort, ct).ConfigureAwait(false);

        await using var stream = client.GetStream();
        await FrameStreamHelper.WriteFrameAsync(stream, header, payload, ct).ConfigureAwait(false);

        var response = await FrameStreamHelper.ReadFrameAsync(
            stream,
            TrackerProtocol.MaxControlJsonBytes,
            ct).ConfigureAwait(false);

        if (response is null)
            throw new EndOfStreamException("Tracker đóng kết nối trước khi gửi phản hồi.");

        if (response.Header.RequestId != requestId)
            throw new InvalidDataException($"RequestId phản hồi ({response.Header.RequestId}) không khớp yêu cầu ({requestId}).");

        return response;
    }

    private static void EnsureAckResponse(FrameMessage response)
    {
        if (response.Header.Type == TrackerProtocol.TypeError)
            ThrowTrackerError(response.Payload);

        if (response.Header.Type != TrackerProtocol.TypeAck)
            throw new InvalidDataException($"Phản hồi không đúng type ACK (nhận type {response.Header.Type}).");

        if (!TrackerProtocol.TryDeserializeJson<AckPayload>(response.Payload, out var ack) || ack is null || !ack.Ok)
            throw new InvalidDataException("Payload ACK không hợp lệ.");
    }

    private static void ThrowTrackerError(byte[] errorPayload)
    {
        if (TrackerProtocol.TryDeserializeJson<ErrorPayload>(errorPayload, out var err) && err is not null)
            throw new TrackerRequestException(err.Code, err.Message);

        throw new TrackerRequestException(TrackerProtocol.ErrorProtocolError, "Tracker trả về lỗi không đọc được payload.");
    }
}
