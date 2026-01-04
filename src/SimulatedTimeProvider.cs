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
    private readonly PriorityQueue<ScheduledTimer, ScheduledTimer> _queue;

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
        _queue = new PriorityQueue<ScheduledTimer, ScheduledTimer>(new ScheduledTimerComparer());
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

        List<(TimerCallback Callback, object? State)>? due = null;

        lock (_gate)
        {
            _utcNow = _utcNow.Add(by);
            _schedulerTicks += by.Ticks;

            while (_queue.TryPeek(out var timer, out _))
            {
                if (timer.IsDisposed)
                {
                    _queue.Dequeue();
                    continue;
                }

                if (timer.DueAtTicks > _schedulerTicks)
                {
                    break;
                }

                _queue.Dequeue();

                if (timer.IsDisposed)
                {
                    continue;
                }

                due ??= [];
                due.Add((timer.Callback, timer.State));

                // Periodic timers: coalesce on jump; schedule next occurrence from "now".
                if (timer.PeriodTicks > 0)
                {
                    timer.DueAtTicks = _schedulerTicks + timer.PeriodTicks;
                    _queue.Enqueue(timer, timer);
                    Statistics.RecordPeriodicReschedule(_queue.Count);
                }
                else
                {
                    timer.MarkDisposed();
                    Statistics.RecordTimerDisposed();
                }
            }
        }

        if (due is null)
        {
            return;
        }

        // Fire callbacks outside lock.
        foreach (var (callback, state) in due)
        {
            Statistics.RecordCallbackFired();
            callback(state);
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
            _queue.Enqueue(timer, timer);
            Statistics.RecordTimerCreated(_queue.Count);
            return timer;
        }
    }

    private void Reschedule(ScheduledTimer timer)
    {
        if (timer.IsDisposed)
        {
            return;
        }

        _queue.Enqueue(timer, timer);
        Statistics.RecordQueueEnqueue(_queue.Count);
    }

    private sealed class ScheduledTimerComparer : IComparer<ScheduledTimer>
    {
        public int Compare(ScheduledTimer? x, ScheduledTimer? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var cmp = x.DueAtTicks.CompareTo(y.DueAtTicks);
            if (cmp != 0) return cmp;
            return x.Id.CompareTo(y.Id);
        }
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
        /// Gets the delegate to invoke when the timer fires.
        /// </summary>
        public TimerCallback Callback { get; }
        /// <summary>
        /// Gets the state object passed to the timer callback.
        /// </summary>
        public object? State { get; }

        /// <summary>
        /// Gets or sets the due time of the timer, in scheduler ticks.
        /// </summary>
        public long DueAtTicks { get; set; }
        /// <summary>
        /// Gets or sets the period of the timer, in scheduler ticks.
        /// </summary>
        public long PeriodTicks { get; set; }

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

                _owner.Statistics.RecordTimerChange();
                _owner.Reschedule(this);
                return true;
            }
        }

        /// <summary>
        /// Disposes the timer, preventing it from firing again.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Statistics.RecordTimerDisposed();
            }
        }

        /// <summary>
        /// Asynchronously disposes the timer, preventing it from firing again.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        internal void MarkDisposed()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Statistics.RecordTimerDisposed();
            }
        }
    }
}
