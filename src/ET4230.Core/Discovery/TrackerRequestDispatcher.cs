using System;
using System.Collections.Generic;
using ET4230.Core.Protocol;

namespace ET4230.Core.Discovery;

public sealed record TrackerDispatchResult(
    FrameHeader ResponseHeader,
    byte[] ResponsePayload,
    bool CloseConnection = false);

/// <summary>
/// Bộ điều phối thuần logic cho Tracker: kiểm tra role/range/JSON payload theo v1
/// rồi gọi <see cref="PeerRegistry"/> và đóng gói phản hồi ACK / PEER_LIST / ERROR.
/// </summary>
public sealed class TrackerRequestDispatcher
{
    private readonly PeerRegistry _registry;
    private readonly int _maxPeersPerResponse;
    private readonly int _maxControlPayloadBytes;

    public TrackerRequestDispatcher(
        PeerRegistry registry,
        int maxPeersPerResponse = 100,
        int maxControlPayloadBytes = TrackerProtocol.MaxControlJsonBytes)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (maxPeersPerResponse <= 0) throw new ArgumentOutOfRangeException(nameof(maxPeersPerResponse));
        if (maxControlPayloadBytes <= 0 || maxControlPayloadBytes > TrackerProtocol.MaxControlJsonBytes)
            throw new ArgumentOutOfRangeException(nameof(maxControlPayloadBytes));

