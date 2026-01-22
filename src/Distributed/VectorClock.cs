namespace Clockworks.Distributed;

/// <summary>
/// Ordering relationships between vector clocks in a partial order.
/// </summary>
public enum VectorClockOrder
{
    /// <summary>
    /// Vector clocks are identical.
    /// </summary>
    Equal,

    /// <summary>
    /// This vector clock causally precedes the other (happens-before).
    /// </summary>
    Before,

    /// <summary>
    /// This vector clock causally follows the other (happens-after).
    /// </summary>
    After,

    /// <summary>
    /// Vector clocks are concurrent (neither causally precedes the other).
    /// </summary>
    Concurrent
}

/// <summary>
/// A vector clock for tracking causal dependencies in distributed systems.
/// 
/// <para>
/// Uses a performance-first sorted-array representation with parallel arrays
/// for node IDs and counters. This provides O(n) merge and compare operations
/// while maintaining deterministic canonical ordering by node ID.
/// </para>
/// 
/// <para>
/// <b>Mathematical Foundation:</b>
/// A vector clock V is a mapping from node IDs to logical counters.
/// For two vector clocks A and B:
/// - A ≤ B iff ∀i: A[i] ≤ B[i] (A happens-before or concurrent with B)
/// - A &lt; B iff A ≤ B ∧ A ≠ B (A strictly happens-before B)
/// - A || B iff ¬(A ≤ B) ∧ ¬(B ≤ A) (A and B are concurrent)
/// </para>
/// </summary>
public readonly struct VectorClock : IEquatable<VectorClock>
{
    // Sorted parallel arrays for sparse representation
    private readonly ushort[]? _nodeIds;
    private readonly ulong[]? _counters;

    /// <summary>
    /// Creates an empty vector clock.
    /// </summary>
    public VectorClock()
    {
        _nodeIds = null;
        _counters = null;
    }

    private VectorClock(ushort[] nodeIds, ulong[] counters)
    {
        _nodeIds = nodeIds;
        _counters = counters;
    }

    /// <summary>
    /// Gets the counter value for a specific node ID.
    /// Returns 0 if the node ID is not present in this vector clock.
    /// </summary>
    public ulong Get(ushort nodeId)
    {
        if (_nodeIds == null || _counters == null)
            return 0;

        // Binary search since array is sorted
        var index = Array.BinarySearch(_nodeIds, nodeId);
        return index >= 0 ? _counters[index] : 0;
    }

    /// <summary>
    /// Returns a new vector clock with the counter for the specified node incremented.
    /// Maintains canonical sorted order.
    /// </summary>
    public VectorClock Increment(ushort nodeId)
    {
        if (_nodeIds == null || _counters == null)
        {
            // First entry
            return new VectorClock(
                nodeIds: [nodeId],
                counters: [1]
            );
        }

        var index = Array.BinarySearch(_nodeIds, nodeId);
        if (index >= 0)
        {
            // Node exists, increment its counter
            // Must copy both arrays to maintain immutability
            var newNodeIds = new ushort[_nodeIds.Length];
            var newCounters = new ulong[_counters.Length];
            Array.Copy(_nodeIds, newNodeIds, _nodeIds.Length);
            Array.Copy(_counters, newCounters, _counters.Length);
            newCounters[index]++;
            return new VectorClock(newNodeIds, newCounters);
        }
        else
        {
            // Node doesn't exist, insert it maintaining sort order
            var insertIndex = ~index;
            var newNodeIds = new ushort[_nodeIds.Length + 1];
            var newCounters = new ulong[_counters.Length + 1];

            Array.Copy(_nodeIds, 0, newNodeIds, 0, insertIndex);
            Array.Copy(_counters, 0, newCounters, 0, insertIndex);

            newNodeIds[insertIndex] = nodeId;
            newCounters[insertIndex] = 1;

            Array.Copy(_nodeIds, insertIndex, newNodeIds, insertIndex + 1, _nodeIds.Length - insertIndex);
            Array.Copy(_counters, insertIndex, newCounters, insertIndex + 1, _counters.Length - insertIndex);

            return new VectorClock(newNodeIds, newCounters);
        }
    }

    /// <summary>
    /// Merges this vector clock with another, taking the pairwise maximum of all counters.
    /// Returns a new vector clock containing the union of node IDs with max values.
    /// </summary>
    public VectorClock Merge(VectorClock other)
    {
        if (_nodeIds == null || _counters == null)
            return other;
        if (other._nodeIds == null || other._counters == null)
            return this;

        // Merge two sorted arrays
        var resultNodes = new List<ushort>();
        var resultCounters = new List<ulong>();

        int i = 0, j = 0;
        while (i < _nodeIds.Length || j < other._nodeIds.Length)
        {
            if (i >= _nodeIds.Length)
            {
                // Exhausted this, add from other
                resultNodes.Add(other._nodeIds[j]);
                resultCounters.Add(other._counters[j]);
                j++;
            }
            else if (j >= other._nodeIds.Length)
            {
                // Exhausted other, add from this
                resultNodes.Add(_nodeIds[i]);
                resultCounters.Add(_counters[i]);
                i++;
            }
            else if (_nodeIds[i] < other._nodeIds[j])
            {
                resultNodes.Add(_nodeIds[i]);
                resultCounters.Add(_counters[i]);
                i++;
            }
            else if (_nodeIds[i] > other._nodeIds[j])
            {
                resultNodes.Add(other._nodeIds[j]);
                resultCounters.Add(other._counters[j]);
                j++;
            }
            else // Equal node IDs
            {
                resultNodes.Add(_nodeIds[i]);
                resultCounters.Add(Math.Max(_counters[i], other._counters[j]));
                i++;
                j++;
            }
        }

        return new VectorClock(resultNodes.ToArray(), resultCounters.ToArray());
    }

    /// <summary>
    /// Compares this vector clock with another to determine their causal relationship.
    /// </summary>
    public VectorClockOrder Compare(VectorClock other)
    {
        var thisIsLessOrEqual = true;
        var otherIsLessOrEqual = true;

        // Get all unique node IDs from both clocks
        var allNodes = new HashSet<ushort>();
        if (_nodeIds != null)
            foreach (var node in _nodeIds)
                allNodes.Add(node);
        if (other._nodeIds != null)
            foreach (var node in other._nodeIds)
                allNodes.Add(node);

        foreach (var nodeId in allNodes)
        {
            var thisValue = Get(nodeId);
            var otherValue = other.Get(nodeId);

            if (thisValue > otherValue)
                thisIsLessOrEqual = false;  // this > other, so this is NOT ≤ other
            if (otherValue > thisValue)
                otherIsLessOrEqual = false; // other > this, so other is NOT ≤ this

            // Early exit if we know they're concurrent
            if (!thisIsLessOrEqual && !otherIsLessOrEqual)
                return VectorClockOrder.Concurrent;
        }

        if (thisIsLessOrEqual && otherIsLessOrEqual)
            return VectorClockOrder.Equal;
        if (thisIsLessOrEqual)
            return VectorClockOrder.Before;
        if (otherIsLessOrEqual)
            return VectorClockOrder.After;

        return VectorClockOrder.Concurrent;
    }

    /// <summary>
    /// Returns true if this vector clock causally precedes the other (happens-before).
    /// </summary>
    public bool HappensBefore(VectorClock other) => Compare(other) == VectorClockOrder.Before;

    /// <summary>
    /// Returns true if this vector clock causally follows the other (happens-after).
    /// </summary>
    public bool HappensAfter(VectorClock other) => Compare(other) == VectorClockOrder.After;

    /// <summary>
    /// Returns true if this vector clock is concurrent with the other.
    /// </summary>
    public bool IsConcurrentWith(VectorClock other) => Compare(other) == VectorClockOrder.Concurrent;

    /// <summary>
    /// Writes this vector clock to a binary representation.
    /// Format: [count:ushort][nodeId:ushort,counter:ulong]*
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        var count = _nodeIds?.Length ?? 0;
        var requiredSize = 2 + (count * 10); // 2 bytes for count + 10 bytes per entry

        if (destination.Length < requiredSize)
            throw new ArgumentException($"Destination must be at least {requiredSize} bytes", nameof(destination));

        // Write count as big-endian ushort
        destination[0] = (byte)(count >> 8);
        destination[1] = (byte)count;

        if (count == 0)
            return;

        var offset = 2;
        for (var i = 0; i < count; i++)
        {
            var nodeId = _nodeIds![i];
            var counter = _counters![i];

            // Write nodeId as big-endian ushort
            destination[offset++] = (byte)(nodeId >> 8);
            destination[offset++] = (byte)nodeId;

            // Write counter as big-endian ulong
            destination[offset++] = (byte)(counter >> 56);
            destination[offset++] = (byte)(counter >> 48);
            destination[offset++] = (byte)(counter >> 40);
            destination[offset++] = (byte)(counter >> 32);
            destination[offset++] = (byte)(counter >> 24);
            destination[offset++] = (byte)(counter >> 16);
            destination[offset++] = (byte)(counter >> 8);
            destination[offset++] = (byte)counter;
        }
    }

    /// <summary>
    /// Gets the binary size required to serialize this vector clock.
    /// </summary>
    public int GetBinarySize()
    {
        var count = _nodeIds?.Length ?? 0;
        return 2 + (count * 10);
    }

    /// <summary>
    /// Reads a vector clock from its binary representation.
    /// </summary>
    public static VectorClock ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < 2)
            throw new ArgumentException("Source must be at least 2 bytes", nameof(source));

        var count = (ushort)((source[0] << 8) | source[1]);
        var requiredSize = 2 + (count * 10);

        if (source.Length < requiredSize)
            throw new ArgumentException($"Source must be at least {requiredSize} bytes for {count} entries", nameof(source));

        if (count == 0)
            return new VectorClock();

        var nodeIds = new ushort[count];
        var counters = new ulong[count];

        var offset = 2;
        for (var i = 0; i < count; i++)
        {
            // Read nodeId as big-endian ushort
            nodeIds[i] = (ushort)((source[offset++] << 8) | source[offset++]);

            // Read counter as big-endian ulong
            counters[i] = ((ulong)source[offset++] << 56) |
                          ((ulong)source[offset++] << 48) |
                          ((ulong)source[offset++] << 40) |
                          ((ulong)source[offset++] << 32) |
                          ((ulong)source[offset++] << 24) |
                          ((ulong)source[offset++] << 16) |
                          ((ulong)source[offset++] << 8) |
                          source[offset++];
        }

        var isSortedUnique = true;
        for (var i = 1; i < nodeIds.Length; i++)
        {
            if (nodeIds[i] <= nodeIds[i - 1])
            {
                isSortedUnique = false;
                break;
            }
        }

        if (isSortedUnique)
            return new VectorClock(nodeIds, counters);

        var pairs = new List<(ushort nodeId, ulong counter)>(nodeIds.Length);
        for (var i = 0; i < nodeIds.Length; i++)
            pairs.Add((nodeIds[i], counters[i]));

        return CreateCanonical(pairs);
    }

    /// <summary>
    /// Returns a deterministic string representation suitable for HTTP headers.
    /// Format: "node1:counter1,node2:counter2,..." (sorted by node ID)
    /// </summary>
    public override string ToString()
    {
        if (_nodeIds == null || _counters == null || _nodeIds.Length == 0)
            return string.Empty;

        var parts = new string[_nodeIds.Length];
        for (var i = 0; i < _nodeIds.Length; i++)
        {
            parts[i] = $"{_nodeIds[i]}:{_counters[i]}";
        }
        return string.Join(',', parts);
    }

    /// <summary>
    /// Parses a vector clock from string format "node1:counter1,node2:counter2,..."
    /// </summary>
    public static VectorClock Parse(string s)
    {
        if (string.IsNullOrEmpty(s))
            return new VectorClock();

        var entries = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
            return new VectorClock();

        var pairs = new List<(ushort nodeId, ulong counter)>();
        foreach (var entry in entries)
        {
            var parts = entry.Split(':');
            if (parts.Length != 2)
                throw new FormatException($"Invalid vector clock entry: {entry}");

            var nodeId = ushort.Parse(parts[0]);
            var counter = ulong.Parse(parts[1]);
            pairs.Add((nodeId, counter));
        }

        return CreateCanonical(pairs);
    }

    private static VectorClock CreateCanonical(List<(ushort nodeId, ulong counter)> pairs)
    {
        if (pairs.Count == 0)
            return new VectorClock();

        pairs.Sort((a, b) => a.nodeId.CompareTo(b.nodeId));

        var nodeIds = new List<ushort>(pairs.Count);
        var counters = new List<ulong>(pairs.Count);
        foreach (var pair in pairs)
        {
            if (nodeIds.Count > 0 && nodeIds[^1] == pair.nodeId)
            {
                if (pair.counter > counters[^1])
                    counters[^1] = pair.counter;
                continue;
            }

            nodeIds.Add(pair.nodeId);
            counters.Add(pair.counter);
        }

        return new VectorClock(nodeIds.ToArray(), counters.ToArray());
    }

    /// <summary>
    /// Tries to parse a vector clock from string format.
    /// </summary>
    public static bool TryParse(string? s, out VectorClock result)
    {
        result = default;
        if (string.IsNullOrEmpty(s))
        {
            result = new VectorClock();
            return true;
        }

        try
        {
            result = Parse(s);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Equals(VectorClock other)
    {
        if (_nodeIds == null || _counters == null)
            return other._nodeIds == null || other._counters == null;

        if (other._nodeIds == null || other._counters == null)
            return false;

        if (_nodeIds.Length != other._nodeIds.Length)
            return false;

        for (var i = 0; i < _nodeIds.Length; i++)
        {
            if (_nodeIds[i] != other._nodeIds[i] || _counters[i] != other._counters[i])
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is VectorClock other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (_nodeIds == null || _counters == null)
            return 0;

        var hash = new HashCode();
        for (var i = 0; i < _nodeIds.Length; i++)
        {
            hash.Add(_nodeIds[i]);
            hash.Add(_counters[i]);
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// Determines whether two vector clocks are equal.
    /// </summary>
    public static bool operator ==(VectorClock left, VectorClock right) => left.Equals(right);

    /// <summary>
    /// Determines whether two vector clocks are not equal.
    /// </summary>
    public static bool operator !=(VectorClock left, VectorClock right) => !left.Equals(right);
}
