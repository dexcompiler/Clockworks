namespace Clockworks.Distributed;

/// <summary>
/// Message header for propagating HLC timestamps across service boundaries.
/// Can be serialized to/from various wire formats.
/// </summary>
public readonly record struct HlcMessageHeader
{
    /// <summary>
    /// Standard header name for HTTP/gRPC.
    /// </summary>
    public const string HeaderName = "X-HLC-Timestamp";

    /// <summary>
    /// The HLC timestamp to propagate.
    /// </summary>
    public HlcTimestamp Timestamp { get; init; }

    /// <summary>
    /// Optional correlation identifier.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// Optional causation identifier.
    /// </summary>
    public Guid? CausationId { get; init; }

    /// <summary>
    /// Creates a new message header instance.
    /// </summary>
    public HlcMessageHeader(HlcTimestamp timestamp, Guid? correlationId = null, Guid? causationId = null)
    {
        Timestamp = timestamp;
        CorrelationId = correlationId;
        CausationId = causationId;
    }

    /// <summary>
    /// Serialize to a compact string for HTTP headers.
    /// Format: "timestamp.counter@node[;correlation;causation]"
    /// </summary>
    public override string ToString()
    {
        var timestamp = Timestamp.ToString();
        if (!CorrelationId.HasValue)
            return timestamp;

        var correlation = CorrelationId.GetValueOrDefault();
        if (!CausationId.HasValue)
        {
            return string.Create(timestamp.Length + 33, (timestamp, correlation), static (span, state) =>
            {
                state.timestamp.AsSpan().CopyTo(span);
                span[state.timestamp.Length] = ';';
                state.correlation.TryFormat(span[(state.timestamp.Length + 1)..], out _, "N");
            });
        }

        var causation = CausationId.GetValueOrDefault();
        return string.Create(timestamp.Length + 66, (timestamp, correlation, causation), static (span, state) =>
        {
            state.timestamp.AsSpan().CopyTo(span);
            var offset = state.timestamp.Length;
            span[offset++] = ';';
            state.correlation.TryFormat(span[offset..], out _, "N");
            offset += 32;
            span[offset++] = ';';
            state.causation.TryFormat(span[offset..], out _, "N");
        });
    }

    /// <summary>
    /// Parse from header string.
    /// </summary>
    public static HlcMessageHeader Parse(string value)
    {
        var span = value.AsSpan();
        var firstSep = span.IndexOf(';');

        if (firstSep < 0)
            return new HlcMessageHeader(HlcTimestamp.Parse(value));

        var timestamp = HlcTimestamp.Parse(span[..firstSep]);
        span = span[(firstSep + 1)..];

        var secondSep = span.IndexOf(';');
        if (secondSep < 0)
            return new HlcMessageHeader(timestamp, Guid.Parse(span));

        var correlation = Guid.Parse(span[..secondSep]);
        var causation = Guid.Parse(span[(secondSep + 1)..]);
        return new HlcMessageHeader(timestamp, correlation, causation);
    }

    /// <summary>
    /// Try to parse from header string.
    /// </summary>
    public static bool TryParse(string? value, out HlcMessageHeader header)
    {
        header = default;
        if (string.IsNullOrEmpty(value)) return false;
        
        try
        {
            header = Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
