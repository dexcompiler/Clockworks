using Xunit;

namespace Clockworks.Tests;

public sealed class HlcGuidFactoryNodeIdTests
{
    [Fact]
    public void Constructor_AllowsMaximumEncodableNodeId()
    {
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new HlcGuidFactory(time, HlcGuidFactory.MaxNodeId, rng: rng);

        var (guid, timestamp) = factory.NewGuidWithHlc();
        var decoded = guid.ToHlcTimestamp();

        Assert.Equal(HlcGuidFactory.MaxNodeId, timestamp.NodeId);
        Assert.Equal(HlcGuidFactory.MaxNodeId, guid.GetNodeId());
        Assert.NotNull(decoded);
        Assert.Equal(HlcGuidFactory.MaxNodeId, decoded.Value.NodeId);
    }

    [Fact]
    public void Constructor_RejectsNodeIdsThatCannotBeEncodedInUuid()
    {
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        var unsupportedNodeId = (ushort)(HlcGuidFactory.MaxNodeId + 1);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new HlcGuidFactory(time, unsupportedNodeId));

        Assert.Equal("nodeId", ex.ParamName);
        Assert.Equal(unsupportedNodeId, ex.ActualValue);
    }
}
