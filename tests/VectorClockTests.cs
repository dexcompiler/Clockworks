using Xunit;
using Clockworks.Distributed;

namespace Clockworks.Tests;

public sealed class VectorClockTests
{
    [Fact]
    public void EmptyVectorClock_ReturnsZeroForAllNodes()
    {
        var vc = new VectorClock();

        Assert.Equal(0UL, vc.Get(1));
        Assert.Equal(0UL, vc.Get(100));
        Assert.Equal(0UL, vc.Get(ushort.MaxValue));
    }

    [Fact]
    public void Increment_FirstNode_CreatesClockWithCounter1()
    {
        var vc = new VectorClock();
        var vc2 = vc.Increment(1);

        Assert.Equal(1UL, vc2.Get(1));
        Assert.Equal(0UL, vc2.Get(2));
    }

    [Fact]
    public void Increment_ExistingNode_IncrementsCounter()
    {
        var vc = new VectorClock();
        var vc2 = vc.Increment(1).Increment(1).Increment(1);

        Assert.Equal(3UL, vc2.Get(1));
    }

    [Fact]
    public void Increment_MultipleNodes_MaintainsSortedOrder()
    {
        var vc = new VectorClock()
            .Increment(5)
            .Increment(2)
            .Increment(8)
            .Increment(1);

        Assert.Equal(1UL, vc.Get(1));
        Assert.Equal(1UL, vc.Get(2));
        Assert.Equal(1UL, vc.Get(5));
        Assert.Equal(1UL, vc.Get(8));

        // Verify sorted order via string representation
        Assert.Equal("1:1,2:1,5:1,8:1", vc.ToString());
    }

    [Fact]
    public void Compare_IdenticalClocks_ReturnsEqual()
    {
        var vc1 = new VectorClock().Increment(1).Increment(2);
        var vc2 = new VectorClock().Increment(1).Increment(2);

        Assert.Equal(VectorClockOrder.Equal, vc1.Compare(vc2));
        Assert.Equal(VectorClockOrder.Equal, vc2.Compare(vc1));
    }

    [Fact]
    public void Compare_EmptyClocks_ReturnsEqual()
    {
        var vc1 = new VectorClock();
        var vc2 = new VectorClock();

        Assert.Equal(VectorClockOrder.Equal, vc1.Compare(vc2));
    }

    [Fact]
    public void Compare_StrictlyBefore_ReturnsBefore()
    {
        // A[1:1, 2:1] < B[1:2, 2:1]
        var vcA = new VectorClock().Increment(1).Increment(2);
        var vcB = vcA.Increment(1);

        Assert.Equal(VectorClockOrder.Before, vcA.Compare(vcB));
        Assert.Equal(VectorClockOrder.After, vcB.Compare(vcA));
    }

    [Fact]
    public void Compare_DisjointNodes_ReturnsConcurrent()
    {
        // A has only node 1, B has only node 2
        var vcA = new VectorClock().Increment(1);
        var vcB = new VectorClock().Increment(2);

        Assert.Equal(VectorClockOrder.Concurrent, vcA.Compare(vcB));
        Assert.Equal(VectorClockOrder.Concurrent, vcB.Compare(vcA));
    }

    [Fact]
    public void Compare_PartialOverlap_ReturnsConcurrent()
    {
        // A[1:2, 2:1], B[1:1, 2:2] - neither dominates
        var vcA = new VectorClock().Increment(1).Increment(1).Increment(2);
        var vcB = new VectorClock().Increment(1).Increment(2).Increment(2);

        Assert.Equal(VectorClockOrder.Concurrent, vcA.Compare(vcB));
        Assert.Equal(VectorClockOrder.Concurrent, vcB.Compare(vcA));
    }

    [Fact]
    public void HappensBefore_WhenBefore_ReturnsTrue()
    {
        var vcA = new VectorClock().Increment(1);
        var vcB = vcA.Increment(1);

        Assert.True(vcA.HappensBefore(vcB));
        Assert.False(vcB.HappensBefore(vcA));
    }

    [Fact]
    public void HappensAfter_WhenAfter_ReturnsTrue()
    {
        var vcA = new VectorClock().Increment(1);
        var vcB = vcA.Increment(1);

        Assert.True(vcB.HappensAfter(vcA));
        Assert.False(vcA.HappensAfter(vcB));
    }

