using System;
using System.Collections.Generic;
using Xunit; 
using ET4230.Core.Discovery;

namespace ET4230.Core.Tests.Discovery;

// 1. Tạo Đồng hồ giả (Fake Clock) cho phép nhảy cóc thời gian
public sealed class FakeClock : IElapsedClock
{
    public TimeSpan Elapsed { get; set; }
    public void Advance(TimeSpan delta) => Elapsed += delta;
}

public class PeerRegistryTests
{
    [Fact]
    public void Register_CungPeerId_HaiFileId_KhongDeNhau()
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);
        
        registry.Register("file1", new PeerEndpoint("peerA", "192.168.1.1", 42301));
        registry.Register("file2", new PeerEndpoint("peerA", "192.168.1.1", 42302));

        // Khóa kép giúp 2 file độc lập, List ra phải có đủ[cite: 3]
        Assert.Single(registry.ListActive("file1"));
        Assert.Single(registry.ListActive("file2"));
    }

    [Fact]
    public void Heartbeat_HetHanDungBien_KhongTuTaoMoi()
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);
        registry.Register("file1", new PeerEndpoint("peerA", "192.168.1.1", 42301));

        // Nhảy vọt 20 giây, tới đúng biên age >= TTL[cite: 3, 4]
        clock.Advance(TimeSpan.FromSeconds(20)); 
        var result = registry.Heartbeat("file1", "peerA");

        // Bị từ chối, xóa khỏi danh sách, không tự tạo peer ảo[cite: 3]
        Assert.False(result); 
        Assert.Empty(registry.ListActive("file1"));
    }

    [Fact]
    public void Register_IPWildcard_BiTuChoi()
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);
        var ep = new PeerEndpoint("peerA", "0.0.0.0", 42301);

        // Ném lỗi ngay khi thấy IP 0.0.0.0[cite: 2]
        Assert.Throws<ArgumentException>(() => registry.Register("file1", ep));
    }

    [Fact]
    public void ListActive_TraVeSnapshot_KhongSuaDuocGoc()
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);
        registry.Register("file1", new PeerEndpoint("peerA", "1.1.1.1", 42301));

        // Lấy danh sách ra rồi cố tình xóa sạch[cite: 1]
        var list = (List<PeerEndpoint>)registry.ListActive("file1");
        list.Clear(); 

        // Dữ liệu gốc trong Registry vẫn còn nguyên vẹn 1 phần tử
        Assert.Single(registry.ListActive("file1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_HostRongHoacBlank_BiTuChoi(string invalidHost)
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);
        var ep = new PeerEndpoint("peerA", invalidHost, 42301);

        Assert.Throws<ArgumentException>(() => registry.Register("file1", ep));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_PeerIdRongHoacBlank_BiTuChoi(string invalidPeerId)
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);
        var ep = new PeerEndpoint(invalidPeerId, "192.168.1.1", 42301);

        Assert.Throws<ArgumentException>(() => registry.Register("file1", ep));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Register_PortNgoaiDai_BiTuChoi(int invalidPort)
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);
        var ep = new PeerEndpoint("peerA", "192.168.1.1", invalidPort);

        Assert.Throws<ArgumentException>(() => registry.Register("file1", ep));
    }

    [Fact]
    public void Register_LaiCungKey_CapNhatEndpointVaLamMoiTTL()
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);

        Assert.True(registry.Register("file1", new PeerEndpoint("peerA", "192.168.1.1", 42301)));
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.True(registry.Register("file1", new PeerEndpoint("peerA", "192.168.1.2", 42309)));

        // Ở t=20: nếu không làm mới LastSeen ở t=15 thì đã hết hạn
        clock.Advance(TimeSpan.FromSeconds(5));
        var active = registry.ListActive("file1");

        var only = Assert.Single(active);
        Assert.Equal("192.168.1.2", only.Host);
        Assert.Equal(42309, only.Port);
    }

    [Fact]
    public void Register_VuotMaxEntries_BiTuChoi_VaDonHetHanTruoc()
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 2);

        Assert.True(registry.Register("file1", new PeerEndpoint("peerA", "192.168.1.1", 42301)));
        Assert.True(registry.Register("file1", new PeerEndpoint("peerB", "192.168.1.2", 42302)));
        Assert.False(registry.Register("file1", new PeerEndpoint("peerC", "192.168.1.3", 42303)));

        // Sau khi hết TTL, các entry cũ được dọn để nhường chỗ
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.True(registry.Register("file1", new PeerEndpoint("peerC", "192.168.1.3", 42303)));
    }

    [Fact]
    public void Heartbeat_GiaHanThanhCongKhiConHan_VaTuChoiInputRong()
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);
        registry.Register("file1", new PeerEndpoint("peerA", "192.168.1.1", 42301));

        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.True(registry.Heartbeat("file1", "peerA"));

        // Tại t=34 (19s sau heartbeat ở t=15), vẫn còn hạn (age < 20)
        clock.Advance(TimeSpan.FromSeconds(19));
        Assert.Single(registry.ListActive("file1"));

        Assert.Throws<ArgumentException>(() => registry.Heartbeat("", "peerA"));
        Assert.Throws<ArgumentException>(() => registry.Heartbeat("file1", "   "));
    }

    [Fact]
    public void ListActive_ChiTraPeerCuaDungFileId()
    {
        var clock = new FakeClock();
        var registry = new PeerRegistry(clock, TimeSpan.FromSeconds(20), 10);

        registry.Register("file1", new PeerEndpoint("peerA", "192.168.1.1", 42301));
        registry.Register("file2", new PeerEndpoint("peerB", "192.168.1.2", 42302));

        var file1Peers = registry.ListActive("file1");
        var only = Assert.Single(file1Peers);
        Assert.Equal("peerA", only.PeerId);
    }
}