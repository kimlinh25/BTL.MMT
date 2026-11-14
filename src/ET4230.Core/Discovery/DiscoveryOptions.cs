using System;
using System.Net;

namespace ET4230.Core.Discovery;

/// <summary>
/// Cấu hình cho Tracker TCP server. Tách biệt địa chỉ bind của socket lắng nghe
/// với các giới hạn của registry và kích thước frame điều khiển.
/// </summary>
public sealed record TrackerOptions
{
    /// <summary>
    /// Địa chỉ IP bind cho TcpListener (ví dụ: 0.0.0.0 để nghe mọi interface IPv4, hoặc 127.0.0.1 khi test nội bộ).
    /// Đây KHÔNG phải endpoint để peer khác kết nối trực tiếp khi dùng 0.0.0.0.
    /// </summary>
    public string BindHost { get; init; } = "0.0.0.0";

    /// <summary>
    /// Cổng TCP lắng nghe của tracker (0 để OS tự chọn khi test, hoặc 1..65535).
    /// </summary>
    public int BindPort { get; init; } = 42300;

    public TimeSpan Ttl { get; init; } = TimeSpan.FromSeconds(20);

    public int MaxEntries { get; init; } = 1024;

    public int MaxPeersPerResponse { get; init; } = 100;

    public int MaxControlPayloadBytes { get; init; } = TrackerProtocol.MaxControlJsonBytes;

    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BindHost) || !IPAddress.TryParse(BindHost.Trim(), out _))
            throw new ArgumentException("BindHost phải là địa chỉ IP hợp lệ (ví dụ 0.0.0.0 hoặc 127.0.0.1).", nameof(BindHost));

        if (BindPort < 0 || BindPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(BindPort), "BindPort phải nằm trong khoảng 0..65535.");

        if (Ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Ttl), "Ttl phải lớn hơn 0.");

        if (MaxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxEntries), "MaxEntries phải lớn hơn 0.");

        if (MaxPeersPerResponse <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxPeersPerResponse), "MaxPeersPerResponse phải lớn hơn 0.");

        if (MaxControlPayloadBytes <= 0 || MaxControlPayloadBytes > TrackerProtocol.MaxControlJsonBytes)
            throw new ArgumentOutOfRangeException(nameof(MaxControlPayloadBytes), $"MaxControlPayloadBytes phải nằm trong 1..{TrackerProtocol.MaxControlJsonBytes}.");

        if (ReadTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReadTimeout), "ReadTimeout phải lớn hơn 0.");
    }
}

/// <summary>
/// Cấu hình cho Discovery Client. Phân biệt rõ địa chỉ kết nối tới tracker
/// và endpoint công bố (Advertised) mà các peer khác có thể kết nối tới mình.
/// </summary>
public sealed record DiscoveryClientOptions
{
    public required string TrackerHost { get; init; }
    public required int TrackerPort { get; init; }

    public required string SelfPeerId { get; init; }

    /// <summary>
    /// Host công bố cho peer khác kết nối (không được là 0.0.0.0 hoặc rỗng).
    /// </summary>
    public required string AdvertisedHost { get; init; }

    /// <summary>
    /// Port TCP listener của peer này công bố cho peer khác kết nối (1..65535).
    /// </summary>
    public required int AdvertisedPort { get; init; }

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        ValidateConnectableEndpoint(TrackerHost, TrackerPort, nameof(TrackerHost), nameof(TrackerPort));
        ValidateConnectableEndpoint(AdvertisedHost, AdvertisedPort, nameof(AdvertisedHost), nameof(AdvertisedPort));

        if (string.IsNullOrWhiteSpace(SelfPeerId))
            throw new ArgumentException("SelfPeerId không được rỗng.", nameof(SelfPeerId));

        if (HeartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval), "HeartbeatInterval phải lớn hơn 0.");

        if (RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout), "RequestTimeout phải lớn hơn 0.");
    }

    internal static void ValidateConnectableEndpoint(string host, int port, string hostParamName, string portParamName)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException($"{hostParamName} không được rỗng.", hostParamName);

        if (string.Equals(host.Trim(), "0.0.0.0", StringComparison.Ordinal))
            throw new ArgumentException($"{hostParamName} không được là wildcard 0.0.0.0.", hostParamName);

        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(portParamName, $"{portParamName} phải nằm trong khoảng 1..65535.");
    }
}