    [Fact]
    public void IsConcurrentWith_WhenConcurrent_ReturnsTrue()
    {
        var vcA = new VectorClock().Increment(1);
        var vcB = new VectorClock().Increment(2);

        Assert.True(vcA.IsConcurrentWith(vcB));
        Assert.True(vcB.IsConcurrentWith(vcA));
    }

    [Fact]
    public void Merge_TwoEmptyClocks_ReturnsEmpty()
    {
        var vc1 = new VectorClock();
        var vc2 = new VectorClock();
        var merged = vc1.Merge(vc2);

        Assert.Equal(new VectorClock(), merged);
    }

    [Fact]
    public void Merge_WithEmptyClock_ReturnsNonEmpty()
    {
        var vc1 = new VectorClock().Increment(1);
        var vc2 = new VectorClock();
        var merged = vc1.Merge(vc2);

        Assert.Equal(vc1, merged);
    }

    [Fact]
    public void Merge_DisjointNodes_UnionOfBoth()
    {
        var vc1 = new VectorClock().Increment(1).Increment(1);
        var vc2 = new VectorClock().Increment(2).Increment(2).Increment(2);
        var merged = vc1.Merge(vc2);

        Assert.Equal(2UL, merged.Get(1));
        Assert.Equal(3UL, merged.Get(2));
    }

    [Fact]
    public void Merge_OverlappingNodes_TakesMaximum()
    {
        var vc1 = new VectorClock().Increment(1).Increment(1).Increment(2);
        var vc2 = new VectorClock().Increment(1).Increment(2).Increment(2);
        var merged = vc1.Merge(vc2);

        Assert.Equal(2UL, merged.Get(1)); // max(2, 1)
        Assert.Equal(2UL, merged.Get(2)); // max(1, 2)
    }

    [Fact]
    public void Merge_IsCommutative()
    {
        var vc1 = new VectorClock().Increment(1).Increment(2);
        var vc2 = new VectorClock().Increment(2).Increment(3);

        var merged1 = vc1.Merge(vc2);
        var merged2 = vc2.Merge(vc1);

        Assert.Equal(merged1, merged2);
    }

    [Fact]
    public void Merge_IsIdempotent()
    {
        var vc1 = new VectorClock().Increment(1).Increment(2);
        var merged1 = vc1.Merge(vc1);
        var merged2 = merged1.Merge(vc1);

        Assert.Equal(vc1, merged1);
        Assert.Equal(vc1, merged2);
    }

    [Fact]
    public void BinarySerialization_EmptyClock_RoundTrips()
    {
        var vc = new VectorClock();
        Span<byte> buffer = stackalloc byte[vc.GetBinarySize()];
        vc.WriteTo(buffer);

        var restored = VectorClock.ReadFrom(buffer);
        Assert.Equal(vc, restored);
    }

    [Fact]
    public void BinarySerialization_SingleNode_RoundTrips()
    {
        var vc = new VectorClock().Increment(42);
        Span<byte> buffer = stackalloc byte[vc.GetBinarySize()];
        vc.WriteTo(buffer);

        var restored = VectorClock.ReadFrom(buffer);
        Assert.Equal(vc, restored);
        Assert.Equal(1UL, restored.Get(42));
    }

    [Fact]
    public void BinarySerialization_MultipleNodes_RoundTrips()
    {
        var vc = new VectorClock()
            .Increment(1).Increment(1)
            .Increment(2).Increment(2).Increment(2)
            .Increment(5);

        Span<byte> buffer = stackalloc byte[vc.GetBinarySize()];
        vc.WriteTo(buffer);

        var restored = VectorClock.ReadFrom(buffer);
        Assert.Equal(vc, restored);
        Assert.Equal(2UL, restored.Get(1));
        Assert.Equal(3UL, restored.Get(2));
        Assert.Equal(1UL, restored.Get(5));
    }

    [Fact]
    public void BinarySerialization_LargeCounters_RoundTrips()
    {
        var vc = new VectorClock();
        for (var i = 0; i < 100; i++)
            vc = vc.Increment(1);

        Span<byte> buffer = stackalloc byte[vc.GetBinarySize()];
        vc.WriteTo(buffer);

        var restored = VectorClock.ReadFrom(buffer);
        Assert.Equal(100UL, restored.Get(1));
    }

    [Fact]
    public void StringSerialization_EmptyClock_RoundTrips()
    {
        var vc = new VectorClock();
        var str = vc.ToString();
        var parsed = VectorClock.Parse(str);

        Assert.Equal(vc, parsed);
    }

