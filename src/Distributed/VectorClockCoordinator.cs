namespace Clockworks.Distributed;

/// <summary>
/// Coordinator for Vector Clock synchronization across distributed nodes.
/// 
/// <para>
/// <b>Usage in Distributed Systems:</b>
/// 
/// When events flow between services, each service should:
/// 1. Call BeforeReceive() when receiving a message with a vector clock
/// 2. Call BeforeSend() when sending a message to get the current vector clock
/// 3. Include the vector clock in message headers
/// 
/// This ensures causal relationships and concurrency are accurately tracked.
/// </para>
/// 
/// <para>
/// <b>Mathematical Guarantee:</b>
/// For any two events e and f across the distributed system:
/// - If e happens-before f, then VC(e) &lt; VC(f)
/// - If VC(e) || VC(f), then e and f are concurrent
/// 
/// This enables precise detection of both causality and concurrency.
/// </para>
/// </summary>
public sealed class VectorClockCoordinator
{
    private readonly ushort _nodeId;
    private readonly object _lock = new();
    private VectorClock _current;

    /// <summary>
    /// Statistics for monitoring vector clock behavior.
    /// </summary>
    public VectorClockStatistics Statistics { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="VectorClockCoordinator"/>.
    /// </summary>
    /// <param name="nodeId">The unique node identifier for this coordinator.</param>
    public VectorClockCoordinator(ushort nodeId)
    {
        _nodeId = nodeId;
        _current = new VectorClock();
    }

    /// <summary>
    /// Gets a snapshot of the current vector clock state.
    /// </summary>
    public VectorClock Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Call before processing a received message.
    /// Merges the remote vector clock into the local clock and increments the local counter.
    /// </summary>
    /// <param name="remote">Vector clock from the received message.</param>
    public void BeforeReceive(VectorClock remote)
    {
        lock (_lock)
        {
            var before = _current;
            _current = _current.Merge(remote).Increment(_nodeId);
            Statistics.RecordReceive(before, _current, remote);
        }
    }

    /// <summary>
    /// Call when sending a message.
    /// Increments the local counter and returns a snapshot to attach to the message.
    /// </summary>
    /// <returns>The current vector clock to include in the outgoing message.</returns>
    public VectorClock BeforeSend()
    {
        lock (_lock)
        {
            _current = _current.Increment(_nodeId);
            var snapshot = _current;
            Statistics.RecordSend(snapshot);
            return snapshot;
        }
    }

    /// <summary>
    /// Call when a local event occurs (no message passing).
    /// Increments the local counter.
    /// </summary>
    public void NewLocalEvent()
    {
        lock (_lock)
        {
            _current = _current.Increment(_nodeId);
            Statistics.RecordLocalEvent(_current);
        }
    }
}

/// <summary>
/// Statistics tracking for Vector Clock behavior analysis.
/// Thread-safe for concurrent updates.
/// </summary>
public sealed class VectorClockStatistics
{
    private long _localEventCount;
    private long _sendCount;
    private long _receiveCount;
    private long _clockMerges;

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
    /// Number of times the local clock was merged with a remote clock.
    /// </summary>
    public long ClockMerges => Volatile.Read(ref _clockMerges);

    internal void RecordLocalEvent(VectorClock current)
    {
        Interlocked.Increment(ref _localEventCount);
    }

    internal void RecordSend(VectorClock current)
    {
        Interlocked.Increment(ref _sendCount);
    }

    internal void RecordReceive(VectorClock before, VectorClock after, VectorClock remote)
    {
        Interlocked.Increment(ref _receiveCount);
        Interlocked.Increment(ref _clockMerges);
    }

    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    public void Reset()
    {
        _localEventCount = 0;
        _sendCount = 0;
        _receiveCount = 0;
        _clockMerges = 0;
    }

    /// <summary>
    /// Returns a human-readable summary of the current statistics.
    /// </summary>
    public override string ToString() =>
        $"Local: {LocalEventCount}, Send: {SendCount}, Receive: {ReceiveCount}, Merges: {ClockMerges}";
}

/// <summary>
/// Message header for propagating Vector Clocks across service boundaries.
/// Can be serialized to/from various wire formats.
/// </summary>
public readonly record struct VectorClockMessageHeader
{
    /// <summary>
    /// Standard header name for HTTP/gRPC.
    /// </summary>
    public const string HeaderName = "X-VectorClock";

    /// <summary>
    /// The vector clock to propagate.
    /// </summary>
    public VectorClock Clock { get; init; }

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
    public VectorClockMessageHeader(VectorClock clock, Guid? correlationId = null, Guid? causationId = null)
    {
        Clock = clock;
        CorrelationId = correlationId;
        CausationId = causationId;
    }

    /// <summary>
    /// Serialize to a compact string for HTTP headers.
    /// Format: "node1:counter1,node2:counter2[;correlation;causation]"
    /// </summary>
    public override string ToString()
    {
        var result = Clock.ToString();
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
    public static VectorClockMessageHeader Parse(string value)
    {
        var parts = value.Split(';');
        var clock = VectorClock.Parse(parts[0]);

        Guid? correlation = parts.Length > 1 ? Guid.Parse(parts[1]) : null;
        Guid? causation = parts.Length > 2 ? Guid.Parse(parts[2]) : null;

        return new VectorClockMessageHeader(clock, correlation, causation);
    }

    /// <summary>
    /// Try to parse from header string.
    /// </summary>
    public static bool TryParse(string? value, out VectorClockMessageHeader header)
    {
        header = default;
        if (string.IsNullOrEmpty(value))
            return false;

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
