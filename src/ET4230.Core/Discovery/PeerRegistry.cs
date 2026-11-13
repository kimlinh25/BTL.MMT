using System;
using System.Collections.Generic;

namespace ET4230.Core.Discovery;

public sealed class PeerRegistry
{
    private sealed record RegistryEntry(PeerEndpoint Endpoint, TimeSpan LastSeen);

    private readonly Dictionary<PeerKey, RegistryEntry> _entries = new();
    private readonly Lock _gate = new();
    private readonly IElapsedClock _clock;
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    public PeerRegistry(IElapsedClock clock, TimeSpan ttl, int maxEntries)
    {
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _ttl = ttl;
        _maxEntries = maxEntries;
    }

    public bool Register(string fileId, PeerEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("FileId không hợp lệ.", nameof(fileId));
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.IsNullOrWhiteSpace(endpoint.PeerId))
            throw new ArgumentException("PeerId không hợp lệ.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(endpoint.Host))
            throw new ArgumentException("Host không hợp lệ.", nameof(endpoint));
        if (endpoint.Port <= 0 || endpoint.Port > 65535)
            throw new ArgumentException("Port ngoài dải hợp lệ (1..65535).", nameof(endpoint));
        if (string.Equals(endpoint.Host.Trim(), "0.0.0.0", StringComparison.Ordinal))
            throw new ArgumentException("Không được công bố IP wildcard 0.0.0.0.", nameof(endpoint));

        lock (_gate)
        {
            var now = _clock.Elapsed;
            var key = new PeerKey(fileId, endpoint.PeerId);

            RemoveExpiredEntries(now);

            if (_entries.ContainsKey(key))
            {
                // Đã tồn tại: ghi đè endpoint mới và làm mới LastSeen
                _entries[key] = new RegistryEntry(endpoint, now);
                return true;
            }

            if (_entries.Count >= _maxEntries)
                return false;

            _entries.Add(key, new RegistryEntry(endpoint, now));
            return true;
        }
    }

    public bool Heartbeat(string fileId, string peerId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("FileId không hợp lệ.", nameof(fileId));
        if (string.IsNullOrWhiteSpace(peerId))
            throw new ArgumentException("PeerId không hợp lệ.", nameof(peerId));

        lock (_gate)
        {
            var now = _clock.Elapsed;
            var key = new PeerKey(fileId, peerId);

            if (_entries.TryGetValue(key, out var entry))
            {
                if (now - entry.LastSeen >= _ttl)
                {
                    _entries.Remove(key);
                    return false;
                }

                _entries[key] = new RegistryEntry(entry.Endpoint, now);
                return true;
            }

            return false;
        }
    }

    public IReadOnlyList<PeerEndpoint> ListActive(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("FileId không hợp lệ.", nameof(fileId));

        lock (_gate)
        {
            var now = _clock.Elapsed;
            var activePeers = new List<PeerEndpoint>();

            foreach (var kvp in _entries)
            {
                if (kvp.Key.FileId == fileId && (now - kvp.Value.LastSeen < _ttl))
                {
                    activePeers.Add(kvp.Value.Endpoint);
                }
            }

            // Trả về bản sao mới (snapshot) để caller không sửa ngược vào _entries
            return activePeers;
        }
    }

    private void RemoveExpiredEntries(TimeSpan now)
    {
        var expiredKeys = new List<PeerKey>();
        foreach (var entry in _entries)
        {
            if (now - entry.Value.LastSeen >= _ttl)
            {
                expiredKeys.Add(entry.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _entries.Remove(key);
        }
    }
}