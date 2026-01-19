using System.Collections.Concurrent;

namespace Clockworks.Distributed;

/// <summary>
/// Coordinator for HLC synchronization across distributed nodes.
/// 
/// <para>
/// <b>Usage in Trading Systems:</b>
/// 
/// When events flow between services (order matching, risk calculation, 
/// settlement), each service should:
/// 1. Call BeforeReceive() when receiving a message with timestamp
/// 2. Call BeforeSend() when sending a message to get current timestamp
/// 3. Include the timestamp in message headers
/// 
/// This ensures causal ordering is preserved across service boundaries.
/// </para>
/// 
/// <para>
/// <b>Mathematical Guarantee:</b>
/// For any two events e and f across the distributed system:
///   e happens-before f ⟹ HLC(e) &lt; HLC(f)
/// 
/// This is achieved by propagating the maximum observed timestamp
/// through message passing, as per Lamport's logical clocks extended
/// with physical time bounds.
/// </para>
/// </summary>
public sealed class HlcCoordinator
{
    private readonly HlcGuidFactory _factory;

    /// <summary>
    /// Statistics for monitoring clock behavior.
    /// </summary>
    public HlcStatistics Statistics { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HlcCoordinator"/>.
    /// </summary>
    /// <param name="factory">The underlying HLC GUID factory used to maintain local clock state.</param>
    public HlcCoordinator(HlcGuidFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Call before processing a received message.
    /// Updates local clock to maintain causality.
    /// </summary>
    /// <param name="remoteTimestamp">Timestamp from message header</param>
    public void BeforeReceive(HlcTimestamp remoteTimestamp)
    {
        var before = _factory.CurrentTimestamp;
        _factory.Witness(remoteTimestamp);
        var after = _factory.CurrentTimestamp;
        
        Statistics.RecordReceive(before, after, remoteTimestamp);
    }

    /// <summary>
    /// Call before processing a received message (from raw timestamp).
    /// </summary>
    public void BeforeReceive(long remoteTimestampMs)
    {
        BeforeReceive(new HlcTimestamp(remoteTimestampMs));
    }

    /// <summary>
    /// Call when sending a message. Returns timestamp to include in header.
    /// </summary>
    public HlcTimestamp BeforeSend()
    {
        var (_, timestamp) = _factory.NewGuidWithHlc();
        Statistics.RecordSend(timestamp);
        return timestamp;
    }

    /// <summary>
    /// Generate a GUID for a local event (no message passing).
    /// </summary>
    public Guid NewLocalEventGuid()
    {
        var (guid, timestamp) = _factory.NewGuidWithHlc();
        Statistics.RecordLocalEvent(timestamp);
        return guid;
    }

    /// <summary>
    /// Get current clock state for diagnostics.
    /// </summary>
    public HlcTimestamp CurrentTimestamp => _factory.CurrentTimestamp;
}

/// <summary>
/// Statistics tracking for HLC behavior analysis.
/// Thread-safe for concurrent updates.
/// </summary>
public sealed class HlcStatistics
{
    private long _localEventCount;
    private long _sendCount;
    private long _receiveCount;
    private long _clockAdvances;  // Times we advanced due to remote
    private long _maxObservedDrift;
    private long _maxRemoteAheadMs;
    private long _maxRemoteBehindMs;
    private long _remoteAheadCount;

    /// <summary>
    /// Total number of local events recorded.
    /// </summary>
    public long LocalEventCount => Volatile.Read(ref _localEventCount);

    /// <summary>
    /// Total number of send events recorded.
    /// </summary>
    public long SendCount => Volatile.Read(ref _sendCount);

    /// <summary>
    /// Total number of receive events recorded.
    /// </summary>
    public long ReceiveCount => Volatile.Read(ref _receiveCount);

    /// <summary>
    /// Number of times the local clock advanced due to observing a remote timestamp.
    /// </summary>
    public long ClockAdvances => Volatile.Read(ref _clockAdvances);

    /// <summary>
    /// Maximum absolute observed drift (in milliseconds) between a received remote timestamp and the local timestamp prior to witnessing.
    /// </summary>
    public long MaxObservedDriftMs => Volatile.Read(ref _maxObservedDrift);

    /// <summary>
    /// Maximum observed amount (in milliseconds) by which a remote timestamp was ahead of the local timestamp prior to witnessing.
    /// </summary>
    public long MaxRemoteAheadMs => Volatile.Read(ref _maxRemoteAheadMs);

    /// <summary>
    /// Maximum observed amount (in milliseconds) by which a remote timestamp was behind the local timestamp prior to witnessing.
    /// </summary>
    public long MaxRemoteBehindMs => Volatile.Read(ref _maxRemoteBehindMs);

    /// <summary>
    /// Number of receive events where the remote timestamp was ahead of or equal to the local timestamp prior to witnessing.
    /// </summary>
    public long RemoteAheadCount => Volatile.Read(ref _remoteAheadCount);

    internal void RecordLocalEvent(HlcTimestamp timestamp)
    {
        Interlocked.Increment(ref _localEventCount);
    }

    internal void RecordSend(HlcTimestamp timestamp)
    {
        Interlocked.Increment(ref _sendCount);
    }

    internal void RecordReceive(HlcTimestamp before, HlcTimestamp after, HlcTimestamp remote)
    {
        Interlocked.Increment(ref _receiveCount);

        // Clock advances due to remote when:
        // 1. The remote timestamp was ahead of our local time before witnessing, AND
        // 2. The remote timestamp was adopted (became our new wall time after witnessing)
        // This indicates the local clock moved forward by adopting the remote timestamp.
        if (remote.WallTimeMs > before.WallTimeMs && after.WallTimeMs == remote.WallTimeMs)
        {
            Interlocked.Increment(ref _clockAdvances);
        }

        var delta = remote.WallTimeMs - before.WallTimeMs;
        if (delta >= 0)
        {
            Interlocked.Increment(ref _remoteAheadCount);
            InterlockedMax(ref _maxRemoteAheadMs, delta);
        }
        else
        {
            InterlockedMax(ref _maxRemoteBehindMs, -delta);
        }

        // Track absolute drift
        InterlockedMax(ref _maxObservedDrift, Math.Abs(delta));
    }

    private static void InterlockedMax(ref long location, long value)
    {
        long current = Volatile.Read(ref location);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref location, value, current);
            if (previous == current) break;
            current = previous;
        }
    }

    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    public void Reset()
    {
        _localEventCount = 0;
        _sendCount = 0;
        _receiveCount = 0;
        _clockAdvances = 0;
        _maxObservedDrift = 0;
        _maxRemoteAheadMs = 0;
        _maxRemoteBehindMs = 0;
        _remoteAheadCount = 0;
    }

