using Clockworks.Instrumentation;

namespace Clockworks;

/// <summary>
/// A deterministic <see cref="TimeProvider"/> suitable for simulations.
///
/// Design:
/// - Wall time (<see cref="GetUtcNow"/>) is controllable and may move backwards via <see cref="SetUtcNow"/>.
/// - Scheduler time (timers) is monotonic and only advances via <see cref="Advance"/>.
/// - Periodic timers default to coalescing on large time jumps.
/// </summary>
public sealed class SimulatedTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();

    private DateTimeOffset _utcNow;
    private readonly TimeZoneInfo _localTimeZone;

    private long _schedulerTicks;

    private long _nextId;

    // Each entry carries the timer's schedule version and an immutable (due, id) priority, so the heap order always
    // holds. Change supersedes a timer's entry and Dispose abandons it, leaving the stale entry in place: Advance
    // discards stale entries that reach the head, and the queue is rebuilt from live entries whenever stale ones
    // outnumber them, so it never holds more than about twice the live timers.
    private readonly PriorityQueue<(ScheduledTimer Timer, long Version), (long DueAtTicks, long Id)> _queue;

    // Timers whose current entry is in the queue; every other queued entry is stale.
    private int _queued;

    /// <summary>
    /// Gets lightweight counters that can be used to observe scheduling behavior during simulation.
    /// </summary>
    public SimulatedTimeProviderStatistics Statistics { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulatedTimeProvider"/>.
    /// </summary>
    /// <param name="startTime">Initial wall time; defaults to <see cref="DateTimeOffset.UnixEpoch"/>.</param>
    /// <param name="localTimeZone">Local time zone; defaults to <see cref="TimeZoneInfo.Utc"/>.</param>
    public SimulatedTimeProvider(DateTimeOffset? startTime = null, TimeZoneInfo? localTimeZone = null)
    {
        _utcNow = startTime ?? DateTimeOffset.UnixEpoch;
        _localTimeZone = localTimeZone ?? TimeZoneInfo.Utc;

        _schedulerTicks = 0;
        _queue = new PriorityQueue<(ScheduledTimer Timer, long Version), (long DueAtTicks, long Id)>();
    }

    /// <summary>
    /// Creates a provider starting at the Unix epoch.
    /// </summary>
    public static SimulatedTimeProvider FromEpoch() => new(DateTimeOffset.UnixEpoch);

    /// <summary>
    /// Creates a provider starting at the specified Unix timestamp in milliseconds.
    /// </summary>
    public static SimulatedTimeProvider FromUnixMs(long unixMs) => new(DateTimeOffset.FromUnixTimeMilliseconds(unixMs));

    /// <summary>
    /// Gets the current wall-clock time.
    /// </summary>
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    /// <summary>
    /// Gets the configured local time zone.
    /// </summary>
    public override TimeZoneInfo LocalTimeZone => _localTimeZone;

    /// <summary>
    /// Gets the frequency of <see cref="GetTimestamp"/> in ticks per second.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="TimeSpan.TicksPerSecond"/> since scheduler time uses TimeSpan ticks.
    /// </remarks>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>
    /// Gets the current high-frequency timestamp based on scheduler time.
    /// </summary>
    /// <remarks>
    /// This returns the scheduler time in ticks, which only advances via <see cref="Advance"/>.
    /// Unlike the base implementation which uses the system's high-frequency timer,
    /// this provides deterministic, controllable timestamps for simulation.
    /// </remarks>
    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _schedulerTicks;
        }
    }

    /// <summary>
    /// Gets the current wall-clock time without advancing scheduler time.
    /// </summary>
    public DateTimeOffset PeekUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    /// <summary>
    /// Sets the current wall-clock time.
    /// </summary>
    /// <remarks>
    /// This does not affect scheduler time or timer ordering; timers are driven by scheduler time, which only advances via
    /// <see cref="Advance"/>.
    /// </remarks>
    public void SetUtcNow(DateTimeOffset value)
    {
        lock (_gate)
        {
            _utcNow = value;
        }
    }

    /// <summary>
    /// Sets the current wall-clock time using a Unix millisecond timestamp.
    /// </summary>
    public void SetUnixMs(long unixMs) => SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(unixMs));

    /// <summary>
    /// Advances scheduler time and wall time forward by the same amount, firing any due timers.
    /// </summary>
    /// <param name="by">Amount to advance; must be non-negative.</param>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), "Advance must be non-negative.");
        }

        Statistics.RecordAdvance(by);

        List<(ScheduledTimer Timer, long Version)>? due = null;

        lock (_gate)
        {
            _utcNow = _utcNow.Add(by);
            _schedulerTicks += by.Ticks;

            while (_queue.TryPeek(out var entry, out var priority))
            {
                if (IsStale(entry))
                {
                    _queue.Dequeue();
                    continue;
                }

                if (priority.DueAtTicks > _schedulerTicks)
                {
                    break;
                }

                _queue.Dequeue();
                Unqueue(entry.Timer);
                due ??= [];
                due.Add(entry);

                // Periodic timers: coalesce on jump; schedule next occurrence from "now".
                if (entry.Timer.PeriodTicks > 0)
                {
                    entry.Timer.DueAtTicks = _schedulerTicks + entry.Timer.PeriodTicks;
                    Enqueue(entry.Timer);
                    Statistics.RecordPeriodicReschedule(_queue.Count);
                }
            }

            CompactIfMostlyStale();
        }

        if (due is null)
        {
            return;
        }

        // Fire callbacks outside lock. A callback may dispose or reschedule a timer that this advance has already
        // found due; such a timer does not fire for this occurrence.
        foreach (var (timer, version) in due)
        {
            TimerCallback? callback;
            object? state;
            lock (_gate)
            {
                if (timer.IsDisposed || timer.Version != version)
                {
                    continue;
                }

                callback = timer.Callback;
                state = timer.State;
                if (timer.PeriodTicks == 0)
                {
                    timer.MarkDisposed();
                }
            }

            Statistics.RecordCallbackFired();
            callback!(state);
        }
    }

    /// <summary>
    /// Advances time by the specified number of milliseconds.
    /// </summary>
    public void AdvanceMs(long milliseconds) => Advance(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>
    /// Creates a timer whose due/period are driven by the simulated scheduler time.
    /// </summary>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime));
        }

        if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        lock (_gate)
        {
            var id = Interlocked.Increment(ref _nextId);
            var dueTicks = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : _schedulerTicks + dueTime.Ticks;
            var periodTicks = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;

            var timer = new ScheduledTimer(this, id, callback, state, dueTicks, periodTicks);
            Enqueue(timer);
            Statistics.RecordTimerCreated(_queue.Count);
            return timer;
        }
    }

    // Queues the timer's current schedule. Callers hold _gate.
    private void Enqueue(ScheduledTimer timer)
    {
        _queue.Enqueue((timer, timer.Version), (timer.DueAtTicks, timer.Id));
        timer.Queued = true;
        _queued++;
    }

    // The timer's queued entry has been dequeued, superseded or abandoned. Callers hold _gate.
    private void Unqueue(ScheduledTimer timer)
    {
        if (timer.Queued)
        {
            timer.Queued = false;
            _queued--;
        }
    }

    // An entry is current only while its timer is queued at the entry's version. Callers hold _gate.
    private static bool IsStale((ScheduledTimer Timer, long Version) entry) =>
        !entry.Timer.Queued || entry.Version != entry.Timer.Version;

    // Rebuilds the queue from its current entries once stale ones outnumber them, in O(n): each stale entry is
    // dropped once, so the rebuilds cost O(1) per change or disposal amortized. Callers hold _gate.
    private void CompactIfMostlyStale()
    {
        if (_queue.Count - _queued <= _queued)
        {
            return;
        }

        var current = new List<((ScheduledTimer Timer, long Version) Entry, (long DueAtTicks, long Id) Priority)>(_queued);
        foreach (var item in _queue.UnorderedItems)
        {
            if (!IsStale(item.Element))
            {
                current.Add(item);
            }
        }

        _queue.Clear();
        _queue.EnqueueRange(current);
    }

    private sealed class ScheduledTimer : ITimer
    {
        private readonly SimulatedTimeProvider _owner;
        private int _disposed;

        public ScheduledTimer(
            SimulatedTimeProvider owner,
            long id,
            TimerCallback callback,
            object? state,
            long dueAtTicks,
            long periodTicks)
        {
            _owner = owner;
            Id = id;
            Callback = callback;
            State = state;
            DueAtTicks = dueAtTicks;
            PeriodTicks = periodTicks;
        }

        /// <summary>
        /// Gets the unique identifier for this timer.
        /// </summary>
        public long Id { get; }
        /// <summary>
        /// Gets the delegate to invoke when the timer fires, or null once the timer is disposed or spent.
        /// </summary>
        public TimerCallback? Callback { get; private set; }
        /// <summary>
        /// Gets the state object passed to the timer callback, released with <see cref="Callback"/>.
        /// </summary>
        public object? State { get; private set; }

        /// <summary>
        /// Gets or sets whether the timer's current entry is in the owner's queue. Read and written under the
        /// owner's lock.
        /// </summary>
        public bool Queued { get; set; }

        /// <summary>
        /// Gets or sets the due time of the timer, in scheduler ticks.
        /// </summary>
        public long DueAtTicks { get; set; }
        /// <summary>
        /// Gets or sets the period of the timer, in scheduler ticks.
        /// </summary>
        public long PeriodTicks { get; set; }

        /// <summary>
        /// Gets the version of the timer's schedule, which <see cref="Change"/> increments. A queued entry with an
        /// older version is stale. Read and written under the owner's lock.
        /// </summary>
        public long Version { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this timer has been disposed.
        /// </summary>
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        /// <summary>
        /// Changes the due time and period of the timer.
        /// </summary>
        /// <returns>True if the timer was successfully rescheduled; false if it was already disposed.</returns>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed)
            {
                return false;
            }

            if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            }

            if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(period));
            }

            lock (_owner._gate)
            {
                if (IsDisposed)
                {
                    return false;
                }

                var dueTicks = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : _owner._schedulerTicks + dueTime.Ticks;
                DueAtTicks = dueTicks;
                PeriodTicks = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;

                // Never reorder a queued entry: supersede it, and queue the new schedule.
                _owner.Unqueue(this);
                Version++;
                _owner.Statistics.RecordTimerChange();
                _owner.CompactIfMostlyStale();
                _owner.Enqueue(this);
                _owner.Statistics.RecordQueueEnqueue(_owner._queue.Count);
                return true;
            }
        }

        /// <summary>
        /// Disposes the timer, preventing it from firing again.
        /// </summary>
        public void Dispose()
        {
            lock (_owner._gate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                Release();
                _owner.Unqueue(this);
                _owner.CompactIfMostlyStale();
            }

            _owner.Statistics.RecordTimerDisposed();
        }

        /// <summary>
        /// Asynchronously disposes the timer, preventing it from firing again.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        // A one-shot timer that has fired is spent. Callers hold the owner's lock; the timer's entry is already out
        // of the queue.
        internal void MarkDisposed()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Release();
                _owner.Statistics.RecordTimerDisposed();
            }
        }

        // A disposed or spent timer drops its callback and state, so an entry still queued for it pins neither.
        private void Release()
        {
            Callback = null;
            State = null;
        }
    }
}
