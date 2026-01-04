namespace Clockworks.Abstractions;

/// <summary>
/// Abstraction for UUIDv7 generation with time control.
/// </summary>
public interface IUuidV7Factory
{
    /// <summary>
    /// Creates a new UUIDv7.
    /// </summary>
    Guid NewGuid();
    
    /// <summary>
    /// Creates a new UUIDv7 and returns the timestamp used.
    /// Useful for correlation and debugging.
    /// </summary>
    (Guid Guid, long TimestampMs) NewGuidWithTimestamp();
    
    /// <summary>
    /// Batch generation for high-throughput scenarios.
    /// More efficient than calling NewGuid() in a loop.
    /// </summary>
    void NewGuids(Span<Guid> destination);
}
