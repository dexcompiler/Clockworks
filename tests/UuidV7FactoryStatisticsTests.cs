using Clockworks.Abstractions;
using Clockworks.Instrumentation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clockworks.Tests;

public sealed class UuidV7FactoryStatisticsTests
{
    [Fact]
    public void Constructor_LeavesStatisticsDisabled_WhenStatisticsAreNotSupplied()
    {
        using var factory = new UuidV7Factory(SimulatedTimeProvider.FromUnixMs(1_700_000_000_000), null);

        Assert.Null(factory.Statistics);
    }

    [Fact]
    public void NewGuid_RecordsGeneratedCount_WhenStatisticsAreEnabled()
    {
        var statistics = new UuidV7FactoryStatistics();
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 123);
        using var factory = new UuidV7Factory(time, rng, CounterOverflowBehavior.SpinWait, statistics);

        statistics.Reset();

        _ = factory.NewGuid();
        _ = factory.NewGuidWithTimestamp();
        Span<Guid> batch = stackalloc Guid[7];
        factory.NewGuids(batch);

        Assert.Same(statistics, factory.Statistics);
        Assert.Equal(9, statistics.GeneratedCount);
        Assert.Equal(9, statistics.Snapshot().GeneratedCount);
    }

    [Fact]
    public void NewGuid_RecordsClockRollbackAndLogicalDrift_WhenPhysicalTimeMovesBackwards()
    {
        const long startMs = 1_700_000_000_000;
        var statistics = new UuidV7FactoryStatistics();
        var time = SimulatedTimeProvider.FromUnixMs(startMs);
        using var rng = new DeterministicRandomNumberGenerator(seed: 123);
        using var factory = new UuidV7Factory(time, rng, CounterOverflowBehavior.SpinWait, statistics);

        statistics.Reset();

        _ = factory.NewGuid();
        time.SetUnixMs(startMs - 25);
        _ = factory.NewGuid();

        var snapshot = statistics.Snapshot();
        Assert.Equal(2, snapshot.GeneratedCount);
        Assert.Equal(1, snapshot.ClockRollbackCount);
        Assert.Equal(1, snapshot.LogicalTimestampAdvanceCount);
        Assert.Equal(25, snapshot.MaxLogicalDriftMs);
    }

    [Fact]
    public void NewGuid_RecordsCounterOverflowAndLogicalAdvance_WhenOverflowIncrementsTimestamp()
    {
        var statistics = new UuidV7FactoryStatistics();
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(
            time,
            rng,
            CounterOverflowBehavior.IncrementTimestamp,
            statistics);

        statistics.Reset();

        for (var i = 0; i < 5000; i++)
            _ = factory.NewGuid();

        var snapshot = statistics.Snapshot();
        Assert.Equal(5000, snapshot.GeneratedCount);
        Assert.True(snapshot.CounterOverflowCount >= 1);
        Assert.True(snapshot.LogicalTimestampAdvanceCount >= 1);
        Assert.True(snapshot.MaxLogicalDriftMs >= 1);
    }

    [Fact]
    public void NewGuids_RecordsRandomBufferRefills_WhenBatchConsumesThreadLocalBuffer()
    {
        var statistics = new UuidV7FactoryStatistics();
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 123);
        using var factory = new UuidV7Factory(time, rng, CounterOverflowBehavior.SpinWait, statistics);
        var batch = new Guid[64];

        statistics.Reset();

        factory.NewGuids(batch);

        var snapshot = statistics.Snapshot();
        Assert.Equal(batch.Length, snapshot.GeneratedCount);
        Assert.True(snapshot.RandomBufferRefillCount >= 1);
    }

    [Fact]
    public async Task NewGuid_RecordsSpinWait_WhenOverflowWaitsForNextMillisecond()
    {
        var statistics = new UuidV7FactoryStatistics();
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(
            time,
            rng,
            CounterOverflowBehavior.SpinWait,
            statistics);

        while (factory.NewGuid().GetCounter() != 0x0FFF)
        {
        }

        statistics.Reset();

        var pending = Task.Run(factory.NewGuid);

        var observedSpin = SpinWait.SpinUntil(
            () => statistics.CounterOverflowCount > 0 && statistics.SpinWaitCount > 0,
            TimeSpan.FromSeconds(2));

        try
        {
            Assert.True(observedSpin);

            time.AdvanceMs(1);

            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (!pending.IsCompleted)
            {
                time.AdvanceMs(1);
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
            }

            pending.Dispose();
        }

        var snapshot = statistics.Snapshot();
        Assert.Equal(1, snapshot.GeneratedCount);
        Assert.Equal(1, snapshot.CounterOverflowCount);
        Assert.Equal(1, snapshot.SpinWaitCount);
    }

    [Fact]
    public async Task NewGuid_RecordsCasRetries_WhenConcurrentAllocationsRace()
    {
        const int participantCount = 8;
        var statistics = new UuidV7FactoryStatistics();
        var time = new BarrierTimeProvider(1_700_000_000_000);
        using var factory = new UuidV7Factory(
            time,
            rng: null,
            CounterOverflowBehavior.SpinWait,
            statistics);

        statistics.Reset();
        time.Arm(participantCount);

        var tasks = Enumerable.Range(0, participantCount)
            .Select(_ => Task.Run(factory.NewGuid))
            .ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = statistics.Snapshot();
        Assert.Equal(participantCount, snapshot.GeneratedCount);
        Assert.True(snapshot.CasRetryCount >= participantCount - 1);
        Assert.Equal(participantCount, tasks.Select(static t => t.Result).Distinct().Count());
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        var statistics = new UuidV7FactoryStatistics();
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);
        using var factory = new UuidV7Factory(time, rng, CounterOverflowBehavior.SpinWait, statistics);

        _ = factory.NewGuid();
        statistics.Reset();

        Assert.Equal(default, statistics.Snapshot());
    }

    [Fact]
    public void AddLockFreeGuidFactory_RegistersSharedStatistics()
    {
        var services = new ServiceCollection();
        var statistics = new UuidV7FactoryStatistics();
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);

        services.AddLockFreeGuidFactory(time, statistics, rng);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IUuidV7Factory>();

        _ = factory.NewGuid();

        Assert.Same(statistics, provider.GetRequiredService<UuidV7FactoryStatistics>());
        Assert.Equal(1, statistics.GeneratedCount);
    }

    private sealed class BarrierTimeProvider(long unixMs) : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        private Barrier? _barrier;
        private int _remainingParticipants;

        public void Arm(int participantCount)
        {
            _remainingParticipants = participantCount;
            _barrier = new Barrier(participantCount);
        }

        public override DateTimeOffset GetUtcNow()
        {
            var barrier = Volatile.Read(ref _barrier);
            if (barrier is not null && Interlocked.Decrement(ref _remainingParticipants) >= 0)
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Timed out while coordinating UUIDv7 CAS contention test.");
            }

            return _utcNow;
        }
    }
}
