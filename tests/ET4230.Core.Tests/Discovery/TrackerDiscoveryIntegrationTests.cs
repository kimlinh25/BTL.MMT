using System;
using System.Threading.Tasks;
using ET4230.Core.Discovery;
using Xunit;

namespace ET4230.Core.Tests.Discovery;

public class TrackerDiscoveryIntegrationTests
{
    [Fact]
    public void FilterCandidatePeers_LocChinhMinhVaEndpointKhongHopLe()
    {
        var raw = new[]
        {
            new PeerEndpoint("peerSelf", "192.168.1.10", 42301),
            new PeerEndpoint("peerBadWildcard", "0.0.0.0", 42302),
            new PeerEndpoint("peerBadPort", "192.168.1.11", 0),
            new PeerEndpoint("peerGood", "192.168.1.12", 42303)
        };

        var filtered = TcpDiscoveryClient.FilterCandidatePeers(raw, "peerSelf");

        var only = Assert.Single(filtered);
        Assert.Equal("peerGood", only.PeerId);
        Assert.Equal("192.168.1.12", only.Host);
        Assert.Equal(42303, only.Port);
    }

    [Fact]
    public async Task TrackerServer_Va_TcpDiscoveryClient_EndToEnd_HoatDongDungTTL()
    {
        var clock = new FakeClock();
        var serverOptions = new TrackerOptions
        {
            BindHost = "127.0.0.1",
            BindPort = 0, // OS tự cấp port trống
            Ttl = TimeSpan.FromSeconds(20),
            MaxEntries = 10
        };

        await using var server = new TrackerServer(serverOptions, clock);
        server.Start();

        var peerA = new TcpDiscoveryClient(new DiscoveryClientOptions
        {
            TrackerHost = "127.0.0.1",
            TrackerPort = server.BoundPort,
            SelfPeerId = "peerA",
            AdvertisedHost = "127.0.0.1",
            AdvertisedPort = 42301
        });

        var peerB = new TcpDiscoveryClient(new DiscoveryClientOptions
        {
            TrackerHost = "127.0.0.1",
            TrackerPort = server.BoundPort,
            SelfPeerId = "peerB",
            AdvertisedHost = "127.0.0.1",
            AdvertisedPort = 42302
        });

        // 1. Peer A và B cùng register file1
        await peerA.RegisterAsync("file1");
        await peerB.RegisterAsync("file1");

        // 2. Peer B hỏi danh sách: phải thấy Peer A và đã lọc chính B
        var fromB = await peerB.ListPeersAsync("file1");
        var onlyA = Assert.Single(fromB);
        Assert.Equal("peerA", onlyA.PeerId);
        Assert.Equal(42301, onlyA.Port);

        // 3. Nhảy tới t=15s, chỉ Peer A heartbeat; Peer B im lặng
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.True(await peerA.HeartbeatAsync("file1"));

        // 4. Nhảy tiếp 5s (t=20s): Peer B hết hạn, Peer A vẫn sống
        clock.Advance(TimeSpan.FromSeconds(5));
        var fromA = await peerA.ListPeersAsync("file1");
        Assert.Empty(fromA); // Chỉ còn A nhưng A tự lọc chính mình

        // 5. Nhảy tiếp 15s (t=35s, 20s sau heartbeat của A): A cũng hết hạn
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.False(await peerA.HeartbeatAsync("file1"));
        Assert.Empty(await peerB.ListPeersAsync("file1"));

        await server.StopAsync();
    }
}
