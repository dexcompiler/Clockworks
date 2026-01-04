using Xunit;
using Clockworks.Distributed;

namespace Clockworks.Tests;

public sealed class HlcCausalityTests
{
    [Fact]
    public void HappensBefore_MessageSendReceive_PreservesOrdering()
    {
        var tp = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);

        using var a = new HlcGuidFactory(tp, nodeId: 1);
        using var b = new HlcGuidFactory(tp, nodeId: 2);

        var coordA = new HlcCoordinator(a);
        var coordB = new HlcCoordinator(b);

        // A sends
        var t1 = coordA.BeforeSend();

        // B receives (witnesses)
        coordB.BeforeReceive(t1);

        // B local event after receiving should be strictly after the received timestamp
        var t2 = coordB.BeforeSend();

        Assert.True(t1 < t2);
    }

    [Fact]
    public void BidirectionalMessageExchange_CurrentTimestampIsNonDecreasing()
    {
        var tp = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);

        using var a = new HlcGuidFactory(tp, nodeId: 1);
        using var b = new HlcGuidFactory(tp, nodeId: 2);

        var coordA = new HlcCoordinator(a);
        var coordB = new HlcCoordinator(b);

        var a0 = coordA.CurrentTimestamp;
        var b0 = coordB.CurrentTimestamp;

        var a1 = coordA.BeforeSend();
        coordB.BeforeReceive(a1);
        var b1 = coordB.CurrentTimestamp;

        var b2 = coordB.BeforeSend();
        coordA.BeforeReceive(b2);
        var a2 = coordA.CurrentTimestamp;

        Assert.True(a0 <= a1);
        Assert.True(b0 <= b1);
        Assert.True(a1 <= a2);
        Assert.True(b1 <= b2);
    }
}
