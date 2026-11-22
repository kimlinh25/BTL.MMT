using System;
using ET4230.Core.Discovery;
using ET4230.Core.Protocol;
using Xunit;

namespace ET4230.Core.Tests.Discovery;

public class TrackerRequestDispatcherTests
{
    private static TrackerRequestDispatcher CreateDispatcher(
        out FakeClock clock,
        int maxEntries = 10,
        int maxPeersPerResponse = 100)
    {
        clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), maxEntries);
        return new TrackerRequestDispatcher(registry, maxPeersPerResponse);
    }

    private static FrameHeader MakeHeader(byte type, int payloadLength, uint requestId = 1) =>
        new(TrackerProtocol.Version, type, TrackerProtocol.DefaultFlags, requestId, (uint)payloadLength);

    [Fact]
    public void Dispatch_RegisterHopLe_TraAckVaLuuVaoRegistry()
    {
        var dispatcher = CreateDispatcher(out _);
        var payload = TrackerProtocol.SerializeJson(new RegisterPayload("peerA", "192.168.1.10", 42301, "file1"));

        var res = dispatcher.Dispatch(MakeHeader(TrackerProtocol.TypeRegister, payload.Length, requestId: 7), payload);

        Assert.Equal(TrackerProtocol.TypeAck, res.ResponseHeader.Type);
        Assert.Equal(7u, res.ResponseHeader.RequestId);
        Assert.False(res.CloseConnection);
        Assert.True(TrackerProtocol.TryDeserializeJson<AckPayload>(res.ResponsePayload, out var ack));
        Assert.True(ack?.Ok);
    }

    [Theory]
    [InlineData("0.0.0.0", 42301)]
    [InlineData("   ", 42301)]
    [InlineData("192.168.1.10", 0)]
    [InlineData("192.168.1.10", 65536)]
    public void Dispatch_RegisterSaiRangeHoacHost_TraErrorInvalidRequest(string host, int port)
    {
        var dispatcher = CreateDispatcher(out _);
        var payload = TrackerProtocol.SerializeJson(new RegisterPayload("peerA", host, port, "file1"));

        var res = dispatcher.Dispatch(MakeHeader(TrackerProtocol.TypeRegister, payload.Length), payload);

        Assert.Equal(TrackerProtocol.TypeError, res.ResponseHeader.Type);
        Assert.True(TrackerProtocol.TryDeserializeJson<ErrorPayload>(res.ResponsePayload, out var err));
        Assert.Equal(TrackerProtocol.ErrorInvalidRequest, err?.Code);
    }

    [Fact]
    public void Dispatch_RegisterJsonHong_TraErrorInvalidRequest()
    {
        var dispatcher = CreateDispatcher(out _);
        var badJson = "{not-valid-json"u8.ToArray();

        var res = dispatcher.Dispatch(MakeHeader(TrackerProtocol.TypeRegister, badJson.Length), badJson);

        Assert.Equal(TrackerProtocol.TypeError, res.ResponseHeader.Type);
        Assert.True(TrackerProtocol.TryDeserializeJson<ErrorPayload>(res.ResponsePayload, out var err));
        Assert.Equal(TrackerProtocol.ErrorInvalidRequest, err?.Code);
    }

    [Fact]
    public void Dispatch_SaiRolePeerMessage_TraProtocolErrorVaDongKetNoi()
    {
        var dispatcher = CreateDispatcher(out _);
        var payload = new byte[12]; // Giả lập REQUEST_BLOCK (type 5) gửi nhầm vào tracker

        var res = dispatcher.Dispatch(MakeHeader(type: 5, payloadLength: payload.Length, requestId: 9), payload);

        Assert.Equal(TrackerProtocol.TypeError, res.ResponseHeader.Type);
        Assert.True(res.CloseConnection);
        Assert.True(TrackerProtocol.TryDeserializeJson<ErrorPayload>(res.ResponsePayload, out var err));
        Assert.Equal(TrackerProtocol.ErrorProtocolError, err?.Code);
    }

    [Fact]
    public void Dispatch_Heartbeat_GiaHanKhiConHan_VaTraPeerNotFoundKhiHetHan()
    {
        var dispatcher = CreateDispatcher(out var clock);
        var regPayload = TrackerProtocol.SerializeJson(new RegisterPayload("peerA", "192.168.1.10", 42301, "file1"));
        dispatcher.Dispatch(MakeHeader(TrackerProtocol.TypeRegister, regPayload.Length, 1), regPayload);

        clock.Advance(TimeSpan.FromSeconds(10));
        var hbPayload = TrackerProtocol.SerializeJson(new HeartbeatPayload("peerA", "file1"));
        var okRes = dispatcher.Dispatch(MakeHeader(TrackerProtocol.TypeHeartbeat, hbPayload.Length, 2), hbPayload);
        Assert.Equal(TrackerProtocol.TypeAck, okRes.ResponseHeader.Type);

        // Hết hạn ở age >= 20s kể từ heartbeat ở t=10
        clock.Advance(TimeSpan.FromSeconds(20));
        var expiredRes = dispatcher.Dispatch(MakeHeader(TrackerProtocol.TypeHeartbeat, hbPayload.Length, 3), hbPayload);
        Assert.Equal(TrackerProtocol.TypeError, expiredRes.ResponseHeader.Type);
        Assert.True(TrackerProtocol.TryDeserializeJson<ErrorPayload>(expiredRes.ResponsePayload, out var err));
        Assert.Equal(TrackerProtocol.ErrorPeerNotFound, err?.Code);
    }

    [Fact]
    public void Dispatch_ListPeers_CatTheoMaxPeersPerResponse()
    {
        var dispatcher = CreateDispatcher(out _, maxEntries: 10, maxPeersPerResponse: 2);

        for (int i = 1; i <= 3; i++)
        {
            var reg = TrackerProtocol.SerializeJson(new RegisterPayload($"peer{i}", $"192.168.1.{i}", 42300 + i, "file1"));
            dispatcher.Dispatch(MakeHeader(TrackerProtocol.TypeRegister, reg.Length, (uint)i), reg);
        }

        var listPayload = TrackerProtocol.SerializeJson(new ListPeersPayload("file1"));
        var res = dispatcher.Dispatch(MakeHeader(TrackerProtocol.TypeListPeers, listPayload.Length, 10), listPayload);

        Assert.Equal(TrackerProtocol.TypePeerList, res.ResponseHeader.Type);
        Assert.True(TrackerProtocol.TryDeserializeJson<PeerListPayload>(res.ResponsePayload, out var peerList));
        Assert.NotNull(peerList);
        Assert.Equal(2, peerList!.Peers.Count);
    }
}
