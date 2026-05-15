using Clockworks.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clockworks.Tests;

public sealed class UuidV7NodePartitionTests
{
    [Fact]
    public void Constructor_ValidatesBitWidthAndNodeId()
    {
        Assert.Equal(1023, UuidV7NodePartition.GetMaxNodeId(10));
        Assert.Equal(65535, UuidV7NodePartition.GetMaxNodeId(16));

        Assert.Throws<ArgumentOutOfRangeException>(() => new UuidV7NodePartition(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UuidV7NodePartition(0, 17));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UuidV7NodePartition(4, 2));
    }

    [Fact]
    public void NewGuid_EmbedsConfiguredNodePartition()
    {
        var partition = new UuidV7NodePartition(nodeId: 42, nodeIdBitWidth: 10);
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 123);
        using var factory = new UuidV7Factory(time, partition, rng);

        var guid = factory.NewGuid();

        Assert.Equal(partition, factory.NodePartition);
        Assert.True(guid.IsVersion7());
        Assert.Equal((ushort?)42, guid.GetNodePartitionId(10));
        Assert.Equal(52, partition.RemainingRandomTailBits);
    }

    [Fact]
    public void NewGuid_SeparatesNodes_WhenTimeCounterAndRandomStreamMatch()
    {
        const long startMs = 1_700_000_000_000;
        const int count = 64;
        var leftPartition = new UuidV7NodePartition(nodeId: 17, nodeIdBitWidth: 10);
        var rightPartition = new UuidV7NodePartition(nodeId: 273, nodeIdBitWidth: 10);
        using var leftRng = new DeterministicRandomNumberGenerator(seed: 42);
        using var rightRng = new DeterministicRandomNumberGenerator(seed: 42);
        using var left = new UuidV7Factory(SimulatedTimeProvider.FromUnixMs(startMs), leftPartition, leftRng);
        using var right = new UuidV7Factory(SimulatedTimeProvider.FromUnixMs(startMs), rightPartition, rightRng);

        for (var i = 0; i < count; i++)
        {
            var leftId = left.NewGuid();
            var rightId = right.NewGuid();

            Assert.NotEqual(leftId, rightId);
            Assert.Equal(leftId.GetTimestampMs(), rightId.GetTimestampMs());
            Assert.Equal(leftId.GetCounter(), rightId.GetCounter());
            Assert.Equal((ushort?)17, leftId.GetNodePartitionId(10));
            Assert.Equal((ushort?)273, rightId.GetNodePartitionId(10));
        }
    }

    [Fact]
    public void NewGuids_EmbedsNodePartitionAcrossBatch()
    {
        var partition = new UuidV7NodePartition(nodeId: 0xBEEF, nodeIdBitWidth: 16);
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 123);
        using var factory = new UuidV7Factory(time, partition, rng);
        var ids = new Guid[128];

        factory.NewGuids(ids);

        Assert.All(ids, id => Assert.Equal((ushort?)0xBEEF, id.GetNodePartitionId(16)));
    }

    [Fact]
    public void GetNodePartitionId_ReturnsNull_WhenGuidIsNotUuidV7()
    {
        Assert.Null(Guid.NewGuid().GetNodePartitionId(10));
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)17)]
    public void GetNodePartitionId_ReturnsNull_WhenBitWidthInvalid(byte nodeIdBitWidth)
    {
        var partition = new UuidV7NodePartition(nodeId: 42, nodeIdBitWidth: 10);
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 123);
        using var factory = new UuidV7Factory(time, partition, rng);

        var guid = factory.NewGuid();

        Assert.Null(guid.GetNodePartitionId(nodeIdBitWidth));
    }

    [Fact]
    public void AddNodePartitionedGuidFactory_SystemTime_RegistersSingletonFactory()
    {
        var services = new ServiceCollection();
        var partition = new UuidV7NodePartition(nodeId: 7, nodeIdBitWidth: 10);

        services.AddNodePartitionedGuidFactory(partition);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IUuidV7Factory>();
        var concrete = provider.GetRequiredService<UuidV7Factory>();
        var id = factory.NewGuid();

        Assert.Same(factory, concrete);
        Assert.Equal(partition, concrete.NodePartition);
        Assert.Equal((ushort?)7, id.GetNodePartitionId(10));
    }

    [Fact]
    public void AddNodePartitionedGuidFactory_RegistersSingletonFactory()
    {
        var services = new ServiceCollection();
        var partition = new UuidV7NodePartition(nodeId: 7, nodeIdBitWidth: 10);
        var time = SimulatedTimeProvider.FromUnixMs(1_700_000_000_000);
        using var rng = new DeterministicRandomNumberGenerator(seed: 1);

        services.AddNodePartitionedGuidFactory(time, partition, rng);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IUuidV7Factory>();
        var concrete = provider.GetRequiredService<UuidV7Factory>();
        var id = factory.NewGuid();

        Assert.Same(factory, concrete);
        Assert.Equal(partition, concrete.NodePartition);
        Assert.Equal((ushort?)7, id.GetNodePartitionId(10));
    }
}
