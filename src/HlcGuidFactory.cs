using Clockworks.Abstractions;
using Clockworks.Distributed;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Clockworks;

/// <summary>
/// UUIDv7 generator with Hybrid Logical Clock (HLC) semantics.
/// 
/// <para>
/// <b>Mathematical Foundation:</b>
/// 
/// HLC combines Lamport's logical clocks with physical time to provide:
/// 1. Causality preservation: e → f ⟹ HLC(e) &lt; HLC(f)
/// 2. Bounded drift: |HLC.l - PT| ≤ ε for configurable ε
/// 3. O(1) comparison with total ordering
/// 
/// The algorithm maintains the invariant:
///   l.j ≥ max(l.i, pt.j) for send event j after receive from i
/// 
/// This gives us a timestamp that:
/// - Always moves forward (monotonic)
/// - Stays close to wall-clock time (bounded drift)
/// - Captures happens-before relationships (causal)
/// </para>
/// 
/// <para>
/// <b>Why HLC for Trading Systems?</b>
/// 
/// In distributed trading/DeFi systems, you often have:
/// - Multiple order matching engines
/// - Event sourcing with replay requirements
/// - Cross-service event correlation
/// - Regulatory audit requirements (MiFID II, etc.)
/// 
/// HLC provides a single timestamp that can:
/// - Order events consistently across nodes
/// - Be used as a database key (lexicographically sortable)
/// - Survive node restarts without coordination
/// - Handle clock skew gracefully
/// </para>
/// 
/// <para>
/// <b>UUIDv7 Encoding:</b>
/// We encode HLC state into UUIDv7's 128 bits:
/// - Bits 0-47: Logical wall time (milliseconds)
/// - Bits 48-51: Version (7)
/// - Bits 52-63: Counter (12 bits)
/// - Bits 64-65: Variant
/// - Bits 66-79: Node ID (14 bits, supports 16K nodes)
/// - Bits 80-127: Random (48 bits)
/// </para>
/// </summary>
public sealed class HlcGuidFactory : IHlcGuidFactory, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly RandomNumberGenerator _rng;
    private readonly bool _ownsRng;
    private readonly ushort _nodeId;
    private readonly HlcOptions _options;
    
    // HLC state - must be accessed under lock for compound updates
    private readonly Lock _lock = new();
    private long _logicalTimeMs;
    private ushort _counter;
    
    // Pre-allocated random buffer (protected by _lock since we need lock anyway)
    private readonly byte[] _randomBuffer = new byte[64];
    private int _randomPosition = 64;
    
    // UUID constants
    private const byte Version7 = 0x70;
    private const byte VersionMask = 0x0F;
    private const byte VariantRfc4122 = 0x80;
    private const byte VariantMask = 0x3F;
    private const int MaxCounterValue = 0xFFF;

    /// <summary>
    /// Creates a new HLC-based GUID factory.
    /// </summary>
    /// <param name="timeProvider">Time source</param>
    /// <param name="nodeId">Unique identifier for this node (0-65535)</param>
    /// <param name="options">HLC configuration options</param>
    /// <param name="rng">Random number generator (null = create new CSPRNG)</param>
    public HlcGuidFactory(
        TimeProvider timeProvider,
        ushort nodeId = 0,
        HlcOptions? options = null,
        RandomNumberGenerator? rng = null)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _nodeId = nodeId;
        _options = options ?? HlcOptions.Default;
        _rng = rng ?? RandomNumberGenerator.Create();
        _ownsRng = rng is null;
        
        // Initialize to current physical time
        _logicalTimeMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        _counter = 0;
    }

    /// <inheritdoc/>
    public HlcTimestamp CurrentTimestamp
    {
        get
        {
            lock (_lock)
            {
                return new HlcTimestamp(_logicalTimeMs, _counter, _nodeId);
            }
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Guid NewGuid() => NewGuidWithHlc().Guid;

    /// <inheritdoc/>
    public (Guid Guid, long TimestampMs) NewGuidWithTimestamp()
    {
        var (guid, hlc) = NewGuidWithHlc();
        return (guid, hlc.WallTimeMs);
    }

    /// <inheritdoc/>
    public void NewGuids(Span<Guid> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = NewGuid();
        }
    }

    /// <inheritdoc/>
    public (Guid Guid, HlcTimestamp Timestamp) NewGuidWithHlc()
    {
        HlcTimestamp timestamp;
        Span<byte> randomBytes = stackalloc byte[6];
        
        lock (_lock)
        {
            var physicalTimeMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            
            // HLC send/local event algorithm
            if (physicalTimeMs > _logicalTimeMs)
            {
                // Physical time advanced - sync to it
                _logicalTimeMs = physicalTimeMs;
                _counter = 0;
            }
            else
            {
                // Physical time hasn't advanced - increment counter
                _counter++;
                
                if (_counter > MaxCounterValue)
                {
                    // Counter overflow - must advance logical time
                    _logicalTimeMs++;
                    _counter = 0;
                    
                    // Check drift bounds
                    var drift = _logicalTimeMs - physicalTimeMs;
                    if (drift > _options.MaxDriftMs)
                    {
                        if (_options.ThrowOnExcessiveDrift)
                        {
                            throw new HlcDriftException(drift, _options.MaxDriftMs);
                        }
                        // Otherwise, we accept the drift (for high-throughput scenarios)
                    }
                }
            }
            
            timestamp = new HlcTimestamp(_logicalTimeMs, _counter, _nodeId);
            
            // Get random bytes while under lock (we have the lock anyway)
            GetRandomBytes(randomBytes);
        }

        return (CreateGuidFromHlc(timestamp, randomBytes), timestamp);
    }

    /// <inheritdoc/>
    public void Witness(HlcTimestamp remoteTimestamp)
    {
        lock (_lock)
        {
            var physicalTimeMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

            // Physical time participates in max selection but has no counter/node information.
            // We treat it as (physicalTimeMs, 0, 0) for ordering purposes.
            var physical = new HlcTimestamp(physicalTimeMs, counter: 0, nodeId: 0);
            var local = new HlcTimestamp(_logicalTimeMs, _counter, _nodeId);

            var max = local;
            if (remoteTimestamp > max) max = remoteTimestamp;
            if (physical > max) max = physical;

            if (max.WallTimeMs == local.WallTimeMs && max.Counter == local.Counter && max.NodeId == local.NodeId)
            {
                // Local time is already the max (or tied with max) - just increment counter.
                _counter++;
                if (_counter > MaxCounterValue)
                {
                    _logicalTimeMs++;
                    _counter = 0;
                }
            }
            else if (max.WallTimeMs == remoteTimestamp.WallTimeMs && max.Counter == remoteTimestamp.Counter && max.NodeId == remoteTimestamp.NodeId)
            {
                // Remote timestamp is the max - adopt its wall time and advance counter beyond it.
                _logicalTimeMs = remoteTimestamp.WallTimeMs;
                _counter = (ushort)(remoteTimestamp.Counter + 1);
                if (_counter > MaxCounterValue)
                {
                    _logicalTimeMs++;
                    _counter = 0;
                }
            }
            else
            {
                // Physical time is the max - sync to it.
                _logicalTimeMs = physicalTimeMs;
                _counter = 0;
            }

            // Check drift bounds
            var drift = _logicalTimeMs - physicalTimeMs;
            if (drift > _options.MaxDriftMs && _options.ThrowOnExcessiveDrift)
            {
                throw new HlcDriftException(drift, _options.MaxDriftMs);
            }
        }
    }

    /// <inheritdoc/>
    public void Witness(long remoteTimestampMs)
    {
        lock (_lock)
        {
            var physicalTimeMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var maxTime = Math.Max(physicalTimeMs, Math.Max(_logicalTimeMs, remoteTimestampMs));
            
            if (maxTime == _logicalTimeMs)
            {
                // Our logical time is already the max - just increment counter
                _counter++;
                if (_counter > MaxCounterValue)
                {
                    _logicalTimeMs++;
                    _counter = 0;
                }
            }
            else if (maxTime == remoteTimestampMs)
            {
                // Remote time is ahead - adopt it
                _logicalTimeMs = remoteTimestampMs;
                _counter = 1; // Start at 1 since remote used 0
            }
            else
            {
                // Physical time is the max - sync to it
                _logicalTimeMs = physicalTimeMs;
                _counter = 0;
            }
            
            // Check drift bounds
            var drift = _logicalTimeMs - physicalTimeMs;
            if (drift > _options.MaxDriftMs && _options.ThrowOnExcessiveDrift)
            {
                throw new HlcDriftException(drift, _options.MaxDriftMs);
            }
        }
    }

    /// <summary>
    /// Synchronize with another HLC clock (bidirectional merge).
    /// Useful for cluster synchronization protocols.
    /// </summary>
    public HlcTimestamp Sync(HlcTimestamp remoteTimestamp)
    {
        lock (_lock)
        {
            Witness(remoteTimestamp.WallTimeMs);
            return new HlcTimestamp(_logicalTimeMs, _counter, _nodeId);
        }
    }

    /// <summary>
    /// Get a snapshot of current state for serialization/checkpointing.
    /// </summary>
    public HlcState GetState()
    {
        lock (_lock)
        {
            return new HlcState(_logicalTimeMs, _counter, _nodeId);
        }
    }

    /// <summary>
    /// Restore state from a checkpoint.
    /// Useful for crash recovery or simulation replay.
    /// </summary>
    public void RestoreState(HlcState state)
    {
        lock (_lock)
        {
            // Only advance forward - never go backwards
            if (state.LogicalTimeMs > _logicalTimeMs ||
                (state.LogicalTimeMs == _logicalTimeMs && state.Counter > _counter))
            {
                _logicalTimeMs = state.LogicalTimeMs;
                _counter = state.Counter;
            }
        }
    }

    private Guid CreateGuidFromHlc(HlcTimestamp timestamp, ReadOnlySpan<byte> randomBytes)
    {
        Span<byte> bytes = stackalloc byte[16];
        
        // Bytes 0-5: 48-bit logical timestamp (big-endian)
        bytes[0] = (byte)(timestamp.WallTimeMs >> 40);
        bytes[1] = (byte)(timestamp.WallTimeMs >> 32);
        bytes[2] = (byte)(timestamp.WallTimeMs >> 24);
        bytes[3] = (byte)(timestamp.WallTimeMs >> 16);
        bytes[4] = (byte)(timestamp.WallTimeMs >> 8);
        bytes[5] = (byte)timestamp.WallTimeMs;

        // Bytes 6-7: version (4 bits) + counter (12 bits)
        bytes[6] = (byte)(Version7 | ((timestamp.Counter >> 8) & VersionMask));
        bytes[7] = (byte)timestamp.Counter;

        // Bytes 8-9: variant (2 bits) + node ID high bits (14 bits across bytes 8-9)
        // We encode node ID in the "random" portion for correlation
        bytes[8] = (byte)(VariantRfc4122 | ((timestamp.NodeId >> 8) & VariantMask));
        bytes[9] = (byte)timestamp.NodeId;

        // Bytes 10-15: random (48 bits)
        randomBytes.Slice(0, 6).CopyTo(bytes.Slice(10, 6));

        return new Guid(bytes, bigEndian: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GetRandomBytes(Span<byte> destination)
    {
        if (_randomPosition + destination.Length > _randomBuffer.Length)
        {
            _rng.GetBytes(_randomBuffer);
            _randomPosition = 0;
        }
        
        _randomBuffer.AsSpan(_randomPosition, destination.Length).CopyTo(destination);
        _randomPosition += destination.Length;
    }

    /// <summary>
    /// Disposes resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_ownsRng)
        {
            _rng.Dispose();
        }
    }
}

/// <summary>
/// Configuration options for HLC behavior.
/// </summary>
public sealed class HlcOptions
{
    /// <summary>
    /// Default options: 1 minute max drift, throw on excessive drift.
    /// </summary>
    public static readonly HlcOptions Default = new()
    {
        MaxDriftMs = 60_000,  // 1 minute
        ThrowOnExcessiveDrift = true
    };
    
    /// <summary>
    /// High-throughput options: 5 minute max drift, don't throw.
    /// Use when throughput is more important than time accuracy.
    /// </summary>
    public static readonly HlcOptions HighThroughput = new()
    {
        MaxDriftMs = 300_000,  // 5 minutes
        ThrowOnExcessiveDrift = false
    };
    
    /// <summary>
    /// Strict options: 1 second max drift, throw on excessive drift.
    /// Use when time accuracy is critical.
    /// </summary>
    public static readonly HlcOptions Strict = new()
    {
        MaxDriftMs = 1_000,  // 1 second
        ThrowOnExcessiveDrift = true
    };

    /// <summary>
    /// Maximum allowed drift between logical and physical time in milliseconds.
    /// </summary>
    public long MaxDriftMs { get; init; } = 60_000;

    /// <summary>
    /// Whether to throw an exception when drift exceeds MaxDriftMs.
    /// If false, drift is allowed to grow unbounded (use with caution).
    /// </summary>
    public bool ThrowOnExcessiveDrift { get; init; } = true;
}

/// <summary>
/// Serializable HLC state for checkpointing/persistence.
/// </summary>
public readonly record struct HlcState(long LogicalTimeMs, ushort Counter, ushort NodeId)
{
    /// <summary>
    /// Serialize to bytes for storage.
    /// </summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[12];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 8), LogicalTimeMs);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 2), Counter);
        BitConverter.TryWriteBytes(bytes.AsSpan(10, 2), NodeId);
        return bytes;
    }

    /// <summary>
    /// Deserialize from bytes.
    /// </summary>
    public static HlcState FromBytes(ReadOnlySpan<byte> bytes)
    {
        return new HlcState(
            BitConverter.ToInt64(bytes.Slice(0, 8)),
            BitConverter.ToUInt16(bytes.Slice(8, 2)),
            BitConverter.ToUInt16(bytes.Slice(10, 2))
        );
    }
}

/// <summary>
/// Exception thrown when HLC drift exceeds configured bounds.
/// </summary>
public sealed class HlcDriftException : Exception
{
    /// <summary>
    /// Gets the measured drift in milliseconds.
    /// </summary>
    public long ActualDriftMs { get; }

    /// <summary>
    /// Gets the configured maximum allowed drift in milliseconds.
    /// </summary>
    public long MaxAllowedDriftMs { get; }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    public HlcDriftException(long actualDrift, long maxDrift)
        : base($"HLC drift of {actualDrift}ms exceeds maximum allowed drift of {maxDrift}ms. " +
               $"This indicates either extremely high throughput (>4M events/sec sustained) " +
               $"or clock synchronization issues.")
    {
        ActualDriftMs = actualDrift;
        MaxAllowedDriftMs = maxDrift;
    }
}