        _maxPeersPerResponse = maxPeersPerResponse;
        _maxControlPayloadBytes = maxControlPayloadBytes;
    }

    public TrackerDispatchResult Dispatch(FrameHeader requestHeader, ReadOnlySpan<byte> payload)
    {
        if (requestHeader.Version != TrackerProtocol.Version || requestHeader.Flags != TrackerProtocol.DefaultFlags)
        {
            return CreateError(
                requestHeader.RequestId,
                TrackerProtocol.ErrorProtocolError,
                "Version hoặc flags không hợp lệ với giao thức v1.",
                closeConnection: true);
        }

        if (requestHeader.RequestId == 0)
        {
            return CreateError(
                0,
                TrackerProtocol.ErrorInvalidRequest,
                "Request tới tracker phải có RequestId khác 0.",
                closeConnection: true);
        }

        if (payload.Length == 0 || payload.Length > _maxControlPayloadBytes)
        {
            return CreateError(
                requestHeader.RequestId,
                TrackerProtocol.ErrorInvalidRequest,
                $"Payload điều khiển phải nằm trong 1..{_maxControlPayloadBytes} byte.",
                closeConnection: true);
        }

        return requestHeader.Type switch
        {
            TrackerProtocol.TypeRegister => HandleRegister(requestHeader.RequestId, payload),
            TrackerProtocol.TypeHeartbeat => HandleHeartbeat(requestHeader.RequestId, payload),
            TrackerProtocol.TypeListPeers => HandleListPeers(requestHeader.RequestId, payload),
            _ => CreateError(
                requestHeader.RequestId,
                TrackerProtocol.ErrorProtocolError,
                $"Type {requestHeader.Type} không hợp lệ cho vai trò tracker.",
                closeConnection: true)
        };
    }

    private TrackerDispatchResult HandleRegister(uint requestId, ReadOnlySpan<byte> payload)
    {
        if (!TrackerProtocol.TryDeserializeJson<RegisterPayload>(payload, out var req) || req is null)
            return CreateError(requestId, TrackerProtocol.ErrorInvalidRequest, "Payload REGISTER không phải JSON hợp lệ.");

        if (string.IsNullOrWhiteSpace(req.FileId) ||
            string.IsNullOrWhiteSpace(req.PeerId) ||
            string.IsNullOrWhiteSpace(req.Host))
        {
            return CreateError(requestId, TrackerProtocol.ErrorInvalidRequest, "REGISTER thiếu fileId, peerId hoặc host.");
        }

        if (req.Port <= 0 || req.Port > 65535)
            return CreateError(requestId, TrackerProtocol.ErrorInvalidRequest, "Port ngoài dải hợp lệ (1..65535).");

        if (string.Equals(req.Host.Trim(), "0.0.0.0", StringComparison.Ordinal))
            return CreateError(requestId, TrackerProtocol.ErrorInvalidRequest, "Không được công bố IP wildcard 0.0.0.0.");

        var endpoint = new PeerEndpoint(req.PeerId.Trim(), req.Host.Trim(), req.Port);
        bool accepted = _registry.Register(req.FileId.Trim(), endpoint);

        return accepted
            ? CreateAck(requestId)
            : CreateError(requestId, TrackerProtocol.ErrorRegistryFull, "Registry đã đạt giới hạn sức chứa.");
    }

    private TrackerDispatchResult HandleHeartbeat(uint requestId, ReadOnlySpan<byte> payload)
    {
        if (!TrackerProtocol.TryDeserializeJson<HeartbeatPayload>(payload, out var req) || req is null)
            return CreateError(requestId, TrackerProtocol.ErrorInvalidRequest, "Payload HEARTBEAT không phải JSON hợp lệ.");

        if (string.IsNullOrWhiteSpace(req.FileId) || string.IsNullOrWhiteSpace(req.PeerId))
            return CreateError(requestId, TrackerProtocol.ErrorInvalidRequest, "HEARTBEAT thiếu fileId hoặc peerId.");

        bool renewed = _registry.Heartbeat(req.FileId.Trim(), req.PeerId.Trim());
        return renewed
            ? CreateAck(requestId)
            : CreateError(requestId, TrackerProtocol.ErrorPeerNotFound, "Peer chưa đăng ký hoặc đã hết hạn TTL.");
    }

    private TrackerDispatchResult HandleListPeers(uint requestId, ReadOnlySpan<byte> payload)
    {
        if (!TrackerProtocol.TryDeserializeJson<ListPeersPayload>(payload, out var req) || req is null)
            return CreateError(requestId, TrackerProtocol.ErrorInvalidRequest, "Payload LIST_PEERS không phải JSON hợp lệ.");

        if (string.IsNullOrWhiteSpace(req.FileId))
            return CreateError(requestId, TrackerProtocol.ErrorInvalidRequest, "LIST_PEERS thiếu fileId.");

        var active = _registry.ListActive(req.FileId.Trim());
        int takeCount = Math.Min(active.Count, _maxPeersPerResponse);

        var capped = new List<PeerEndpoint>(takeCount);
        for (int i = 0; i < takeCount; i++)
            capped.Add(active[i]);

        // Đảm bảo payload PEER_LIST không vượt quá giới hạn control JSON
        byte[] jsonBytes = TrackerProtocol.SerializeJson(new PeerListPayload(capped));
        while (jsonBytes.Length > _maxControlPayloadBytes && capped.Count > 0)
        {
            int nextCount = Math.Max(0, capped.Count - Math.Max(1, capped.Count / 4));
            capped.RemoveRange(nextCount, capped.Count - nextCount);
            jsonBytes = TrackerProtocol.SerializeJson(new PeerListPayload(capped));
        }

        var header = new FrameHeader(
            TrackerProtocol.Version,
            TrackerProtocol.TypePeerList,
            TrackerProtocol.DefaultFlags,
            requestId,
            (uint)jsonBytes.Length);

        return new TrackerDispatchResult(header, jsonBytes);
    }

    private static TrackerDispatchResult CreateAck(uint requestId)
    {
        byte[] jsonBytes = TrackerProtocol.SerializeJson(new AckPayload(true));
        var header = new FrameHeader(
            TrackerProtocol.Version,
            TrackerProtocol.TypeAck,
            TrackerProtocol.DefaultFlags,
            requestId,
            (uint)jsonBytes.Length);
        return new TrackerDispatchResult(header, jsonBytes);
    }

    private static TrackerDispatchResult CreateError(
        uint requestId,
        string code,
        string message,
        bool closeConnection = false)
    {
        byte[] jsonBytes = TrackerProtocol.SerializeJson(new ErrorPayload(code, message));
        var header = new FrameHeader(
            TrackerProtocol.Version,
            TrackerProtocol.TypeError,
            TrackerProtocol.DefaultFlags,
            requestId,
            (uint)jsonBytes.Length);
        return new TrackerDispatchResult(header, jsonBytes, closeConnection);
    }
}
