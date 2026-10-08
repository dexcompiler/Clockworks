using System.Runtime.CompilerServices;
using Xunit;
using Clockworks.Instrumentation;

namespace Clockworks.Tests;

public sealed class SimulatedTimeProviderTests
{
    private sealed class Box { public int Value; }

    [Fact]
    public void One_shot_timer_fires_after_advance()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        using var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        tp.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0, box.Value);

        tp.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, box.Value);
    }

    [Fact]
    public void Periodic_timer_coalesces_on_large_jumps()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        using var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        tp.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(1, box.Value);

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, box.Value);
    }

    [Fact]
    public void Timeout_helper_is_time_provider_driven()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        using var cts = Timeouts.CreateTimeout(tp, TimeSpan.FromSeconds(5));

        Assert.False(cts.IsCancellationRequested);
        tp.Advance(TimeSpan.FromSeconds(6));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Timer_change_reschedules_due_time()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        using var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        tp.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, box.Value);

        Assert.True(timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, box.Value);
    }

    [Fact]
    public void Timer_dispose_prevents_firing()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        timer.Dispose();

        tp.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, box.Value);
    }

    [Fact]
    public void Multiple_timers_fire_in_due_time_order()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var list = new List<int>();
        using var t1 = tp.CreateTimer(static s => ((List<int>)s!).Add(1), list, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        using var t2 = tp.CreateTimer(static s => ((List<int>)s!).Add(2), list, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        tp.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal([2, 1], list);
    }

    [Fact]
    public void Timers_with_same_due_time_fire_in_creation_order()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var list = new List<int>();
        using var t1 = tp.CreateTimer(static s => ((List<int>)s!).Add(1), list, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using var t2 = tp.CreateTimer(static s => ((List<int>)s!).Add(2), list, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        tp.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([1, 2], list);
    }

    [Fact]
    public void SetUtcNow_does_not_affect_scheduler_timeouts()
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        using var cts = Timeouts.CreateTimeout(tp, TimeSpan.FromSeconds(5));

        // Move wall time far into the future without advancing scheduler time.
        tp.SetUtcNow(DateTimeOffset.UnixEpoch.AddYears(50));
        Assert.False(cts.IsCancellationRequested);

        // Still only scheduler time advance should trigger cancellation.
        tp.Advance(TimeSpan.FromSeconds(6));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Periodic_timer_can_be_stopped_via_change_infinite_due_time()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        using var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, box.Value);

        Assert.True(timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan));

        tp.Advance(TimeSpan.FromSeconds(100));
        Assert.Equal(1, box.Value);
    }

    [Fact]
    public void Periodic_timer_dispose_stops_future_fires()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, box.Value);

        timer.Dispose();

        tp.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, box.Value);
    }

    [Fact]
    public void Timer_callback_can_dispose_itself()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        ITimer? timer = null;
        timer = tp.CreateTimer(_ =>
        {
            timer!.Dispose();
            box.Value++;
        }, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, box.Value);

        tp.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, box.Value);
    }

    [Fact]
    public void Timer_change_returns_false_after_dispose()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var timer = tp.CreateTimer(static _ => { }, state: null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        timer.Dispose();

        Assert.False(timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));
    }

    [Fact]
    public void Timer_can_change_from_one_shot_to_periodic()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        using var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        Assert.True(timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, box.Value);

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, box.Value);
    }

    [Fact]
    public void Timer_can_change_from_periodic_to_one_shot()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var box = new Box();
        using var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, box.Value);

        Assert.True(timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan));

        tp.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, box.Value);

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, box.Value);

        tp.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, box.Value);
    }

    [Fact]
    public void SimulatedTimeProvider_records_basic_statistics()
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        tp.Statistics.Reset();

        var box = new Box();
        using var timer = tp.CreateTimer(static s => Interlocked.Increment(ref ((Box)s!).Value), box, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        Assert.Equal(1, tp.Statistics.TimersCreated);
        Assert.Equal(1, tp.Statistics.QueueEnqueues);

        tp.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, tp.Statistics.AdvanceCalls);
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, tp.Statistics.AdvanceTicks);
        Assert.Equal(1, tp.Statistics.CallbacksFired);
        Assert.True(tp.Statistics.TimersDisposed >= 1);
    }

    [Fact]
    public void Timeouts_statistics_are_recorded()
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        var stats = new TimeoutStatistics();

        using var cts = Timeouts.CreateTimeout(tp, TimeSpan.FromSeconds(5), stats);
        Assert.Equal(1, stats.Created);
        Assert.Equal(0, stats.Fired);

        tp.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(1, stats.Fired);
        Assert.Equal(1, stats.Disposed);
    }

    [Fact]
    public void Periodic_timer_coalesces_and_schedules_next_tick_from_now()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var list = new List<long>();
        using var timer = tp.CreateTimer(_ => list.Add(tp.GetUtcNow().ToUnixTimeMilliseconds()), state: null,
            dueTime: TimeSpan.FromSeconds(1), period: TimeSpan.FromSeconds(1));

        tp.Advance(TimeSpan.FromSeconds(10));
        Assert.Single(list);

        var first = list[0];

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, list.Count);
        Assert.Equal(first + 1000, list[1]);
    }

    [Fact]
    public void Timer_callback_can_change_itself()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        ITimer? timer = null;
        var list = new List<int>();

        timer = tp.CreateTimer(_ =>
        {
            list.Add(1);
            Assert.False(timer!.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan));
        }, state: null, dueTime: TimeSpan.FromSeconds(1), period: Timeout.InfiniteTimeSpan);

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([1], list);
    }

    [Fact]
    public void Timer_callback_can_make_another_timer_due_and_it_fires_on_next_advance()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        var list = new List<int>();

        ITimer? t2 = null;
        t2 = tp.CreateTimer(_ => list.Add(2), state: null, dueTime: TimeSpan.FromSeconds(100), period: Timeout.InfiniteTimeSpan);

        using var t1 = tp.CreateTimer(_ =>
        {
            list.Add(1);
            Assert.True(t2.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
        }, state: null, dueTime: TimeSpan.FromSeconds(1), period: Timeout.InfiniteTimeSpan);

        tp.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([1], list);

        tp.Advance(TimeSpan.Zero);
        Assert.Equal([1, 2], list);
    }

    [Fact]
    public void Changing_the_earliest_timer_later_does_not_hide_a_due_timer()
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        var fired = new List<string>();

        using var a = tp.CreateTimer(_ => fired.Add("a"), state: null, dueTime: TimeSpan.FromSeconds(10), period: Timeout.InfiniteTimeSpan);
        using var b = tp.CreateTimer(_ => fired.Add("b"), state: null, dueTime: TimeSpan.FromSeconds(20), period: Timeout.InfiniteTimeSpan);

        Assert.True(a.Change(TimeSpan.FromSeconds(25), Timeout.InfiniteTimeSpan));

        tp.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(["b"], fired);

        tp.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(["b", "a"], fired);
    }

    [Fact]
    public void Stopping_the_earliest_timer_does_not_block_later_timers()
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        var fired = new List<string>();

        using var a = tp.CreateTimer(_ => fired.Add("a"), state: null, dueTime: TimeSpan.FromSeconds(10), period: Timeout.InfiniteTimeSpan);
        using var b = tp.CreateTimer(_ => fired.Add("b"), state: null, dueTime: TimeSpan.FromSeconds(20), period: Timeout.InfiniteTimeSpan);

        Assert.True(a.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan));

        tp.Advance(TimeSpan.FromDays(1));
        Assert.Equal(["b"], fired);
    }

    [Fact]
    public void CancelAfter_on_a_provider_source_does_not_hide_a_due_delay()
    {
        var tp = SimulatedTimeProvider.FromEpoch();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1), tp);
        var delay = Task.Delay(TimeSpan.FromSeconds(2), tp);

        // CancelAfter reschedules the source's timer through ITimer.Change.
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        tp.Advance(TimeSpan.FromSeconds(3));
        Assert.True(delay.IsCompletedSuccessfully);
        Assert.False(cts.IsCancellationRequested);

        tp.Advance(TimeSpan.FromSeconds(2));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Timer_disposed_by_an_earlier_callback_in_the_same_advance_does_not_fire()
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        var fired = new List<string>();

        ITimer? second = null;
        using var first = tp.CreateTimer(_ =>
        {
            fired.Add("first");
            second!.Dispose();
        }, state: null, dueTime: TimeSpan.FromSeconds(1), period: Timeout.InfiniteTimeSpan);
        second = tp.CreateTimer(_ => fired.Add("second"), state: null, dueTime: TimeSpan.FromSeconds(2), period: Timeout.InfiniteTimeSpan);

        tp.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(["first"], fired);
    }

    [Fact]
    public void Timer_rescheduled_by_an_earlier_callback_in_the_same_advance_fires_at_its_new_due_time()
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        var fired = new List<string>();

        ITimer? second = null;
        using var first = tp.CreateTimer(_ =>
        {
            fired.Add("first");
            Assert.True(second!.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan));
        }, state: null, dueTime: TimeSpan.FromSeconds(1), period: Timeout.InfiniteTimeSpan);
        using var secondTimer = second = tp.CreateTimer(_ => fired.Add("second"), state: null, dueTime: TimeSpan.FromSeconds(2), period: Timeout.InfiniteTimeSpan);

        tp.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(["first"], fired);

        tp.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(["first", "second"], fired);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("far-future")]
    public void Rescheduled_timers_do_not_accumulate_behind_a_live_periodic_timer(string firstSchedule)
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        using var heartbeat = tp.CreateTimer(static _ => { }, state: null, dueTime: TimeSpan.FromSeconds(1), period: TimeSpan.FromSeconds(1));

        var payloads = ArmFireAndDispose(tp, 1_000, firstSchedule == "disabled" ? Timeout.InfiniteTimeSpan : TimeSpan.FromDays(1));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // At most the heartbeat and one armed timer are live at a time, so the queue never needs more than
        // twice that, and no fired, disposed timer keeps its state reachable.
        Assert.InRange(tp.Statistics.MaxQueueLength, 1, 4);
        Assert.DoesNotContain(payloads, payload => payload.IsAlive);
        GC.KeepAlive(heartbeat);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] ArmFireAndDispose(SimulatedTimeProvider tp, int count, TimeSpan firstDueTime)
    {
        var payloads = new WeakReference[count];
        for (var i = 0; i < count; i++)
        {
            var payload = new byte[1024];
            payloads[i] = new WeakReference(payload);
            using var timer = tp.CreateTimer(static _ => { }, payload, firstDueTime, Timeout.InfiniteTimeSpan);
            Assert.True(timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));
            tp.Advance(TimeSpan.FromSeconds(1));
        }

        return payloads;
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("fired")]
    public void A_finished_timer_releases_its_state_while_a_stale_entry_for_it_is_queued(string ending)
    {
        var tp = SimulatedTimeProvider.FromEpoch();
        var live = new ITimer[4];
        for (var i = 0; i < live.Length; i++)
        {
            live[i] = tp.CreateTimer(static _ => { }, state: null, dueTime: TimeSpan.FromHours(1), period: Timeout.InfiniteTimeSpan);
        }

        // Four live timers outnumber the one stale entry, and it is due after them, so the queue keeps it.
        var payload = ArmAndFinish(tp, ending);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(payload.IsAlive);
        GC.KeepAlive(live);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ArmAndFinish(SimulatedTimeProvider tp, string ending)
    {
        var payload = new byte[1024];
        var timer = tp.CreateTimer(static _ => { }, payload, TimeSpan.FromHours(2), Timeout.InfiniteTimeSpan);
        if (ending == "disposed")
        {
            timer.Dispose();
        }
        else
        {
            Assert.True(timer.Change(TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan));
            tp.Advance(TimeSpan.FromMilliseconds(1));
        }

        return new WeakReference(payload);
    }
}
