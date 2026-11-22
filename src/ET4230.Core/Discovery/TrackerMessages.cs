using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ET4230.Core.Discovery;

/// <summary>
/// Hằng số type và DTO JSON UTF-8 cho luồng tracker theo Giao-thuc-v1.md.
/// </summary>
public static class TrackerProtocol
{
    public const byte Version = 1;
    public const ushort DefaultFlags = 0;
    public const int MaxControlJsonBytes = 64 * 1024;

    public const byte TypeError = 8;
    public const byte TypeRegister = 16;
    public const byte TypeListPeers = 17;
    public const byte TypePeerList = 18;
    public const byte TypeHeartbeat = 19;
    public const byte TypeAck = 20;

    public const string ErrorInvalidRequest = "INVALID_REQUEST";
    public const string ErrorProtocolError = "PROTOCOL_ERROR";
    public const string ErrorPeerNotFound = "PEER_NOT_FOUND";
    public const string ErrorRegistryFull = "REGISTRY_FULL";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static byte[] SerializeJson<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    public static bool TryDeserializeJson<T>(ReadOnlySpan<byte> utf8Json, out T? value)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(utf8Json, JsonOptions);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }
}

public sealed record RegisterPayload(string PeerId, string Host, int Port, string FileId);

public sealed record HeartbeatPayload(string PeerId, string FileId);

public sealed record ListPeersPayload(string FileId);

public sealed record PeerListPayload(IReadOnlyList<PeerEndpoint> Peers);

public sealed record AckPayload(bool Ok);

public sealed record ErrorPayload(string Code, string Message);
