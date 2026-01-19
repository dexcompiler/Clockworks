using Xunit;
using Clockworks.Distributed;

namespace Clockworks.Tests;

public sealed class HlcStatisticsTests
{
    [Fact]
    public void HlcStatistics_tracks_ahead_behind_and_drift()
    {
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        coordinator.Statistics.Reset();

        // Remote ahead by +5000ms
        coordinator.BeforeReceive(new HlcTimestamp(15_000));

        Assert.Equal(1, coordinator.Statistics.ReceiveCount);
        Assert.Equal(1, coordinator.Statistics.RemoteAheadCount);
        Assert.True(coordinator.Statistics.MaxRemoteAheadMs >= 5000);
        Assert.True(coordinator.Statistics.MaxObservedDriftMs >= 5000);

        // Remote behind by -2000ms
        coordinator.BeforeReceive(new HlcTimestamp(8_000));

        Assert.Equal(2, coordinator.Statistics.ReceiveCount);
        Assert.True(coordinator.Statistics.MaxRemoteBehindMs >= 2000);
    }

    [Fact]
    public void ClockAdvances_increments_when_remote_ahead_causes_advance()
    {
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        coordinator.Statistics.Reset();

        // Remote timestamp ahead of local time should cause clock to advance
        coordinator.BeforeReceive(new HlcTimestamp(15_000));

        Assert.Equal(1, coordinator.Statistics.ClockAdvances);
    }

    [Fact]
    public void ClockAdvances_does_not_increment_when_remote_behind()
    {
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        coordinator.Statistics.Reset();

        // Remote timestamp behind local time should not cause clock advance
        coordinator.BeforeReceive(new HlcTimestamp(5_000));

        Assert.Equal(0, coordinator.Statistics.ClockAdvances);
    }

    [Fact]
    public void ClockAdvances_does_not_increment_when_physical_time_is_max()
    {
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        coordinator.Statistics.Reset();

        // Advance physical time significantly
        tp.SetUtcNow(tp.GetUtcNow().AddMilliseconds(20_000));

        // Remote timestamp behind current physical time
        // Physical time will be the max, not remote
        coordinator.BeforeReceive(new HlcTimestamp(15_000));

        // Clock advanced, but due to physical time, not remote
        Assert.Equal(0, coordinator.Statistics.ClockAdvances);
    }

    [Fact]
    public void ClockAdvances_increments_correctly_across_multiple_receives()
    {
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        coordinator.Statistics.Reset();

        // First remote ahead - should increment
        coordinator.BeforeReceive(new HlcTimestamp(15_000));
        Assert.Equal(1, coordinator.Statistics.ClockAdvances);

        // Second remote behind current - should not increment
        coordinator.BeforeReceive(new HlcTimestamp(12_000));
        Assert.Equal(1, coordinator.Statistics.ClockAdvances);

        // Third remote ahead again - should increment
        coordinator.BeforeReceive(new HlcTimestamp(20_000));
        Assert.Equal(2, coordinator.Statistics.ClockAdvances);
    }

    [Fact]
    public void ClockAdvances_does_not_increment_when_remote_equals_local()
    {
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        coordinator.Statistics.Reset();

        // Get current timestamp
        var current = coordinator.CurrentTimestamp;

        // Remote timestamp equal to local time should not cause advance
        coordinator.BeforeReceive(new HlcTimestamp(current.WallTimeMs));

        // No advance due to remote (logical time already at this value, just counter increments)
        Assert.Equal(0, coordinator.Statistics.ClockAdvances);
    }

    [Fact]
    public void ClockAdvances_edge_case_remote_greater_than_after()
    {
        // This test case exposes the bug in the current implementation
        // If remote > local but physical time catches up and becomes the max,
        // then after > remote, but the current buggy condition would still
        // increment ClockAdvances because remote >= after is false, so it won't increment.
        // 
        // Actually, let me think about this differently...
        // Current condition: after > before AND remote >= after
        // This would be true when remote is much larger than both before and after
        // But the correct condition should be: after == remote (meaning remote was adopted)
        
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        coordinator.Statistics.Reset();

        // Let's say local is at 10_000, we receive remote at 12_000
        // but remote is WAY ahead at 50_000
        coordinator.BeforeReceive(new HlcTimestamp(50_000));

        // The clock advances to 50_000 (remote was the max and was adopted)
        // Current condition: after.WallTimeMs (50_000) > before.WallTimeMs (10_000) ✓
        //                    remote.WallTimeMs (50_000) >= after.WallTimeMs (50_000) ✓
        // So it would increment - this is actually correct!
        Assert.Equal(1, coordinator.Statistics.ClockAdvances);
    }

    [Fact]
    public void ClockAdvances_diagnostic_test()
    {
        // Let's trace through various scenarios to understand the current logic
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        // Scenario: Physical time advances beyond remote
        // This should NOT count as clock advance due to remote
        coordinator.Statistics.Reset();
        tp.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(20_000));
        var before = coordinator.CurrentTimestamp;
        coordinator.BeforeReceive(new HlcTimestamp(15_000));
        var after = coordinator.CurrentTimestamp;
        
        // In this case:
        // before.WallTimeMs = ~10_000 (initial)
        // remote.WallTimeMs = 15_000
        // physical = 20_000
        // maxTime = 20_000 (physical is max)
        // after.WallTimeMs = 20_000
        // 
        // Current condition: (20_000 > 10_000) && (15_000 >= 20_000) = true && false = false
        // So ClockAdvances would NOT increment - this is CORRECT!
        Assert.Equal(0, coordinator.Statistics.ClockAdvances);
    }

    [Fact]
    public void ClockAdvances_does_not_increment_on_counter_overflow()
    {
        // Edge case: If the counter overflows when local time is already the max,
        // the wall time would advance by 1ms but this is NOT due to the remote timestamp
        var tp = SimulatedTimeProvider.FromUnixMs(10_000);
        using var factory = new HlcGuidFactory(tp, nodeId: 1);
        var coordinator = new HlcCoordinator(factory);

        coordinator.Statistics.Reset();
        
        // First, advance local clock ahead of physical time
        coordinator.BeforeReceive(new HlcTimestamp(15_000));
        Assert.Equal(1, coordinator.Statistics.ClockAdvances);
        
        // Now local is at 15_000. Generate many events to potentially overflow counter
        // (MaxCounterValue = 0xFFF = 4095)
        for (int i = 0; i < 4100; i++)
        {
            coordinator.NewLocalEventGuid();
        }
        
        coordinator.Statistics.Reset();
        
        // Now receive a remote timestamp that's behind local
        // If counter overflows during Witness, wall time might advance but not due to remote
        var beforeReceive = coordinator.CurrentTimestamp;
        coordinator.BeforeReceive(new HlcTimestamp(14_000));
        var afterReceive = coordinator.CurrentTimestamp;
        
        // Even if wall time advanced due to counter overflow, this should NOT
        // count as a clock advance due to remote (remote was behind)
        // Our condition: remote > before AND after == remote
        // Here: 14_000 > before (false) so ClockAdvances should NOT increment
        Assert.Equal(0, coordinator.Statistics.ClockAdvances);
    }
}
