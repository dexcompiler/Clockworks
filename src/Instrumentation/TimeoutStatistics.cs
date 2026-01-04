namespace Clockworks.Instrumentation;

/// <summary>
/// Lightweight counters for observing <c>Timeouts</c> usage.
/// </summary>
/// <remarks>
/// These counters are intended for testing and simulation diagnostics. Values are updated concurrently and are safe to read
/// without external synchronization.
/// </remarks>
public sealed class TimeoutStatistics
{
    private long _created;
    private long _fired;
    private long _disposed;

    /// <summary>
    /// Total number of timeouts created.
    /// </summary>
    public long Created => Volatile.Read(ref _created);

    /// <summary>
    /// Total number of timeouts that fired (i.e., reached their due time).
    /// </summary>
    public long Fired => Volatile.Read(ref _fired);

    /// <summary>
    /// Total number of timeout timers disposed.
    /// </summary>
    public long Disposed => Volatile.Read(ref _disposed);

    internal void RecordCreated() => Interlocked.Increment(ref _created);
    internal void RecordFired() => Interlocked.Increment(ref _fired);
    internal void RecordDisposed() => Interlocked.Increment(ref _disposed);

    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    public void Reset()
    {
        _created = 0;
        _fired = 0;
        _disposed = 0;
    }
}
