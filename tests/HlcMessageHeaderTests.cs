using Xunit;
using Clockworks.Distributed;

namespace Clockworks.Tests;

public sealed class HlcMessageHeaderTests
{
    [Fact]
    public void ToString_Parse_RoundTrips_WhenCorrelationAndCausationPresent()
    {
        var header = new HlcMessageHeader(
            timestamp: new HlcTimestamp(1_700_000_000_000, counter: 12, nodeId: 3),
            correlationId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            causationId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        var str = header.ToString();
        var parsed = HlcMessageHeader.Parse(str);

        Assert.Equal(header.Timestamp, parsed.Timestamp);
        Assert.Equal(header.CorrelationId, parsed.CorrelationId);
        Assert.Equal(header.CausationId, parsed.CausationId);
    }

    [Fact]
    public void TryParse_ReturnsFalse_OnNullOrEmpty()
    {
        Assert.False(HlcMessageHeader.TryParse(null, out _));
        Assert.False(HlcMessageHeader.TryParse(string.Empty, out _));
    }

    [Fact]
    public void GoldenString_Parses_AsExpected()
    {
        const string value = "1700000000000.0012@3;aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        var parsed = HlcMessageHeader.Parse(value);

        Assert.Equal(new HlcTimestamp(1_700_000_000_000, counter: 12, nodeId: 3), parsed.Timestamp);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), parsed.CorrelationId);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), parsed.CausationId);
    }

    [Fact]
    public void TryParse_RoundTrips_TimestampOnly()
    {
        const string value = "1700000000000.0012@3";
        Assert.True(HlcMessageHeader.TryParse(value, out var header));
        Assert.Equal(new HlcTimestamp(1_700_000_000_000, counter: 12, nodeId: 3), header.Timestamp);
        Assert.Null(header.CorrelationId);
        Assert.Null(header.CausationId);
    }

    [Fact]
    public void TryParse_RoundTrips_TimestampAndCorrelation()
    {
        const string value = "1700000000000.0012@3;aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert.True(HlcMessageHeader.TryParse(value, out var header));
        Assert.Equal(new HlcTimestamp(1_700_000_000_000, counter: 12, nodeId: 3), header.Timestamp);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), header.CorrelationId);
        Assert.Null(header.CausationId);
    }

    [Fact]
    public void TryParse_ReturnsFalse_OnInvalidGuidAndTimestamp()
    {
        Assert.False(HlcMessageHeader.TryParse("1700000000000.0012@3;not-a-guid", out _));
        Assert.False(HlcMessageHeader.TryParse("not-a-timestamp;aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out _));
    }
}
