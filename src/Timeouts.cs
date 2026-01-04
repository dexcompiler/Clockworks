using Clockworks.Instrumentation;

namespace Clockworks;

/// <summary>
/// Provides factory methods for creating cancellation token sources and disposable handles that are cancelled after a
/// specified timeout, using a given <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// The factory methods are thread-safe. Returned objects follow the thread-safety and lifetime semantics of
/// <see cref="CancellationTokenSource"/> and <see cref="ITimer"/>.
/// </remarks>
public static class Timeouts
{
    /// <summary>
    /// Default statistics instance used by overloads that do not explicitly provide a <see cref="TimeoutStatistics"/>.
    /// </summary>
    public static TimeoutStatistics DefaultStatistics { get; } = new();

    /// <summary>
    /// Creates a <see cref="CancellationTokenSource"/> that will be cancelled after the specified timeout using the provided
    /// <see cref="TimeProvider"/>.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="CancellationTokenSource"/> does not own the underlying timer; the timer is disposed when the timeout
    /// fires. If you need disposal of the timer to be tied to your own lifetime management, use
    /// <see cref="CreateTimeoutHandle(TimeProvider, TimeSpan)"/>.
    /// </remarks>
    public static CancellationTokenSource CreateTimeout(TimeProvider timeProvider, TimeSpan timeout)
        => CreateTimeout(timeProvider, timeout, DefaultStatistics);

    /// <summary>
    /// Creates a <see cref="CancellationTokenSource"/> that will be cancelled after the specified timeout using the provided
    /// <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="timeProvider">The time provider used to schedule the timeout. Cannot be null.</param>
    /// <param name="timeout">
    /// The duration to wait before the timeout is triggered. If the value is less than or equal to zero, the timeout is
    /// considered to have already expired.
    /// </param>
    /// <param name="statistics">An optional statistics object used to record timeout events. May be null.</param>
    /// <returns>
    /// A <see cref="CancellationTokenSource"/> that will be cancelled when the timeout elapses. For non-positive timeouts, the returned
    /// source is already cancelled.
    /// </returns>
    public static CancellationTokenSource CreateTimeout(TimeProvider timeProvider, TimeSpan timeout, TimeoutStatistics? statistics)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        statistics?.RecordCreated();

        var cts = new CancellationTokenSource();

        if (timeout <= TimeSpan.Zero)
        {
            cts.Cancel();
            statistics?.RecordFired();
            statistics?.RecordDisposed();
            return cts;
        }

        var state = new TimeoutState { Cts = cts, Statistics = statistics };
        state.Timer = timeProvider.CreateTimer(static s =>
        {
            var st = (TimeoutState)s!;
            try
            {
                st.Cts.Cancel();
                st.Statistics?.RecordFired();
            }
            finally
            {
                st.Timer?.Dispose();
                st.Statistics?.RecordDisposed();
            }
        }, state, timeout, Timeout.InfiniteTimeSpan);

        return cts;
    }

    /// <summary>
    /// Creates a new <see cref="TimeoutHandle"/> that will be triggered after the specified timeout interval using the provided
    /// <see cref="TimeProvider"/>.
    /// </summary>
    public static TimeoutHandle CreateTimeoutHandle(TimeProvider timeProvider, TimeSpan timeout)
        => CreateTimeoutHandle(timeProvider, timeout, DefaultStatistics);

    /// <summary>
    /// Creates a new <see cref="TimeoutHandle"/> that will be triggered after the specified timeout interval using the provided
    /// <see cref="TimeProvider"/>.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="TimeoutHandle"/> encapsulates a <see cref="CancellationTokenSource"/> that is cancelled when the timeout elapses.
    /// Disposing the handle cancels the token source (if not already cancelled), disposes the underlying timer, and disposes the token source.
    /// 
    /// If <paramref name="timeout"/> is non-positive, the token source is cancelled immediately and no timer is created.
    /// </remarks>
    /// <param name="timeProvider">The time provider used to schedule the timeout. Cannot be null.</param>
    /// <param name="timeout">
    /// The duration to wait before the timeout is triggered. If the value is less than or equal to zero, the timeout is
    /// considered to have already expired.
    /// </param>
    /// <param name="statistics">An optional statistics object used to record timeout events. May be null.</param>
    /// <returns>
    /// A <see cref="TimeoutHandle"/> that can be disposed to cancel the timeout and release the underlying resources.
    /// </returns>
    public static TimeoutHandle CreateTimeoutHandle(TimeProvider timeProvider, TimeSpan timeout, TimeoutStatistics? statistics)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        statistics?.RecordCreated();

        var cts = new CancellationTokenSource();

        if (timeout <= TimeSpan.Zero)
        {
            cts.Cancel();
            statistics?.RecordFired();
            statistics?.RecordDisposed();
            return new TimeoutHandle(cts, timer: null, statistics, timerAlreadyDisposed: true);
        }

        var state = new TimeoutState { Cts = cts, Statistics = statistics };
        state.Timer = timeProvider.CreateTimer(static s =>
        {
            var st = (TimeoutState)s!;
            try
            {
                st.Cts.Cancel();
                st.Statistics?.RecordFired();
            }
            finally
            {
                st.Timer?.Dispose();
                st.Statistics?.RecordDisposed();
            }
        }, state, timeout, Timeout.InfiniteTimeSpan);

        return new TimeoutHandle(cts, state.Timer, statistics, timerAlreadyDisposed: false);
    }

    /// <summary>
    /// Represents a handle to a timeout operation.
    /// </summary>
    /// <remarks>
    /// Dispose the handle to cancel the timeout (if not already cancelled) and to dispose the underlying timer and token source.
    /// Accessing <see cref="Token"/> after disposal may throw <see cref="ObjectDisposedException"/>, consistent with
    /// <see cref="CancellationTokenSource"/>.
    /// </remarks>
    public sealed class TimeoutHandle : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly ITimer? _timer;
        private readonly TimeoutStatistics? _statistics;
        private int _disposed;
        private readonly bool _timerAlreadyDisposed;

        internal TimeoutHandle(CancellationTokenSource cts, ITimer? timer, TimeoutStatistics? statistics, bool timerAlreadyDisposed)
        {
            _cts = cts;
            _timer = timer;
            _statistics = statistics;
            _timerAlreadyDisposed = timerAlreadyDisposed;
        }

        /// <summary>
        /// Gets the token that is cancelled when the timeout elapses.
        /// </summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>
        /// Gets the underlying token source.
        /// </summary>
        public CancellationTokenSource CancellationTokenSource => _cts;

        /// <summary>
        /// Cancels the underlying token source (if not already cancelled) and disposes the underlying timer and token source.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (!_timerAlreadyDisposed)
            {
                _timer?.Dispose();
                _statistics?.RecordDisposed();
            }

            _cts.Dispose();
        }
    }

    private sealed class TimeoutState
    {
        public required CancellationTokenSource Cts { get; init; }
        public TimeoutStatistics? Statistics { get; init; }
        public ITimer? Timer { get; set; }
    }
}