    /// <summary>
    /// Returns a human-readable summary of the current statistics.
    /// </summary>
    public override string ToString() =>
        $"Local: {LocalEventCount}, Send: {SendCount}, Receive: {ReceiveCount}, " +
        $"Advances: {ClockAdvances}, MaxDrift: {MaxObservedDriftMs}ms, " +
        $"MaxAhead: {MaxRemoteAheadMs}ms, MaxBehind: {MaxRemoteBehindMs}ms";
}

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
        var result = Timestamp.ToString();
        if (CorrelationId.HasValue)
        {
            result += $";{CorrelationId.Value:N}";
            if (CausationId.HasValue)
            {
                result += $";{CausationId.Value:N}";
            }
        }
        return result;
    }

    /// <summary>
    /// Parse from header string.
    /// </summary>
    public static HlcMessageHeader Parse(string value)
    {
        var parts = value.Split(';');
        var timestamp = HlcTimestamp.Parse(parts[0]);
        
        Guid? correlation = parts.Length > 1 ? Guid.Parse(parts[1]) : null;
        Guid? causation = parts.Length > 2 ? Guid.Parse(parts[2]) : null;
        
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

/// <summary>
/// Registry for tracking multiple HLC nodes in a cluster.
/// Useful for testing and simulation of distributed systems.
/// </summary>
public sealed class HlcClusterRegistry
{
    private readonly ConcurrentDictionary<ushort, HlcGuidFactory> _nodes = new();
    private readonly TimeProvider _sharedTimeProvider;

    /// <summary>
    /// Creates a registry for tracking multiple HLC nodes that share a single <see cref="TimeProvider"/>.
    /// </summary>
    public HlcClusterRegistry(TimeProvider timeProvider)
    {
        _sharedTimeProvider = timeProvider;
    }

    /// <summary>
    /// Register a node in the cluster.
    /// </summary>
    public HlcGuidFactory RegisterNode(ushort nodeId, HlcOptions? options = null)
    {
        return _nodes.GetOrAdd(nodeId, id => new HlcGuidFactory(_sharedTimeProvider, id, options));
    }

    /// <summary>
    /// Get a registered node.
    /// </summary>
    public HlcGuidFactory? GetNode(ushort nodeId)
    {
        return _nodes.TryGetValue(nodeId, out var factory) ? factory : null;
    }

    /// <summary>
    /// Simulate sending a message from one node to another.
    /// Updates receiver's clock based on sender's timestamp.
    /// </summary>
    public void SimulateMessage(ushort senderId, ushort receiverId)
    {
        if (!_nodes.TryGetValue(senderId, out var sender))
            throw new ArgumentException($"Sender node {senderId} not registered");
        if (!_nodes.TryGetValue(receiverId, out var receiver))
            throw new ArgumentException($"Receiver node {receiverId} not registered");

        var (_, senderTimestamp) = sender.NewGuidWithHlc();
        receiver.Witness(senderTimestamp);
    }

    /// <summary>
    /// Get all registered nodes.
    /// </summary>
    public IEnumerable<(ushort NodeId, HlcGuidFactory Factory)> GetAllNodes()
    {
        return _nodes.Select(kvp => (kvp.Key, kvp.Value));
    }

    /// <summary>
    /// Get the maximum logical time across all nodes.
    /// Useful for determining global ordering.
    /// </summary>
    public long GetMaxLogicalTime()
    {
        return _nodes.Values.Max(f => f.CurrentTimestamp.WallTimeMs);
    }
}