    [Fact]
    public void StringSerialization_SingleNode_RoundTrips()
    {
        var vc = new VectorClock().Increment(42);
        var str = vc.ToString();
        var parsed = VectorClock.Parse(str);

        Assert.Equal(vc, parsed);
        Assert.Equal("42:1", str);
    }

    [Fact]
    public void StringSerialization_MultipleNodes_RoundTrips()
    {
        var vc = new VectorClock()
            .Increment(1).Increment(1)
            .Increment(2).Increment(2).Increment(2)
            .Increment(5);

        var str = vc.ToString();
        var parsed = VectorClock.Parse(str);

        Assert.Equal(vc, parsed);
        Assert.Equal("1:2,2:3,5:1", str);
    }

    [Fact]
    public void StringSerialization_UnsortedInput_ParsesAndSorts()
    {
        var str = "5:1,2:3,1:2";
        var parsed = VectorClock.Parse(str);

        Assert.Equal(2UL, parsed.Get(1));
        Assert.Equal(3UL, parsed.Get(2));
        Assert.Equal(1UL, parsed.Get(5));
        Assert.Equal("1:2,2:3,5:1", parsed.ToString());
    }

    [Fact]
    public void StringSerialization_DuplicateNodes_TakesMaximum()
    {
        var str = "2:1,1:4,2:3,1:2";
        var parsed = VectorClock.Parse(str);

        Assert.Equal(4UL, parsed.Get(1));
        Assert.Equal(3UL, parsed.Get(2));
        Assert.Equal("1:4,2:3", parsed.ToString());
    }

    [Fact]
    public void TryParse_ValidString_ReturnsTrue()
    {
        Assert.True(VectorClock.TryParse("1:2,2:3", out var result));
        Assert.Equal(2UL, result.Get(1));
        Assert.Equal(3UL, result.Get(2));
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsTrue()
    {
        Assert.True(VectorClock.TryParse("", out var result));
        Assert.Equal(new VectorClock(), result);
    }

    [Fact]
    public void TryParse_Null_ReturnsTrue()
    {
        Assert.True(VectorClock.TryParse(null, out var result));
        Assert.Equal(new VectorClock(), result);
    }

    [Fact]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        Assert.False(VectorClock.TryParse("invalid", out _));
        Assert.False(VectorClock.TryParse("1:2:3", out _));
    }

    [Fact]
    public void BinarySerialization_UnsortedInput_Canonicalizes()
    {
        var buffer = BuildBinary(
            (nodeId: 2, counter: 4UL),
            (nodeId: 1, counter: 7UL),
            (nodeId: 2, counter: 5UL));

        var parsed = VectorClock.ReadFrom(buffer);

        Assert.Equal(7UL, parsed.Get(1));
        Assert.Equal(5UL, parsed.Get(2));
        Assert.Equal("1:7,2:5", parsed.ToString());
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var vc1 = new VectorClock().Increment(1).Increment(2);
        var vc2 = new VectorClock().Increment(1).Increment(2);

        Assert.True(vc1.Equals(vc2));
        Assert.True(vc1 == vc2);
        Assert.False(vc1 != vc2);
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var vc1 = new VectorClock().Increment(1);
        var vc2 = new VectorClock().Increment(2);

        Assert.False(vc1.Equals(vc2));
        Assert.False(vc1 == vc2);
        Assert.True(vc1 != vc2);
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHash()
    {
        var vc1 = new VectorClock().Increment(1).Increment(2);
        var vc2 = new VectorClock().Increment(1).Increment(2);

        Assert.Equal(vc1.GetHashCode(), vc2.GetHashCode());
    }

    private static byte[] BuildBinary(params (ushort nodeId, ulong counter)[] entries)
    {
        var buffer = new byte[2 + (entries.Length * 10)];
        buffer[0] = (byte)(entries.Length >> 8);
        buffer[1] = (byte)entries.Length;

        var offset = 2;
        foreach (var entry in entries)
        {
            buffer[offset++] = (byte)(entry.nodeId >> 8);
            buffer[offset++] = (byte)entry.nodeId;

            var counter = entry.counter;
            buffer[offset++] = (byte)(counter >> 56);
            buffer[offset++] = (byte)(counter >> 48);
            buffer[offset++] = (byte)(counter >> 40);
            buffer[offset++] = (byte)(counter >> 32);
            buffer[offset++] = (byte)(counter >> 24);
            buffer[offset++] = (byte)(counter >> 16);
            buffer[offset++] = (byte)(counter >> 8);
            buffer[offset++] = (byte)counter;
        }

        return buffer;
    }
}
