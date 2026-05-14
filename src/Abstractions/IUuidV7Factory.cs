namespace Clockworks.Abstractions;

/// <summary>
/// Abstraction for UUIDv7 generation with time control.
/// </summary>
/// <remarks>
/// Implementations may provide deterministic monotonic allocation guarantees for a single factory instance. They do
/// not imply deterministic global uniqueness across independent factories, processes, or machines unless the
/// implementation explicitly documents such coordination.
/// </remarks>
public interface IUuidV7Factory
{
    /// <summary>
    /// Creates a new UUIDv7 value.
    /// </summary>
    Guid NewGuid();
    
    /// <summary>
    /// Creates a new UUIDv7 and returns the timestamp used.
    /// Useful for correlation and debugging.
    /// </summary>
    (Guid Guid, long TimestampMs) NewGuidWithTimestamp();
    
    /// <summary>
    /// Batch generation for high-throughput scenarios. More efficient than calling <see cref="NewGuid"/> in a loop.
    /// </summary>
    void NewGuids(Span<Guid> destination);
}
