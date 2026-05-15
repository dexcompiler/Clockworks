namespace Clockworks;

/// <summary>
/// Defines an opt-in node or shard discriminator embedded into the most-significant bits of UUIDv7 <c>rand_b</c>.
/// </summary>
/// <remarks>
/// The discriminator is intended for fleets that can assign unique node, shard, process, or deployment IDs without a
/// central UUID allocator. It preserves the 48-bit timestamp and 12-bit monotonic counter layout used by
/// <see cref="UuidV7Factory"/>, while trading some random-tail entropy for deterministic namespace partitioning.
/// </remarks>
public readonly record struct UuidV7NodePartition
{
    /// <summary>
    /// Number of effective random bits available in UUIDv7 <c>rand_b</c> after the RFC variant bits.
    /// </summary>
    public const int EffectiveRandomTailBits = 62;

    /// <summary>
    /// Minimum supported node discriminator width.
    /// </summary>
    public const byte MinNodeIdBitWidth = 1;

    /// <summary>
    /// Maximum supported node discriminator width. This leaves at least 46 random bits in <c>rand_b</c>.
    /// </summary>
    public const byte MaxNodeIdBitWidth = 16;

    /// <summary>
    /// Creates a node partition.
    /// </summary>
    /// <param name="nodeId">Node, shard, process, or deployment discriminator value.</param>
    /// <param name="nodeIdBitWidth">Number of most-significant <c>rand_b</c> bits reserved for <paramref name="nodeId"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="nodeIdBitWidth"/> is outside the supported range or
    /// <paramref name="nodeId"/> cannot fit in the configured width.
    /// </exception>
    public UuidV7NodePartition(ushort nodeId, byte nodeIdBitWidth)
    {
        ValidateBitWidth(nodeIdBitWidth);

        var maxNodeId = GetMaxNodeId(nodeIdBitWidth);
        if (nodeId > maxNodeId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeId),
                nodeId,
                $"Node ID must be less than or equal to {maxNodeId} for a {nodeIdBitWidth}-bit partition.");
        }

        NodeId = nodeId;
        NodeIdBitWidth = nodeIdBitWidth;
    }

    /// <summary>
    /// Node, shard, process, or deployment discriminator value.
    /// </summary>
    public ushort NodeId { get; }

    /// <summary>
    /// Number of most-significant <c>rand_b</c> bits reserved for <see cref="NodeId"/>.
    /// </summary>
    public byte NodeIdBitWidth { get; }

    /// <summary>
    /// Number of effective random-tail bits that remain after reserving <see cref="NodeIdBitWidth"/> bits.
    /// </summary>
    public int RemainingRandomTailBits => EffectiveRandomTailBits - NodeIdBitWidth;

    internal bool IsConfigured => NodeIdBitWidth != 0;

    /// <summary>
    /// Gets the largest node ID representable by <paramref name="nodeIdBitWidth"/>.
    /// </summary>
    public static ushort GetMaxNodeId(byte nodeIdBitWidth)
    {
        ValidateBitWidth(nodeIdBitWidth);
        return (ushort)((1 << nodeIdBitWidth) - 1);
    }

    internal static void ValidateBitWidth(byte nodeIdBitWidth)
    {
        if (nodeIdBitWidth is < MinNodeIdBitWidth or > MaxNodeIdBitWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeIdBitWidth),
                nodeIdBitWidth,
                $"Node ID bit width must be between {MinNodeIdBitWidth} and {MaxNodeIdBitWidth}.");
        }
    }

    internal void ApplyTo(Span<byte> uuidBytes)
    {
        uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80);
        WriteNodeId(uuidBytes, NodeId, NodeIdBitWidth);
    }

    internal static ushort ReadNodeId(ReadOnlySpan<byte> uuidBytes, byte nodeIdBitWidth)
    {
        ValidateBitWidth(nodeIdBitWidth);

        if (nodeIdBitWidth <= 6)
        {
            var mask = (1 << nodeIdBitWidth) - 1;
            return (ushort)((uuidBytes[8] >> (6 - nodeIdBitWidth)) & mask);
        }

        var remaining = nodeIdBitWidth - 6;
        var nodeId = (ushort)((uuidBytes[8] & 0x3F) << remaining);

        if (remaining <= 8)
        {
            var mask = (1 << remaining) - 1;
            nodeId |= (ushort)((uuidBytes[9] >> (8 - remaining)) & mask);
            return nodeId;
        }

        var finalBits = remaining - 8;
        var finalMask = (1 << finalBits) - 1;
        nodeId |= (ushort)(uuidBytes[9] << finalBits);
        nodeId |= (ushort)((uuidBytes[10] >> (8 - finalBits)) & finalMask);
        return nodeId;
    }

    private static void WriteNodeId(Span<byte> uuidBytes, ushort nodeId, byte nodeIdBitWidth)
    {
        if (nodeIdBitWidth <= 6)
        {
            var shift = 6 - nodeIdBitWidth;
            var mask = ((1 << nodeIdBitWidth) - 1) << shift;
            var value = nodeId << shift;
            uuidBytes[8] = (byte)((uuidBytes[8] & ~mask) | value);
            return;
        }

        var remaining = nodeIdBitWidth - 6;
        uuidBytes[8] = (byte)((uuidBytes[8] & 0xC0) | ((nodeId >> remaining) & 0x3F));

        var remainingValue = nodeId & ((1 << remaining) - 1);
        if (remaining <= 8)
        {
            var shift = 8 - remaining;
            var mask = ((1 << remaining) - 1) << shift;
            var value = remainingValue << shift;
            uuidBytes[9] = (byte)((uuidBytes[9] & ~mask) | value);
            return;
        }

        var finalBits = remaining - 8;
        uuidBytes[9] = (byte)(remainingValue >> finalBits);

        var finalShift = 8 - finalBits;
        var finalMask = ((1 << finalBits) - 1) << finalShift;
        var finalValue = (remainingValue & ((1 << finalBits) - 1)) << finalShift;
        uuidBytes[10] = (byte)((uuidBytes[10] & ~finalMask) | finalValue);
    }
}
