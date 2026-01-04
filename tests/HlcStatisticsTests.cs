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
}
