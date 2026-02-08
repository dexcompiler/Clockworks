namespace Clockworks.Distributed;

/// <summary>
/// Statistics tracking for HLC behavior analysis.
/// Thread-safe for concurrent updates.
/// </summary>
public sealed class HlcStatistics
{
    private long _localEventCount;
    private long _sendCount;
    private long _receiveCount;
    private long _clockAdvances;  // Times we advanced due to remote
    private long _maxObservedDrift;
    private long _maxRemoteAheadMs;
    private long _maxRemoteBehindMs;
    private long _remoteAheadCount;

    /// <summary>
    /// Total number of local events recorded.
    /// </summary>
    public long LocalEventCount => Volatile.Read(ref _localEventCount);

    /// <summary>
    /// Total number of send events recorded.
    /// </summary>
    public long SendCount => Volatile.Read(ref _sendCount);

    /// <summary>
    /// Total number of receive events recorded.
    /// </summary>
    public long ReceiveCount => Volatile.Read(ref _receiveCount);

    /// <summary>
    /// Number of times the local clock advanced due to observing a remote timestamp.
    /// </summary>
    public long ClockAdvances => Volatile.Read(ref _clockAdvances);

    /// <summary>
    /// Maximum absolute observed drift (in milliseconds) between a received remote timestamp and the local timestamp prior to witnessing.
    /// </summary>
    public long MaxObservedDriftMs => Volatile.Read(ref _maxObservedDrift);

    /// <summary>
    /// Maximum observed amount (in milliseconds) by which a remote timestamp was ahead of the local timestamp prior to witnessing.
    /// </summary>
    public long MaxRemoteAheadMs => Volatile.Read(ref _maxRemoteAheadMs);

    /// <summary>
    /// Maximum observed amount (in milliseconds) by which a remote timestamp was behind the local timestamp prior to witnessing.
    /// </summary>
    public long MaxRemoteBehindMs => Volatile.Read(ref _maxRemoteBehindMs);

    /// <summary>
    /// Number of receive events where the remote timestamp was ahead of or equal to the local timestamp prior to witnessing.
    /// </summary>
    public long RemoteAheadCount => Volatile.Read(ref _remoteAheadCount);

    internal void RecordLocalEvent(HlcTimestamp timestamp)
    {
        Interlocked.Increment(ref _localEventCount);
    }

    internal void RecordSend(HlcTimestamp timestamp)
    {
        Interlocked.Increment(ref _sendCount);
    }

    internal void RecordReceive(HlcTimestamp before, HlcTimestamp after, HlcTimestamp remote)
    {
        Interlocked.Increment(ref _receiveCount);

        // Clock advances due to remote when the remote timestamp is ahead of the local timestamp
        // prior to witnessing and the post-witness wall time matches the remote wall time.
        if (remote > before && after.WallTimeMs == remote.WallTimeMs)
        {
            Interlocked.Increment(ref _clockAdvances);
        }

        var delta = remote.WallTimeMs - before.WallTimeMs;
        if (delta >= 0)
        {
            Interlocked.Increment(ref _remoteAheadCount);
            InterlockedMax(ref _maxRemoteAheadMs, delta);
        }
        else
        {
            InterlockedMax(ref _maxRemoteBehindMs, -delta);
        }

        // Track absolute drift
        InterlockedMax(ref _maxObservedDrift, long.Abs(delta));
    }

    private static void InterlockedMax(ref long location, long value)
    {
        long current = Volatile.Read(ref location);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref location, value, current);
            if (previous == current) break;
            current = previous;
        }
    }

    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    public void Reset()
    {
        _localEventCount = 0;
        _sendCount = 0;
        _receiveCount = 0;
        _clockAdvances = 0;
        _maxObservedDrift = 0;
        _maxRemoteAheadMs = 0;
        _maxRemoteBehindMs = 0;
        _remoteAheadCount = 0;
    }

    /// <summary>
    /// Returns a human-readable summary of the current statistics.
    /// </summary>
    public override string ToString() =>
        $"Local: {LocalEventCount}, Send: {SendCount}, Receive: {ReceiveCount}, " +
        $"Advances: {ClockAdvances}, MaxDrift: {MaxObservedDriftMs}ms, " +
        $"MaxAhead: {MaxRemoteAheadMs}ms, MaxBehind: {MaxRemoteBehindMs}ms";
}
