using System.Buffers.Binary;
using System.Globalization;

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
/// for node IDs and counters. This provides O(n+m) merge and compare operations
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
    private const int MaxEntries = ushort.MaxValue + 1;

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
            // Node exists; node IDs array does not change, so it can be shared safely.
            var newCounters = new ulong[_counters.Length];
            Array.Copy(_counters, newCounters, _counters.Length);
            newCounters[index]++;
            return new VectorClock(_nodeIds, newCounters);
        }
        else
        {
            // Node doesn't exist, insert it maintaining sort order
            if (_nodeIds.Length >= MaxEntries)
                throw new InvalidOperationException($"Cannot increment: vector clock at max capacity ({MaxEntries})");
            
            var insertIndex = ~index;
            var newNodeIds = new ushort[_nodeIds.Length + 1];
            var newCounters = new ulong[_counters.Length + 1];

            if (insertIndex > 0)
            {
                Array.Copy(_nodeIds, 0, newNodeIds, 0, insertIndex);
                Array.Copy(_counters, 0, newCounters, 0, insertIndex);
            }

            newNodeIds[insertIndex] = nodeId;
            newCounters[insertIndex] = 1;

            var tail = _nodeIds.Length - insertIndex;
            if (tail > 0)
            {
                Array.Copy(_nodeIds, insertIndex, newNodeIds, insertIndex + 1, tail);
                Array.Copy(_counters, insertIndex, newCounters, insertIndex + 1, tail);
            }

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

        // Merge two sorted arrays (upper bound = combined length)
        var maxLen = _nodeIds.Length + other._nodeIds.Length;
        var nodeIds = new ushort[maxLen];
        var counters = new ulong[maxLen];

        int i = 0, j = 0, k = 0;
        while (i < _nodeIds.Length || j < other._nodeIds.Length)
        {
            if (i >= _nodeIds.Length)
            {
                nodeIds[k] = other._nodeIds[j];
                counters[k] = other._counters[j];
                j++;
            }
            else if (j >= other._nodeIds.Length)
            {
                nodeIds[k] = _nodeIds[i];
                counters[k] = _counters[i];
                i++;
            }
            else if (_nodeIds[i] < other._nodeIds[j])
            {
                nodeIds[k] = _nodeIds[i];
                counters[k] = _counters[i];
                i++;
            }
            else if (_nodeIds[i] > other._nodeIds[j])
            {
                nodeIds[k] = other._nodeIds[j];
                counters[k] = other._counters[j];
                j++;
            }
            else
            {
                nodeIds[k] = _nodeIds[i];
                counters[k] = Math.Max(_counters[i], other._counters[j]);
                i++;
                j++;
            }

            k++;
        }

        if (k == maxLen)
            return new VectorClock(nodeIds, counters);

        Array.Resize(ref nodeIds, k);
        Array.Resize(ref counters, k);
        return new VectorClock(nodeIds, counters);
    }

    /// <summary>
    /// Compares this vector clock with another to determine their causal relationship.
    /// </summary>
    public VectorClockOrder Compare(VectorClock other)
    {
        if (_nodeIds == null || _counters == null)
        {
            if (other._nodeIds == null || other._counters == null)
                return VectorClockOrder.Equal;
            return VectorClockOrder.Before;
        }

        if (other._nodeIds == null || other._counters == null)
            return VectorClockOrder.After;

        var thisIsLessOrEqual = true;
        var otherIsLessOrEqual = true;

        // Linear merge-style comparison over two sorted arrays.
        // Missing entries are treated as 0.
        int i = 0, j = 0;
        while (i < _nodeIds.Length || j < other._nodeIds.Length)
        {
            ushort nodeId;
            ulong thisValue;
            ulong otherValue;

            if (j >= other._nodeIds.Length || (i < _nodeIds.Length && _nodeIds[i] < other._nodeIds[j]))
            {
                nodeId = _nodeIds[i];
                thisValue = _counters[i];
                otherValue = 0;
                i++;
            }
            else if (i >= _nodeIds.Length || _nodeIds[i] > other._nodeIds[j])
            {
                nodeId = other._nodeIds[j];
                thisValue = 0;
                otherValue = other._counters[j];
                j++;
            }
            else
            {
                nodeId = _nodeIds[i];
                thisValue = _counters[i];
                otherValue = other._counters[j];
                i++;
                j++;
            }

            if (thisValue > otherValue)
                thisIsLessOrEqual = false;
            else if (otherValue > thisValue)
                otherIsLessOrEqual = false;

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
    /// Format: [count:uint][nodeId:ushort,counter:ulong]*
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        var count = _nodeIds?.Length ?? 0;
        if (count > MaxEntries)
            throw new InvalidOperationException($"Vector clock cannot contain more than {MaxEntries} entries.");

        var requiredSize = checked(4 + (count * 10)); // 4 bytes for count + 10 bytes per entry

        if (destination.Length < requiredSize)
            throw new ArgumentException($"Destination must be at least {requiredSize} bytes", nameof(destination));

        BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)count);

        if (count == 0)
            return;

        var offset = 4;
        for (var i = 0; i < count; i++)
        {
            var nodeId = _nodeIds![i];
            var counter = _counters![i];

            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset, 2), nodeId);
            offset += 2;
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(offset, 8), counter);
            offset += 8;
        }
    }

    /// <summary>
    /// Gets the binary size required to serialize this vector clock.
    /// </summary>
    public int GetBinarySize()
    {
        var count = _nodeIds?.Length ?? 0;
        return checked(4 + (count * 10));
    }

    /// <summary>
    /// Reads a vector clock from its binary representation.
    /// Automatically deduplicates entries by taking the maximum counter value for duplicate node IDs.
    /// </summary>
    public static VectorClock ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < 4)
            throw new ArgumentException("Source must be at least 4 bytes", nameof(source));

        var count = BinaryPrimitives.ReadUInt32BigEndian(source);
        if (count > MaxEntries || count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(source), $"Vector clock entry count {count} exceeds max {MaxEntries}.");

        var countValue = (int)count;
        var requiredSize = checked(4 + (countValue * 10));

        if (source.Length < requiredSize)
            throw new ArgumentException($"Source must be at least {requiredSize} bytes for {count} entries", nameof(source));

        if (countValue == 0)
            return new VectorClock();

        var nodeIds = new ushort[countValue];
        var counters = new ulong[countValue];

        var offset = 4;
        for (var i = 0; i < countValue; i++)
        {
            nodeIds[i] = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, 2));
            offset += 2;
            counters[i] = BinaryPrimitives.ReadUInt64BigEndian(source.Slice(offset, 8));
            offset += 8;
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
            parts[i] = string.Concat(
                _nodeIds[i].ToString(CultureInfo.InvariantCulture),
                ":",
                _counters[i].ToString(CultureInfo.InvariantCulture));
        }
        return string.Join(',', parts);
    }

    /// <summary>
    /// Parses a vector clock from string format "node1:counter1,node2:counter2,..."
    /// </summary>
    public static VectorClock Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return new VectorClock();

        var entries = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
            return new VectorClock();

        var pairs = new List<(ushort nodeId, ulong counter)>();
        foreach (var entry in entries)
        {
            var parts = entry.Split(':', 2, StringSplitOptions.None);
            if (parts.Length != 2)
                throw new FormatException($"Invalid vector clock entry: {entry}");

            var nodeId = ushort.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
            var counter = ulong.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
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

        return new VectorClock([.. nodeIds], [.. counters]);
    }

    /// <summary>
    /// Tries to parse a vector clock from string format.
    /// </summary>
    public static bool TryParse(string? s, out VectorClock result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
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
