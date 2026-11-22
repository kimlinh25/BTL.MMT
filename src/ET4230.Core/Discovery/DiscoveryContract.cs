using System;
using System.Diagnostics;

namespace ET4230.Core.Discovery;

public interface IElapsedClock
{
    TimeSpan Elapsed { get; }
}

/// <summary>
/// Đồng hồ monotonic dựa trên <see cref="Stopwatch"/> để đo khoảng thời gian TTL không bị ảnh hưởng bởi chỉnh giờ hệ thống.
/// </summary>
public sealed class StopwatchElapsedClock : IElapsedClock
{
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startTimestamp);
}

public readonly record struct PeerKey(string FileId, string PeerId);

public sealed record PeerEndpoint(string PeerId, string Host, int Port);