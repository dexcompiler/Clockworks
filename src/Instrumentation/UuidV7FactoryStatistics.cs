namespace Clockworks.Instrumentation;

/// <summary>
/// Lightweight counters for observing <see cref="UuidV7Factory"/> behavior.
/// </summary>
/// <remarks>
/// Statistics are opt-in. Passing an instance to <see cref="UuidV7Factory"/> enables atomic counter updates on the
/// UUIDv7 generation path; leaving statistics unset avoids those atomic updates. Counters are diagnostic signals: under
/// lock-free contention, event counters such as <see cref="CounterOverflowCount"/> and <see cref="SpinWaitCount"/>
/// describe observed path entries and wait attempts rather than globally serialized allocation decisions.
/// </remarks>
public sealed class UuidV7FactoryStatistics
{
    private long _generatedCount;
    private long _clockRollbackCount;
    private long _counterOverflowCount;
    private long _spinWaitCount;
    private long _logicalTimestampAdvanceCount;
    private long _maxLogicalDriftMs;
    private long _casRetryCount;
    private long _randomBufferRefillCount;

    /// <summary>
    /// Total number of UUIDs generated.
    /// </summary>
    public long GeneratedCount => Volatile.Read(ref _generatedCount);

    /// <summary>
    /// Number of successful allocation decisions made while physical time was behind the factory's logical frontier.
    /// </summary>
    public long ClockRollbackCount => Volatile.Read(ref _clockRollbackCount);

    /// <summary>
    /// Number of times the 12-bit per-millisecond counter overflow path was reached.
    /// </summary>
    public long CounterOverflowCount => Volatile.Read(ref _counterOverflowCount);

    /// <summary>
    /// Number of times generation had to wait for physical time to advance after counter overflow.
    /// </summary>
    public long SpinWaitCount => Volatile.Read(ref _spinWaitCount);

    /// <summary>
    /// Number of successful allocation decisions that emitted a logical timestamp ahead of physical time.
    /// </summary>
    public long LogicalTimestampAdvanceCount => Volatile.Read(ref _logicalTimestampAdvanceCount);

    /// <summary>
    /// Maximum observed distance, in milliseconds, between emitted logical time and physical time.
    /// </summary>
    public long MaxLogicalDriftMs => Volatile.Read(ref _maxLogicalDriftMs);

    /// <summary>
    /// Number of failed compare-exchange attempts in the lock-free allocation loop.
    /// </summary>
    public long CasRetryCount => Volatile.Read(ref _casRetryCount);

    /// <summary>
    /// Number of thread-local random buffer refills.
    /// </summary>
    public long RandomBufferRefillCount => Volatile.Read(ref _randomBufferRefillCount);

    /// <summary>
    /// Captures a point-in-time snapshot of all UUIDv7 factory counters.
    /// </summary>
    /// <remarks>
    /// Each field is read atomically, but the snapshot is not a linearizable transaction across all counters.
    /// </remarks>
    public UuidV7FactoryStatisticsSnapshot Snapshot() => new(
        GeneratedCount: GeneratedCount,
        ClockRollbackCount: ClockRollbackCount,
        CounterOverflowCount: CounterOverflowCount,
        SpinWaitCount: SpinWaitCount,
        LogicalTimestampAdvanceCount: LogicalTimestampAdvanceCount,
        MaxLogicalDriftMs: MaxLogicalDriftMs,
        CasRetryCount: CasRetryCount,
        RandomBufferRefillCount: RandomBufferRefillCount);

    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    /// <remarks>
    /// Each counter is reset atomically, but the reset is not a linearizable transaction across all counters.
    /// </remarks>
    public void Reset()
    {
        Interlocked.Exchange(ref _generatedCount, 0);
        Interlocked.Exchange(ref _clockRollbackCount, 0);
        Interlocked.Exchange(ref _counterOverflowCount, 0);
        Interlocked.Exchange(ref _spinWaitCount, 0);
        Interlocked.Exchange(ref _logicalTimestampAdvanceCount, 0);
        Interlocked.Exchange(ref _maxLogicalDriftMs, 0);
        Interlocked.Exchange(ref _casRetryCount, 0);
        Interlocked.Exchange(ref _randomBufferRefillCount, 0);
    }

    internal void RecordGenerated(long count) => Interlocked.Add(ref _generatedCount, count);

    internal void RecordClockRollback() => Interlocked.Increment(ref _clockRollbackCount);

    internal void RecordCounterOverflow() => Interlocked.Increment(ref _counterOverflowCount);

    internal void RecordSpinWait() => Interlocked.Increment(ref _spinWaitCount);

    internal void RecordLogicalTimestampAdvance(long driftMs)
    {
        Interlocked.Increment(ref _logicalTimestampAdvanceCount);
        InterlockedMax(ref _maxLogicalDriftMs, driftMs);
    }

    internal void RecordCasRetry() => Interlocked.Increment(ref _casRetryCount);

    internal void RecordRandomBufferRefill() => Interlocked.Increment(ref _randomBufferRefillCount);

    private static void InterlockedMax(ref long location, long value)
    {
        var current = Volatile.Read(ref location);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref location, value, current);
            if (previous == current)
                break;

            current = previous;
        }
    }
}

/// <summary>
/// Point-in-time UUIDv7 factory statistics.
/// </summary>
public readonly record struct UuidV7FactoryStatisticsSnapshot(
    long GeneratedCount,
    long ClockRollbackCount,
    long CounterOverflowCount,
    long SpinWaitCount,
    long LogicalTimestampAdvanceCount,
    long MaxLogicalDriftMs,
    long CasRetryCount,
    long RandomBufferRefillCount);
