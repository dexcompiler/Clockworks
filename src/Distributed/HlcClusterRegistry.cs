using System.Collections.Concurrent;

namespace Clockworks.Distributed;

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
