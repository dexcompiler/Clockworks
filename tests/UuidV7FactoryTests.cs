using Xunit;

namespace Clockworks.Tests;

public sealed class UuidV7FactoryTests
{
    [Fact]
    public void NewGuids_IsMonotonic_AndUnique_WhenTimeDoesNotAdvance()
    {
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, rng, overflowBehavior: CounterOverflowBehavior.IncrementTimestamp);

        Span<Guid> batch = stackalloc Guid[256];
        factory.NewGuids(batch);

        for (var i = 1; i < batch.Length; i++)
        {
            Assert.True(batch[i - 1] < batch[i]);
        }

        var set = new HashSet<Guid>();
        foreach (var g in batch)
            Assert.True(set.Add(g));

        // Time does not need to advance.
        Assert.Equal(1_700_000_000_000, time.GetUtcNow().ToUnixTimeMilliseconds());
    }

    [Fact]
    public void NewGuids_RemainsMonotonic_AcrossCounterOverflow_WhenIncrementTimestampEnabled()
    {
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, rng, overflowBehavior: CounterOverflowBehavior.IncrementTimestamp);

        // Large enough to cross the 12-bit counter range regardless of random start.
        var arr = new Guid[5000];
        factory.NewGuids(arr);

        for (var i = 1; i < arr.Length; i++)
        {
            Assert.True(arr[i - 1] < arr[i]);
        }
    }

    [Fact]
    public void NewGuid_IsMonotonic_WhenTimeDoesNotAdvance()
    {
        var (factory, time, _) = DeterministicGuidSetup.CreateLockFree(seed: 123, startTimeUnixMs: 1_700_000_000_000);

        var g1 = factory.NewGuid();
        var g2 = factory.NewGuid();
        var g3 = factory.NewGuid();

        Assert.True(g1 < g2);
        Assert.True(g2 < g3);

        // Ensure physical time didn't need to move.
        Assert.Equal(1_700_000_000_000, time.GetUtcNow().ToUnixTimeMilliseconds());
    }

    [Fact]
    public void NewGuid_RemainsMonotonic_WhenWallTimeMovesBackwards()
    {
        var (factory, time, _) = DeterministicGuidSetup.CreateLockFree(seed: 123, startTimeUnixMs: 1_700_000_000_000);

        var g1 = factory.NewGuid();

        // Move wall time backwards without advancing scheduler time.
        time.SetUnixMs(1_699_999_000_000);

        var g2 = factory.NewGuid();

        Assert.True(g1 < g2);
    }

    [Fact]
    public void OverflowBehavior_ThrowException_ThrowsWhenCounterOverflows()
    {
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, rng, overflowBehavior: CounterOverflowBehavior.ThrowException);

        // Force the generator into a known millisecond boundary.
        // Advancing time causes the counter to be reset (to a random start), so we can't assume 0.
        // Instead, take the first allocation's timestamp and then keep allocating until overflow occurs.
        // This asserts the behavior (that overflow throws) without tying the test to the counter start value.
        var (_, ts) = factory.NewGuidWithTimestamp();

        while (true)
        {
            try
            {
                var (_, nextTs) = factory.NewGuidWithTimestamp();
                Assert.Equal(ts, nextTs);
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }
    }

    [Fact]
    public void OverflowBehavior_IncrementTimestamp_StaysMonotonic_EvenWhenTimeDoesNotAdvance()
    {
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, rng, overflowBehavior: CounterOverflowBehavior.IncrementTimestamp);

        Guid last = default;
        for (int i = 0; i < 5000; i++)
        {
            var g = factory.NewGuid();
            if (i > 0)
            {
                Assert.True(last < g);
            }
            last = g;
        }
    }

    [Fact]
    public void OverflowBehavior_Auto_UsesIncrementTimestamp_ForSimulatedTimeProvider()
    {
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, rng, overflowBehavior: CounterOverflowBehavior.Auto);

        Guid last = default;
        for (int i = 0; i < 5000; i++)
        {
            var g = factory.NewGuid();
            if (i > 0)
            {
                Assert.True(last < g);
            }
            last = g;
        }
    }
}
