using Xunit;

namespace Clockworks.Tests;

public sealed class HlcGuidFactoryDriftTests
{
    [Fact]
    public void NewGuidWithHlc_WhenClockMovesBackwardBeyondMaxDrift_ThrowsImmediately_WhenStrict()
    {
        var tp = new SimulatedTimeProvider();

        using var factory = new HlcGuidFactory(
            tp,
            nodeId: 1,
            options: new HlcOptions { MaxDriftMs = 0, ThrowOnExcessiveDrift = true });

        _ = factory.NewGuidWithHlc();

        var now = tp.GetUtcNow();
        tp.SetUtcNow(now - TimeSpan.FromMilliseconds(1));

        Assert.Throws<HlcDriftException>(() => factory.NewGuidWithHlc());
    }

    [Fact]
    public void NewGuidWithHlc_WhenClockMovesBackwardBeyondMaxDrift_DoesNotThrow_WhenNotStrict()
    {
        var tp = new SimulatedTimeProvider();

        using var factory = new HlcGuidFactory(
            tp,
            nodeId: 1,
            options: new HlcOptions { MaxDriftMs = 0, ThrowOnExcessiveDrift = false });

        _ = factory.NewGuidWithHlc();

        var now = tp.GetUtcNow();
        tp.SetUtcNow(now - TimeSpan.FromMilliseconds(1));

        var ex = Record.Exception(() => factory.NewGuidWithHlc());
        Assert.Null(ex);
    }
}
