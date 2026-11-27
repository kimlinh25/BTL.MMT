using System.Net;
using System.Text.Json;
using ET4230.Core.Discovery;
using ET4230.Core.Scheduling;

namespace ET4230.Cli;

internal sealed record W03Config
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int SchemaVersion { get; init; } = 1;
    public TrackerSettings Tracker { get; init; } = new();
    public PeerSettings Peer { get; init; } = new();
    public TransferSettings Transfer { get; init; } = new();
    public PeerStrategy Strategy { get; init; } = new();
    public FileSettings File { get; init; } = new();
    private string ConfigDirectory { get; init; } = "";

    public static async Task<W03Config> LoadAsync(string path, CancellationToken ct)
    {
        string fullPath = Path.GetFullPath(path);
        await using var stream = System.IO.File.OpenRead(fullPath);
        var config = await JsonSerializer.DeserializeAsync<W03Config>(stream, JsonOptions, ct);
        if (config is not { SchemaVersion: 1, Tracker: not null, Peer: not null,
            Transfer: not null, Strategy: not null, File: not null })
        {
            throw new ArgumentException("Cần JSON schemaVersion 1 và các mục cấu hình phải là object.");
        }

        return config with { ConfigDirectory = Path.GetDirectoryName(fullPath)! };
    }

    public string ResolvePath(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Thiếu {name}.");
        return Path.GetFullPath(value, ConfigDirectory);
    }

    public TrackerOptions TrackerOptions() => new()
    {
        BindHost = Tracker.BindHost,
        BindPort = ValidPort(Tracker.Port, "tracker.port"),
        Ttl = Seconds(Tracker.TtlSeconds, "tracker.ttlSeconds")
    };

    public DiscoveryClientOptions DiscoveryOptions(Guid peerId) => new()
    {
        TrackerHost = ConnectHost(Tracker.Host, "tracker.host"),
        TrackerPort = ValidPort(Tracker.Port, "tracker.port"),
        SelfPeerId = peerId.ToString("D"),
        AdvertisedHost = ConnectHost(Peer.AdvertiseHost, "peer.advertiseHost"),
        AdvertisedPort = ValidPort(Peer.Port, "peer.port"),
        HeartbeatInterval = Seconds(Tracker.HeartbeatSeconds, "tracker.heartbeatSeconds"),
        RequestTimeout = RequestTimeout()
    };

    public IPEndPoint SeedEndpoint()
    {
        if (!IPAddress.TryParse(Peer.ListenHost, out var address))
            throw new ArgumentException("peer.listenHost phải là địa chỉ IP lắng nghe hợp lệ.");
        if (Tracker.HeartbeatSeconds >= Tracker.TtlSeconds)
            throw new ArgumentException("tracker.heartbeatSeconds phải nhỏ hơn tracker.ttlSeconds.");
        return new IPEndPoint(address, ValidPort(Peer.Port, "peer.port"));
    }

    public TimeSpan RequestTimeout() =>
        Seconds(Transfer.RequestTimeoutSeconds, "transfer.requestTimeoutSeconds");

    public void ValidateUpload()
    {
        if (Peer.MaxUploadSessions is < 1 or > 16)
            throw new ArgumentException("peer.maxUploadSessions phải thuộc 1..16.");
        double limit = Strategy.ApplicationUploadLimitMbps;
        if (!double.IsFinite(limit) || limit < 0 || limit > 100_000 || (limit > 0 && limit < 0.01))
            throw new ArgumentException("strategy.applicationUploadLimitMbps phải là 0 hoặc thuộc 0.01..100000.");
    }

    public SwarmOptions SwarmOptions()
    {
        if (Strategy.PieceSelection is not ("sequential" or "rarest-first") ||
            Transfer.DiscoveryIntervalMilliseconds is < 50 or > 10000 ||
            Transfer.ShareAfterCompletionSeconds is < 0 or > 3600)
            throw new ArgumentException("Chiến lược chọn piece hoặc cấu hình discovery/upload không hợp lệ.");

        return new(RequestTimeout(), TimeSpan.FromMilliseconds(Transfer.DiscoveryIntervalMilliseconds),
            Transfer.MaxSources, Transfer.MaxRetries, Strategy.PieceSelection == "rarest-first");
    }

    public void ValidateNewManifestLayout()
    {
        if (Transfer.BlockSizeBytes is <= 0 or > 1024 * 1024 - 12)
            throw new ArgumentException("transfer.blockSizeBytes phải vừa payload BLOCK v1.");
        if (Transfer.PieceSizeBytes < Transfer.BlockSizeBytes ||
            Transfer.PieceSizeBytes % Transfer.BlockSizeBytes != 0)
            throw new ArgumentException("transfer.pieceSizeBytes phải là bội dương của blockSizeBytes.");
    }

    private static string ConnectHost(string? host, string name)
    {
        string? value = host?.Trim();
        if (string.IsNullOrEmpty(value) || value is "0.0.0.0" or "::")
            throw new ArgumentException($"{name} phải là địa chỉ peer có thể kết nối.");
        return value;
    }

    private static int ValidPort(int port, string name) =>
        port is >= 1 and <= 65535 ? port : throw new ArgumentException($"{name} phải thuộc 1..65535.");

    private static TimeSpan Seconds(int value, string name) =>
        value > 0 && (long)value * 1000 <= uint.MaxValue - 1
            ? TimeSpan.FromSeconds(value)
            : throw new ArgumentException($"{name} nằm ngoài giới hạn timeout dương.");

    public sealed record TrackerSettings
    {
        public string BindHost { get; init; } = "127.0.0.1";
        public string Host { get; init; } = "127.0.0.1";
        public int Port { get; init; } = 42300;
        public int HeartbeatSeconds { get; init; } = 5;
        public int TtlSeconds { get; init; } = 20;
    }

    public sealed record PeerSettings
    {
        public string ListenHost { get; init; } = "127.0.0.1";
        public string AdvertiseHost { get; init; } = "127.0.0.1";
        public int Port { get; init; } = 42301;
        public int MaxUploadSessions { get; init; } = 8;
    }

    public sealed record TransferSettings
    {
        public int PieceSizeBytes { get; init; } = 1048576;
        public int BlockSizeBytes { get; init; } = 16384;
        public int RequestTimeoutSeconds { get; init; } = 15;
        public int DiscoveryIntervalMilliseconds { get; init; } = 250;
        public int MaxSources { get; init; } = 8;
        public int MaxRetries { get; init; } = 3;
        public int ShareAfterCompletionSeconds { get; init; }
    }

    public sealed record PeerStrategy
    {
        public string PieceSelection { get; init; } = "sequential";
        public double ApplicationUploadLimitMbps { get; init; }
    }

    public sealed record FileSettings
    {
        public string? SourcePath { get; init; }
        public string? ManifestPath { get; init; }
        public string? OutputPath { get; init; }
    }
}
