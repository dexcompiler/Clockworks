using Clockworks.Distributed;

namespace Clockworks.Abstractions;

/// <summary>
/// Extended interface for factories that support Hybrid Logical Clock semantics.
/// </summary>
public interface IHlcGuidFactory : IUuidV7Factory
{
    /// <summary>
    /// Gets the current logical timestamp without generating a GUID.
    /// </summary>
    HlcTimestamp CurrentTimestamp { get; }
    
    /// <summary>
    /// Creates a new GUID and returns full HLC state.
    /// </summary>
    (Guid Guid, HlcTimestamp Timestamp) NewGuidWithHlc();
    
    /// <summary>
    /// Witness/receive a timestamp from another node.
    /// Updates local clock to maintain causality.
    /// </summary>
    /// <param name="remoteTimestamp">Timestamp received from remote node</param>
    void Witness(HlcTimestamp remoteTimestamp);
    
    /// <summary>
    /// Witness a raw millisecond timestamp (e.g., from a message header).
    /// </summary>
    void Witness(long remoteTimestampMs);
}
