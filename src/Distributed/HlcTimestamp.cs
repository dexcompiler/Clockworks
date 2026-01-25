namespace Clockworks.Distributed;

/// <summary>
/// Represents a Hybrid Logical Clock timestamp.
/// Combines wall-clock time with a logical counter for causality.
/// </summary>
/// <remarks>
/// Mathematical foundation (Kulkarni et al., 2014):
/// 
/// HLC maintains the invariant: l.j ≥ pt.j for all events j
/// where l.j is the logical time and pt.j is the physical time.
/// 
/// For any two events e and f: e → f ⟹ l.e &lt; l.f (causality preservation)
/// 
/// The counter c provides ordering within the same logical millisecond,
/// giving us O(1) comparison with bounded drift from physical time.
/// </remarks>
public readonly record struct HlcTimestamp : IComparable<HlcTimestamp>, IComparable
{
    /// <summary>
    /// Logical wall time in milliseconds since Unix epoch.
    /// May drift ahead of physical time to maintain causality.
    /// </summary>
    public long WallTimeMs { get; init; }

    /// <summary>
    /// Logical counter within the same wall time.
    /// Resets when wall time advances.
    /// </summary>
    public ushort Counter { get; init; }

    /// <summary>
    /// Node identifier for distributed scenarios.
    /// Used as tiebreaker for simultaneous events.
    /// </summary>
    public ushort NodeId { get; init; }

    /// <summary>
    /// Initializes a new HLC timestamp.
    /// </summary>
    /// <param name="wallTimeMs">Logical wall time in milliseconds since Unix epoch.</param>
    /// <param name="counter">Logical counter for ordering within the same millisecond.</param>
    /// <param name="nodeId">Node identifier used as a tiebreaker.</param>
    public HlcTimestamp(long wallTimeMs, ushort counter = 0, ushort nodeId = 0)
    {
        WallTimeMs = wallTimeMs;
        Counter = counter;
        NodeId = nodeId;
    }

    /// <summary>
    /// Packs the timestamp into a 64-bit value for efficient storage/transmission.
    /// Layout: [48 bits wall time][12 bits counter][4 bits node (truncated)]
    /// </summary>
    public long ToPackedInt64()
    {
        // 48 bits for timestamp (good until year 10889)
        // 12 bits for counter (0-4095)
        // 4 bits for node ID (0-15, use for small clusters)
        return (WallTimeMs << 16)
            | ((long)((ushort)(Counter & 0x0FFF)) << 4)
            | (long)(NodeId & 0x000F);
    }

    /// <summary>
    /// Unpacks a 64-bit packed representation produced by <see cref="ToPackedInt64"/>.
    /// </summary>
    public static HlcTimestamp FromPackedInt64(long packed)
    {
        return new HlcTimestamp(
            wallTimeMs: packed >> 16,
            counter: (ushort)((packed >> 4) & 0xFFF),
            nodeId: (ushort)(packed & 0xF)
        );
    }

    /// <summary>
    /// Full 80-bit encoding preserving all state.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < 10)
            throw new ArgumentException("Destination must be at least 10 bytes", nameof(destination));

        // Big-endian for lexicographic sorting
        destination[0] = (byte)(WallTimeMs >> 40);
        destination[1] = (byte)(WallTimeMs >> 32);
        destination[2] = (byte)(WallTimeMs >> 24);
        destination[3] = (byte)(WallTimeMs >> 16);
        destination[4] = (byte)(WallTimeMs >> 8);
        destination[5] = (byte)WallTimeMs;
        destination[6] = (byte)(Counter >> 8);
        destination[7] = (byte)Counter; 
        destination[8] = (byte)(NodeId >> 8);
        destination[9] = (byte)NodeId;
    }

    /// <summary>
    /// Reads a timestamp from a 10-byte big-endian encoding written by <see cref="WriteTo"/>.
    /// </summary>
    public static HlcTimestamp ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < 10)
            throw new ArgumentException("Source must be at least 10 bytes", nameof(source));

        long wallTime = ((long)source[0] << 40) | ((long)source[1] << 32) |
                        ((long)source[2] << 24) | ((long)source[3] << 16) |
                        ((long)source[4] << 8) | source[5];
        ushort counter = (ushort)((source[6] << 8) | source[7]);
        ushort nodeId = (ushort)((source[8] << 8) | source[9]);

        return new HlcTimestamp(wallTime, counter, nodeId);
    }

    /// <summary>
    /// Compares this timestamp to another timestamp using wall time, then counter, then node id.
    /// </summary>
    public int CompareTo(HlcTimestamp other)
    {
        var cmp = WallTimeMs.CompareTo(other.WallTimeMs);
        if (cmp != 0) return cmp;

        cmp = Counter.CompareTo(other.Counter);
        if (cmp != 0) return cmp;

        return NodeId.CompareTo(other.NodeId);
    }

    /// <summary>
    /// Compares this timestamp to another object.
    /// </summary>
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is HlcTimestamp other) return CompareTo(other);
        throw new ArgumentException("Object must be of type HlcTimestamp", nameof(obj));
    }

    /// <summary>
    /// Determines whether <paramref name="left"/> is earlier than <paramref name="right"/>.
    /// </summary>
    public static bool operator <(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether <paramref name="left"/> is later than <paramref name="right"/>.
    /// </summary>
    public static bool operator >(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether <paramref name="left"/> is earlier than or equal to <paramref name="right"/>.
    /// </summary>
    public static bool operator <=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether <paramref name="left"/> is later than or equal to <paramref name="right"/>.
    /// </summary>
    public static bool operator >=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Returns the string format <c>"walltime.counter@node"</c>.
    /// </summary>
    public override string ToString() => $"{WallTimeMs:D13}.{Counter:D4}@{NodeId}";

    /// <summary>
    /// Parse from string format "walltime.counter@node"
    /// </summary>
    public static HlcTimestamp Parse(string s)
    {
        var atIndex = s.LastIndexOf('@');
        var dotIndex = s.LastIndexOf('.', atIndex > 0 ? atIndex : s.Length - 1);

        return new HlcTimestamp(
            wallTimeMs: long.Parse(s.AsSpan(0, dotIndex)),
            counter: ushort.Parse(s.AsSpan(dotIndex + 1, atIndex - dotIndex - 1)),
            nodeId: ushort.Parse(s.AsSpan(atIndex + 1))
        );
    }
}
