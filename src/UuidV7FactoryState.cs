using System.Buffers.Binary;

namespace Clockworks;

/// <summary>
/// Serializable UUIDv7 factory frontier for restart-aware monotonicity.
/// </summary>
/// <remarks>
/// The state represents the last logical <c>(timestamp, counter)</c> frontier observed by a <see cref="UuidV7Factory"/>.
/// Persisting and restoring this value lets a later factory avoid allocating below that frontier. It does not coordinate
/// multiple live factories; use node partitioning, external coordination, or storage uniqueness constraints when
/// multiple writers share a namespace.
/// </remarks>
public readonly record struct UuidV7FactoryState
{
    /// <summary>
    /// Number of bytes in the canonical big-endian state encoding.
    /// </summary>
    public const int EncodedLength = 8;

    /// <summary>
    /// Maximum timestamp representable by UUIDv7's 48-bit Unix millisecond field.
    /// </summary>
    public const long MaxTimestampMs = 0xFFFF_FFFF_FFFFL;

    /// <summary>
    /// Maximum 12-bit UUIDv7 monotonic counter value.
    /// </summary>
    public const ushort MaxCounter = 0x0FFF;

    /// <summary>
    /// Creates a UUIDv7 factory state snapshot.
    /// </summary>
    /// <param name="timestampMs">Logical Unix timestamp in milliseconds.</param>
    /// <param name="counter">12-bit monotonic counter at <paramref name="timestampMs"/>.</param>
    public UuidV7FactoryState(long timestampMs, ushort counter)
    {
        if (timestampMs is < 0 or > MaxTimestampMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestampMs),
                timestampMs,
                $"Timestamp must be between 0 and {MaxTimestampMs}.");
        }

        if (counter > MaxCounter)
        {
            throw new ArgumentOutOfRangeException(
                nameof(counter),
                counter,
                $"Counter must be less than or equal to {MaxCounter}.");
        }

        TimestampMs = timestampMs;
        Counter = counter;
    }

    /// <summary>
    /// Logical Unix timestamp in milliseconds.
    /// </summary>
    public long TimestampMs { get; }

    /// <summary>
    /// 12-bit monotonic counter at <see cref="TimestampMs"/>.
    /// </summary>
    public ushort Counter { get; }

    /// <summary>
    /// Serializes the state to a compact canonical big-endian byte array.
    /// </summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[EncodedLength];
        WriteTo(bytes);
        return bytes;
    }

    /// <summary>
    /// Writes the state to a compact canonical big-endian encoding.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
            throw new ArgumentException($"Destination must be at least {EncodedLength} bytes.", nameof(destination));

        destination[0] = (byte)(TimestampMs >> 40);
        destination[1] = (byte)(TimestampMs >> 32);
        destination[2] = (byte)(TimestampMs >> 24);
        destination[3] = (byte)(TimestampMs >> 16);
        destination[4] = (byte)(TimestampMs >> 8);
        destination[5] = (byte)TimestampMs;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(6, 2), Counter);
    }

    /// <summary>
    /// Reads state from the compact canonical big-endian encoding produced by <see cref="WriteTo"/>.
    /// </summary>
    public static UuidV7FactoryState ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < EncodedLength)
            throw new ArgumentException($"Source must be at least {EncodedLength} bytes.", nameof(source));

        var timestampMs = ((long)source[0] << 40) |
                          ((long)source[1] << 32) |
                          ((long)source[2] << 24) |
                          ((long)source[3] << 16) |
                          ((long)source[4] << 8) |
                          source[5];
        var counter = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(6, 2));
        return new UuidV7FactoryState(timestampMs, counter);
    }
}
