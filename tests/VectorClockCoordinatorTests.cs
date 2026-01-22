using Xunit;
using Clockworks.Distributed;

namespace Clockworks.Tests;

public sealed class VectorClockCoordinatorTests
{
    [Fact]
    public void NewCoordinator_StartsWithEmptyClock()
    {
        var coord = new VectorClockCoordinator(1);

        Assert.Equal(new VectorClock(), coord.Current);
    }

    [Fact]
    public void BeforeSend_IncrementsLocalCounter()
    {
        var coord = new VectorClockCoordinator(1);

        var clock = coord.BeforeSend();

        Assert.Equal(1UL, clock.Get(1));
    }

    [Fact]
    public void BeforeSend_MultipleTimes_IncrementsCounter()
    {
        var coord = new VectorClockCoordinator(1);

        coord.BeforeSend();
        coord.BeforeSend();
        var clock = coord.BeforeSend();

        Assert.Equal(3UL, clock.Get(1));
    }

    [Fact]
    public void NewLocalEvent_IncrementsLocalCounter()
    {
        var coord = new VectorClockCoordinator(1);

        coord.NewLocalEvent();
        coord.NewLocalEvent();

        Assert.Equal(2UL, coord.Current.Get(1));
    }

    [Fact]
    public void BeforeReceive_MergesRemoteClock()
    {
        var coord = new VectorClockCoordinator(1);

        var remote = new VectorClock().Increment(2).Increment(2).Increment(2);
        coord.BeforeReceive(remote);

        var current = coord.Current;
        Assert.Equal(1UL, current.Get(1)); // Incremented after merge
        Assert.Equal(3UL, current.Get(2)); // Merged from remote
    }

    [Fact]
    public void BeforeReceive_MergesAndIncrementsLocal()
    {
        var coord = new VectorClockCoordinator(1);
        coord.BeforeSend(); // Local is now [1:1]

        var remote = new VectorClock().Increment(2).Increment(2); // Remote is [2:2]
        coord.BeforeReceive(remote);

        var current = coord.Current;
        Assert.Equal(2UL, current.Get(1)); // Was 1, merged with 0, then incremented
        Assert.Equal(2UL, current.Get(2)); // Merged from remote
    }

    [Fact]
    public void HappensBefore_MessageSendReceive_PreservesOrdering()
    {
        var coordA = new VectorClockCoordinator(1);
        var coordB = new VectorClockCoordinator(2);

        // A sends
        var clockA = coordA.BeforeSend();

        // B receives
        coordB.BeforeReceive(clockA);

        // B sends (after receiving from A)
        var clockB = coordB.BeforeSend();

        Assert.True(clockA.HappensBefore(clockB));
        Assert.False(clockB.HappensBefore(clockA));
    }

    [Fact]
    public void Concurrent_IndependentNodes_DetectsConcurrency()
    {
        var coordA = new VectorClockCoordinator(1);
        var coordB = new VectorClockCoordinator(2);

        // A and B each generate events independently
        var clockA = coordA.BeforeSend();
        var clockB = coordB.BeforeSend();

        Assert.True(clockA.IsConcurrentWith(clockB));
        Assert.True(clockB.IsConcurrentWith(clockA));
    }

    [Fact]
    public void BidirectionalExchange_MaintainsCausality()
    {
        var coordA = new VectorClockCoordinator(1);
        var coordB = new VectorClockCoordinator(2);

        // A sends to B
        var a1 = coordA.BeforeSend();
        coordB.BeforeReceive(a1);

        // B sends to A
        var b1 = coordB.BeforeSend();
        coordA.BeforeReceive(b1);

        // A sends again
        var a2 = coordA.BeforeSend();

        // Verify causality chain
        Assert.True(a1.HappensBefore(b1));
        Assert.True(b1.HappensBefore(a2));
        Assert.True(a1.HappensBefore(a2)); // Transitivity
    }

    [Fact]
    public void Statistics_TracksSendReceiveLocal()
    {
        var coord = new VectorClockCoordinator(1);

        coord.BeforeSend();
        coord.BeforeSend();
        coord.NewLocalEvent();
        coord.BeforeReceive(new VectorClock().Increment(2));

        Assert.Equal(1, coord.Statistics.LocalEventCount);
        Assert.Equal(2, coord.Statistics.SendCount);
        Assert.Equal(1, coord.Statistics.ReceiveCount);
        Assert.Equal(1, coord.Statistics.ClockMerges);
    }

