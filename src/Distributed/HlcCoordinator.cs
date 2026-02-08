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
