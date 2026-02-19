using Xunit;
using Clockworks.Distributed;

namespace Clockworks.Tests;

public sealed class HlcTimestampTests
{
    [Fact]
    public void WriteTo_ReadFrom_RoundTrips()
    {
        var ts = new HlcTimestamp(wallTimeMs: 1_700_000_000_123, counter: 42, nodeId: 7);

        Span<byte> bytes = stackalloc byte[10];
        ts.WriteTo(bytes);

        var parsed = HlcTimestamp.ReadFrom(bytes);

        Assert.Equal(ts, parsed);
    }

    [Fact]
    public void ToPackedInt64_FromPackedInt64_RoundTrips_WithNodeTruncation()
    {
        var ts = new HlcTimestamp(wallTimeMs: 1_700_000_000_123, counter: 0x0ABC, nodeId: 0x00F7);

        var packed = ts.ToPackedInt64();
        var parsed = HlcTimestamp.FromPackedInt64(packed);

        Assert.Equal(ts.WallTimeMs, parsed.WallTimeMs);
        Assert.Equal((ushort)(ts.Counter & 0x0FFF), parsed.Counter);
        Assert.Equal((ushort)(ts.NodeId & 0x000F), parsed.NodeId);
    }

    [Fact]
    public void FromPackedInt64_DoesNotSignExtendWallTime_WhenPackedHighBitSet()
    {
        // Pick a 48-bit wall time where bit 47 is set; after packing (<< 16) this becomes bit 63.
        var wall48 = (1L << 47) + 123;
        var ts = new HlcTimestamp(wallTimeMs: wall48, counter: 1, nodeId: 2);

        var packed = ts.ToPackedInt64();
        Assert.True(packed < 0, "Packed representation should have sign bit set for this case.");

        var decoded = HlcTimestamp.FromPackedInt64(packed);
        Assert.Equal(wall48, decoded.WallTimeMs);
        Assert.Equal((ushort)1, decoded.Counter);
        Assert.Equal((ushort)2, decoded.NodeId);
    }
}
