using Xunit;

namespace Clockworks.Tests;

public sealed class UuidV7FactoryStateTests
{
    [Fact]
    public void Constructor_ValidatesTimestampAndCounter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UuidV7FactoryState(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UuidV7FactoryState(UuidV7FactoryState.MaxTimestampMs + 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UuidV7FactoryState(1, UuidV7FactoryState.MaxCounter + 1));
    }

    [Fact]
    public void WriteToAndReadFrom_RoundTripState()
    {
        var state = new UuidV7FactoryState(1_700_000_000_000, 1234);
        Span<byte> bytes = stackalloc byte[UuidV7FactoryState.EncodedLength];

        state.WriteTo(bytes);

        Assert.Equal(state, UuidV7FactoryState.ReadFrom(bytes));
        Assert.Equal(state, UuidV7FactoryState.ReadFrom(state.ToBytes()));
    }

    [Fact]
    public void NewGuid_RestartsAboveState_WhenWallClockMovesBackwards()
    {
        const long startMs = 1_700_000_000_000;
        var firstTime = SimulatedTimeProvider.FromUnixMs(startMs);
        using var firstRng = new DeterministicRandomNumberGenerator(seed: 1);
        using var first = new UuidV7Factory(firstTime, firstRng);

        var beforeRestart = first.NewGuid();
        var state = first.GetState();

        var restartedTime = SimulatedTimeProvider.FromUnixMs(startMs - 10_000);
        using var restartedRng = new DeterministicRandomNumberGenerator(seed: 2);
        using var restarted = new UuidV7Factory(restartedTime, state, restartedRng);

        var afterRestart = restarted.NewGuid();

        Assert.True(afterRestart > beforeRestart);
        Assert.Equal(state.TimestampMs, afterRestart.GetTimestampMs());
        Assert.Equal((ushort)(state.Counter + 1), afterRestart.GetCounter());
    }

    [Fact]
    public void Constructor_UsesPhysicalTime_WhenRestoredStateIsLowerThanPhysicalTime()
    {
        const long physicalMs = 1_700_000_000_000;
        var restored = new UuidV7FactoryState(physicalMs - 1000, 4095);
        var time = SimulatedTimeProvider.FromUnixMs(physicalMs);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, restored, rng);

        var id = factory.NewGuid();

        Assert.Equal(physicalMs, id.GetTimestampMs());
        Assert.True(id.GetCounter() <= UuidV7FactoryState.MaxCounter);
    }

    [Fact]
    public void Constructor_ContinuesFromRestoredCounter_WhenRestoredStateEqualsPhysicalTime()
    {
        const long physicalMs = 1_700_000_000_000;
        var restored = new UuidV7FactoryState(physicalMs, 1000);
        var time = SimulatedTimeProvider.FromUnixMs(physicalMs);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, restored, rng);

        var id = factory.NewGuid();

        Assert.Equal(physicalMs, id.GetTimestampMs());
        Assert.Equal((ushort?)1001, id.GetCounter());
    }

    [Fact]
    public void Constructor_ContinuesFromRestoredTime_WhenRestoredStateIsGreaterThanPhysicalTime()
    {
        const long physicalMs = 1_700_000_000_000;
        var restored = new UuidV7FactoryState(physicalMs + 250, 2000);
        var time = SimulatedTimeProvider.FromUnixMs(physicalMs);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, restored, rng);

        var id = factory.NewGuid();

        Assert.Equal(restored.TimestampMs, id.GetTimestampMs());
        Assert.Equal((ushort?)2001, id.GetCounter());
    }

    [Fact]
    public void RestoreState_DoesNotMoveFactoryBackwards()
    {
        const long startMs = 1_700_000_000_000;
        var time = SimulatedTimeProvider.FromUnixMs(startMs);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, rng);

        _ = factory.NewGuid();
        var advanced = factory.GetState();

        factory.RestoreState(new UuidV7FactoryState(startMs - 100, 4095));
        var afterRestore = factory.GetState();

        Assert.Equal(advanced, afterRestore);
    }

    [Fact]
    public void RestoreState_AdvancesFactoryToFutureFrontier()
    {
        const long startMs = 1_700_000_000_000;
        var time = SimulatedTimeProvider.FromUnixMs(startMs);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, rng);
        var restored = new UuidV7FactoryState(startMs + 10, 100);

        factory.RestoreState(restored);
        var id = factory.NewGuid();

        Assert.Equal(restored.TimestampMs, id.GetTimestampMs());
        Assert.Equal((ushort?)101, id.GetCounter());
    }

    [Fact]
    public void RestoreState_HandlesPackedTimestampSignBitBoundary()
    {
        const long lowerTimestampMs = (1L << 47) - 1;
        const long higherTimestampMs = 1L << 47;
        var time = SimulatedTimeProvider.FromUnixMs(lowerTimestampMs);
        var initial = new UuidV7FactoryState(lowerTimestampMs, UuidV7FactoryState.MaxCounter);
        var restored = new UuidV7FactoryState(higherTimestampMs, 100);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(
            time,
            initial,
            rng,
            overflowBehavior: CounterOverflowBehavior.IncrementTimestamp);

        factory.RestoreState(restored);
        var state = factory.GetState();
        var id = factory.NewGuid();

        Assert.Equal(restored, state);
        Assert.Equal(higherTimestampMs, id.GetTimestampMs());
        Assert.Equal((ushort?)101, id.GetCounter());
    }

    [Fact]
    public void Constructor_PreservesNodePartition_WhenRestoredStateIsUsed()
    {
        const long startMs = 1_700_000_000_000;
        var partition = new UuidV7NodePartition(nodeId: 42, nodeIdBitWidth: 10);
        var restored = new UuidV7FactoryState(startMs + 10, 100);
        var time = SimulatedTimeProvider.FromUnixMs(startMs);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, partition, restored, rng);

        var id = factory.NewGuid();

        Assert.Equal(restored.TimestampMs, id.GetTimestampMs());
        Assert.Equal((ushort?)101, id.GetCounter());
        Assert.Equal((ushort?)42, id.GetNodePartitionId(10));
    }

    [Fact]
    public void Constructor_IncrementTimestampHandlesRestoredMaxCounterAheadOfPhysicalTime()
    {
        const long startMs = 1_700_000_000_000;
        var restored = new UuidV7FactoryState(startMs + 10, UuidV7FactoryState.MaxCounter);
        var time = SimulatedTimeProvider.FromUnixMs(startMs);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(
            time,
            restored,
            rng,
            overflowBehavior: CounterOverflowBehavior.IncrementTimestamp);

        var id = factory.NewGuid();

        Assert.Equal(restored.TimestampMs + 1, id.GetTimestampMs());
    }
}
