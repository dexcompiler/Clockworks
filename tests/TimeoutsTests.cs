using Xunit;
using Clockworks.Instrumentation;

namespace Clockworks.Tests;

public sealed class TimeoutsTests
{
    [Fact]
    public void CreateTimeoutHandle_Dispose_CancelsToken_AndDisposesTimer()
    {
        var tp = new SimulatedTimeProvider();
        var stats = new TimeoutStatistics();

        using var handle = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(10), stats);

        Assert.False(handle.Token.IsCancellationRequested);
        Assert.Equal(1, stats.Created);
        Assert.Equal(0, stats.Fired);
        Assert.Equal(0, stats.Disposed);

        handle.Dispose();

        Assert.Equal(0, stats.Fired);
        Assert.Equal(1, stats.Disposed);

        tp.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(0, stats.Fired);
        Assert.Equal(1, stats.Disposed);
    }

    [Fact]
    public void CreateTimeoutHandle_ImmediateTimeout_FiresAndDisposesImmediately()
    {
        var tp = new SimulatedTimeProvider();
        var stats = new TimeoutStatistics();

        using var handle = Timeouts.CreateTimeoutHandle(tp, TimeSpan.Zero, stats);

        Assert.True(handle.Token.IsCancellationRequested);
        Assert.Equal(1, stats.Created);
        Assert.Equal(1, stats.Fired);
        Assert.Equal(1, stats.Disposed);

        handle.Dispose();

        Assert.Equal(1, stats.Disposed);
    }

    [Fact]
    public void CreateTimeoutHandle_Dispose_IsIdempotent_ForStatistics()
    {
        var tp = new SimulatedTimeProvider();
        var stats = new TimeoutStatistics();

        var handle = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(10), stats);

        handle.Dispose();
        handle.Dispose();

        Assert.Equal(1, stats.Created);
        Assert.Equal(0, stats.Fired);
        Assert.Equal(1, stats.Disposed);

        tp.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(0, stats.Fired);
        Assert.Equal(1, stats.Disposed);
    }

    [Fact]
    public void CreateTimeout_DisposingReturnedCts_CausesTimerCallbackToThrow_WhenItFires()
    {
        var tp = new SimulatedTimeProvider();
        var stats = new TimeoutStatistics();

        using var cts = Timeouts.CreateTimeout(tp, TimeSpan.FromSeconds(5), stats);
        cts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tp.Advance(TimeSpan.FromSeconds(5)));

        Assert.Equal(1, stats.Created);
    }
}