    [Fact]
    public void Statistics_Reset_ClearsCounters()
    {
        var coord = new VectorClockCoordinator(1);

        coord.BeforeSend();
        coord.NewLocalEvent();
        coord.BeforeReceive(new VectorClock().Increment(2));

        coord.Statistics.Reset();

        Assert.Equal(0, coord.Statistics.LocalEventCount);
        Assert.Equal(0, coord.Statistics.SendCount);
        Assert.Equal(0, coord.Statistics.ReceiveCount);
        Assert.Equal(0, coord.Statistics.ClockMerges);
    }

    [Fact]
    public async Task Coordinator_ThreadSafe_ConcurrentAccess()
    {
        var coord = new VectorClockCoordinator(1);
        var tasks = new List<Task>();

        for (var i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => coord.BeforeSend()));
            tasks.Add(Task.Run(() => coord.NewLocalEvent()));
            tasks.Add(Task.Run(() => coord.BeforeReceive(new VectorClock().Increment(2))));
        }

        await Task.WhenAll(tasks);

        // Should have incremented 300 times (100 sends, 100 local events, 100 receives)
        Assert.Equal(300UL, coord.Current.Get(1));
    }
}

public sealed class VectorClockMessageHeaderTests
{
    [Fact]
    public void ToString_Parse_RoundTrips_WithClockOnly()
    {
        var header = new VectorClockMessageHeader(
            clock: new VectorClock().Increment(1).Increment(2).Increment(2));

        var str = header.ToString();
        var parsed = VectorClockMessageHeader.Parse(str);

        Assert.Equal(header.Clock, parsed.Clock);
        Assert.Null(parsed.CorrelationId);
        Assert.Null(parsed.CausationId);
    }

    [Fact]
    public void ToString_Parse_RoundTrips_WithAllFields()
    {
        var header = new VectorClockMessageHeader(
            clock: new VectorClock().Increment(1).Increment(2).Increment(2),
            correlationId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            causationId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        var str = header.ToString();
        var parsed = VectorClockMessageHeader.Parse(str);

        Assert.Equal(header.Clock, parsed.Clock);
        Assert.Equal(header.CorrelationId, parsed.CorrelationId);
        Assert.Equal(header.CausationId, parsed.CausationId);
    }

    [Fact]
    public void ToString_WithCorrelationOnly_OmitsCausation()
    {
        var header = new VectorClockMessageHeader(
            clock: new VectorClock().Increment(1),
            correlationId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        var str = header.ToString();
        var parsed = VectorClockMessageHeader.Parse(str);

        Assert.Equal(header.Clock, parsed.Clock);
        Assert.Equal(header.CorrelationId, parsed.CorrelationId);
        Assert.Null(parsed.CausationId);
    }

    [Fact]
    public void ToString_EmptyClock_ProducesEmptyString()
    {
        var header = new VectorClockMessageHeader(new VectorClock());

        var str = header.ToString();

        Assert.Equal(string.Empty, str);
    }

    [Fact]
    public void TryParse_ValidString_ReturnsTrue()
    {
        var str = "1:2,2:1;aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        Assert.True(VectorClockMessageHeader.TryParse(str, out var result));
        Assert.Equal(2UL, result.Clock.Get(1));
        Assert.Equal(1UL, result.Clock.Get(2));
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), result.CorrelationId);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), result.CausationId);
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        Assert.False(VectorClockMessageHeader.TryParse("", out _));
        Assert.False(VectorClockMessageHeader.TryParse(null, out _));
    }

    [Fact]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        Assert.False(VectorClockMessageHeader.TryParse("invalid", out _));
        Assert.False(VectorClockMessageHeader.TryParse("1:2:3", out _));
    }

    [Fact]
    public void GoldenString_Parses_AsExpected()
    {
        const string value = "1:3,5:2,10:1;aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        var parsed = VectorClockMessageHeader.Parse(value);

        Assert.Equal(3UL, parsed.Clock.Get(1));
        Assert.Equal(2UL, parsed.Clock.Get(5));
        Assert.Equal(1UL, parsed.Clock.Get(10));
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), parsed.CorrelationId);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), parsed.CausationId);
    }

    [Fact]
    public void HeaderName_IsCorrect()
    {
        Assert.Equal("X-VectorClock", VectorClockMessageHeader.HeaderName);
    }
}
