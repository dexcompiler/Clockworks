using Clockworks.Abstractions;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Clockworks;

/// <summary>
/// UUIDv7 generator with complete time control via <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// Implements RFC 9562 UUID version 7 and returns values as <see cref="Guid"/> with:
/// 
/// - Monotonic counter for sub-millisecond ordering
/// - Lock-free synchronization using CAS operations
/// - Configurable overflow behavior
/// - TimeProvider integration for testing/simulation
///
/// <para>
/// <b>UUIDv7 bit layout (RFC 9562):</b>
/// <code>
///  0                   1                   2                   3
///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |                         unix_ts_ms (32 bits)                 |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |          unix_ts_ms (16 bits) |  ver  |       rand_a         |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |var|                       rand_b                             |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |                           rand_b                             |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// </code>
/// We use rand_a (12 bits) as a monotonic counter for ordering within milliseconds.
/// </para>
/// </remarks>
public sealed class UuidV7Factory : IUuidV7Factory, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly RandomNumberGenerator _rng;
    private readonly bool _ownsRng;
    private readonly CounterOverflowBehavior _overflowBehavior;
    private readonly CounterOverflowBehavior _effectiveOverflowBehavior;

    // Packed state: [48 bits timestamp][16 bits counter]
    // Using 64-bit atomic operations for lock-free updates
    private long _packedState;

    // Pre-allocated buffer for random bytes to reduce RNG calls
    // Each GUID needs 8 random bytes; we batch for efficiency
    private readonly ThreadLocal<RandomBuffer> _randomBuffer;

    private const int MaxCounterValue = 0xFFF;       // 12 bits = 4095
    private const int CounterRandomStart = 0x7FF;   // Start in lower half (11 bits max)
    private const long TimestampMask = unchecked((long)0xFFFF_FFFF_FFFF_0000L);
    private const long CounterMask = 0x0000_0000_0000_FFFFL;

    // UUID constants
    private const byte Version7 = 0x70;        // 0111 xxxx
    private const byte VersionMask = 0x0F;
    private const byte VariantRfc4122 = 0x80;  // 10xx xxxx
    private const byte VariantMask = 0x3F;

    /// <summary>
    /// Creates a new UUIDv7 generator.
    /// </summary>
    /// <param name="timeProvider">Time source (use <see cref="TimeProvider.System"/> for production).</param>
    /// <param name="rng">
    /// Random number generator to use for the random portion of the UUID. If <see langword="null"/>, a new
    /// cryptographically-secure RNG is created and owned by this instance.
    /// </param>
    /// <param name="overflowBehavior">Behavior to apply when the per-millisecond counter overflows.</param>
    public UuidV7Factory(
        TimeProvider timeProvider,
        RandomNumberGenerator? rng = null,
        CounterOverflowBehavior overflowBehavior = CounterOverflowBehavior.SpinWait)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _rng = rng ?? RandomNumberGenerator.Create();
        _ownsRng = rng is null;
        _overflowBehavior = overflowBehavior;

        _effectiveOverflowBehavior = overflowBehavior == CounterOverflowBehavior.Auto
            ? (_timeProvider is SimulatedTimeProvider ? CounterOverflowBehavior.IncrementTimestamp : CounterOverflowBehavior.SpinWait)
            : overflowBehavior;

        _randomBuffer = new ThreadLocal<RandomBuffer>(() => new RandomBuffer(_rng), trackAllValues: false);

        // Initialize state with current time and random counter
        var initialTimestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var initialCounter = GetRandomCounterStart();
        _packedState = PackState(initialTimestamp, initialCounter);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Guid NewGuid()
    {
        var (timestampMs, counter) = AllocateTimestampAndCounter();
        return CreateGuidFromState(timestampMs, counter);
    }

    /// <inheritdoc/>
    public (Guid Guid, long TimestampMs) NewGuidWithTimestamp()
    {
        var (timestampMs, counter) = AllocateTimestampAndCounter();
        return (CreateGuidFromState(timestampMs, counter), timestampMs);
    }

    /// <inheritdoc/>
    public void NewGuids(Span<Guid> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = NewGuid();
        }
    }

    /// <summary>
    /// Allocates a unique (timestamp, counter) pair using a CAS loop.
    /// </summary>
    /// <remarks>
    /// Guarantees:
    /// - Monotonically increasing (timestamp, counter) pairs for a given factory instance.
    /// - No two calls return the same pair.
    /// 
    /// Note: if the underlying time provider goes backwards, the generator preserves monotonicity by continuing from the
    /// previously observed timestamp.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (long TimestampMs, ushort Counter) AllocateTimestampAndCounter()
    {
        var spinWait = new SpinWait();

        while (true)
        {
            var currentPacked = Volatile.Read(ref _packedState);
            var (currentTimestamp, currentCounter) = UnpackState(currentPacked);

            var physicalTime = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

            long newTimestamp;
            ushort newCounter;

            if (physicalTime > currentTimestamp)
            {
                // Physical time advanced - reset counter with random start
                newTimestamp = physicalTime;
                newCounter = GetRandomCounterStart();
            }
            else if (physicalTime == currentTimestamp)
            {
                // Same millisecond - increment counter
                if (currentCounter >= MaxCounterValue)
                {
                    // Counter overflow
                    switch (_effectiveOverflowBehavior)
                    {
                        case CounterOverflowBehavior.SpinWait:
                            SpinWaitForNextMillisecond(physicalTime);
                            continue; // Retry with new time

                        case CounterOverflowBehavior.IncrementTimestamp:
                            newTimestamp = currentTimestamp + 1;
                            newCounter = GetRandomCounterStart();
                            break;

                        case CounterOverflowBehavior.ThrowException:
                            throw new InvalidOperationException(
                                $"Counter overflow: generated {MaxCounterValue + 1} UUIDs within millisecond {currentTimestamp}");

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    newTimestamp = currentTimestamp;
                    newCounter = (ushort)(currentCounter + 1);
                }
            }
            else
            {
                // Time went backwards; preserve monotonicity by continuing from the current state.
                if (currentCounter >= MaxCounterValue)
                {
                    newTimestamp = currentTimestamp + 1;
                    newCounter = GetRandomCounterStart();
                }
                else
                {
                    newTimestamp = currentTimestamp;
                    newCounter = (ushort)(currentCounter + 1);
                }
            }

            var newPacked = PackState(newTimestamp, newCounter);

            if (Interlocked.CompareExchange(ref _packedState, newPacked, currentPacked) == currentPacked)
            {
                return (newTimestamp, newCounter);
            }

            spinWait.SpinOnce();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Guid CreateGuidFromState(long timestampMs, ushort counter)
    {
        // Stack allocate the 16-byte buffer
        Span<byte> bytes = stackalloc byte[16];

        // Bytes 0-5: 48-bit timestamp (big-endian, network byte order)
        bytes[0] = (byte)(timestampMs >> 40);
        bytes[1] = (byte)(timestampMs >> 32);
        bytes[2] = (byte)(timestampMs >> 24);
        bytes[3] = (byte)(timestampMs >> 16);
        bytes[4] = (byte)(timestampMs >> 8);
        bytes[5] = (byte)timestampMs;

        // Bytes 6-7: version (4 bits) + counter (12 bits)
        bytes[6] = (byte)(Version7 | ((counter >> 8) & VersionMask));
        bytes[7] = (byte)counter;

        // Bytes 8-15: random data with variant bits
        var randomBytes = _randomBuffer.Value!.GetBytes(8);
        randomBytes.CopyTo(bytes.Slice(8, 8));

        // Set variant bits: 10xxxxxx
        bytes[8] = (byte)((bytes[8] & VariantMask) | VariantRfc4122);

        return new Guid(bytes, bigEndian: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long PackState(long timestamp, ushort counter)
    {
        return (timestamp << 16) | counter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (long Timestamp, ushort Counter) UnpackState(long packed)
    {
        return (packed >> 16, (ushort)(packed & 0xFFFF));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort GetRandomCounterStart()
    {
        // Start in lower half of counter space to leave room for increments
        // This reduces collision probability across instances starting in the same ms
        var bytes = _randomBuffer.Value!.GetBytes(2);
        return (ushort)((bytes[0] | (bytes[1] << 8)) & CounterRandomStart);
    }

    private void SpinWaitForNextMillisecond(long currentMs)
    {
        var spinWait = new SpinWait();
        while (_timeProvider.GetUtcNow().ToUnixTimeMilliseconds() <= currentMs)
        {
            spinWait.SpinOnce();

            // Safety valve: don't spin forever in case of broken TimeProvider
            if (spinWait.Count > 10000)
            {
                Thread.Sleep(1);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsRng)
        {
            _rng.Dispose();
        }
        _randomBuffer.Dispose();
    }

    /// <summary>
    /// Thread-local buffer for batching RNG calls.
    /// Reduces syscall overhead significantly.
    /// </summary>
    private sealed class RandomBuffer
    {
        private readonly RandomNumberGenerator _rng;
        private readonly byte[] _buffer;
        private int _position;

        private const int BufferSize = 256; // ~32 GUIDs worth

        public RandomBuffer(RandomNumberGenerator rng)
        {
            _rng = rng;
            _buffer = new byte[BufferSize];
            _position = BufferSize; // Force initial fill
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> GetBytes(int count)
        {
            if (_position + count > BufferSize)
            {
                Refill();
            }

            var result = _buffer.AsSpan(_position, count);
            _position += count;
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Refill()
        {
            _rng.GetBytes(_buffer);
            _position = 0;
        }
    }
}

/// <summary>
/// Behavior when the UUIDv7 counter overflows (> 4095 UUIDs in the same millisecond).
/// </summary>
public enum CounterOverflowBehavior
{
    /// <summary>
    /// Spin-wait until the next millisecond.
    /// Maintains strict time accuracy but may block.
    /// Best for: Testing, simulation, low-throughput production.
    /// </summary>
    SpinWait,

    /// <summary>
    /// Increment the timestamp artificially.
    /// Maintains throughput but timestamp may drift ahead of physical time.
    /// Best for: High-throughput scenarios where ordering matters more than time accuracy.
    /// </summary>
    IncrementTimestamp,

    /// <summary>
    /// Throw an exception.
    /// Useful for detecting unexpected throughput in development/testing.
    /// </summary>
    ThrowException,

    /// <summary>
    /// Automatically choose an overflow strategy based on the time provider.
    /// Defaults to <see cref="IncrementTimestamp"/> for simulated time to avoid deadlocks,
    /// and <see cref="SpinWait"/> for system time.
    /// </summary>
    Auto
}
