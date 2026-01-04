namespace Clockworks.Instrumentation;

/// <summary>
/// Lightweight counters for observing <c>SimulatedTimeProvider</c> scheduling behavior.
/// </summary>
/// <remarks>
/// These counters are intended for testing and simulation diagnostics. Values are updated concurrently and are safe to read
/// without external synchronization.
/// </remarks>
public sealed class SimulatedTimeProviderStatistics
{
    private long _timersCreated;
    private long _timerChanges;
    private long _timersDisposed;
    private long _callbacksFired;
    private long _periodicReschedules;
    private long _advanceCalls;
    private long _advanceTicks;
    private long _maxQueueLength;
    private long _queueEnqueues;

    /// <summary>
    /// Total number of timers created.
    /// </summary>
    public long TimersCreated => Volatile.Read(ref _timersCreated);

    /// <summary>
    /// Total number of successful timer <c>Change</c> operations.
    /// </summary>
    public long TimerChanges => Volatile.Read(ref _timerChanges);

    /// <summary>
    /// Total number of timers disposed.
    /// </summary>
    public long TimersDisposed => Volatile.Read(ref _timersDisposed);

    /// <summary>
    /// Total number of callbacks fired.
    /// </summary>
    public long CallbacksFired => Volatile.Read(ref _callbacksFired);

    /// <summary>
    /// Total number of periodic timer reschedules performed.
    /// </summary>
    public long PeriodicReschedules => Volatile.Read(ref _periodicReschedules);

    /// <summary>
    /// Total number of <c>Advance</c> calls.
    /// </summary>
    public long AdvanceCalls => Volatile.Read(ref _advanceCalls);

    /// <summary>
    /// Total scheduler ticks advanced across all <c>Advance</c> calls.
    /// </summary>
    public long AdvanceTicks => Volatile.Read(ref _advanceTicks);

    /// <summary>
    /// Maximum observed timer queue length.
    /// </summary>
    public long MaxQueueLength => Volatile.Read(ref _maxQueueLength);

    /// <summary>
    /// Total number of times a timer was enqueued (including reschedules).
    /// </summary>
    public long QueueEnqueues => Volatile.Read(ref _queueEnqueues);

    internal void RecordTimerCreated(int queueLength)
    {
        Interlocked.Increment(ref _timersCreated);
        RecordQueueEnqueue(queueLength);
    }

    internal void RecordQueueEnqueue(int queueLength)
    {
        Interlocked.Increment(ref _queueEnqueues);
        InterlockedMax(ref _maxQueueLength, queueLength);
    }

    internal void RecordTimerChange() => Interlocked.Increment(ref _timerChanges);

    internal void RecordTimerDisposed() => Interlocked.Increment(ref _timersDisposed);

    internal void RecordCallbackFired() => Interlocked.Increment(ref _callbacksFired);

    internal void RecordPeriodicReschedule(int queueLength)
    {
        Interlocked.Increment(ref _periodicReschedules);
        RecordQueueEnqueue(queueLength);
    }

    internal void RecordAdvance(TimeSpan by)
    {
        Interlocked.Increment(ref _advanceCalls);
        Interlocked.Add(ref _advanceTicks, by.Ticks);
    }

    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    public void Reset()
    {
        _timersCreated = 0;
        _timerChanges = 0;
        _timersDisposed = 0;
        _callbacksFired = 0;
        _periodicReschedules = 0;
        _advanceCalls = 0;
        _advanceTicks = 0;
        _maxQueueLength = 0;
        _queueEnqueues = 0;
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
}
