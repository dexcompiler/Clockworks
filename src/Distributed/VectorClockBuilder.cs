using System.Buffers.Binary;

namespace Clockworks.Distributed;

internal sealed class VectorClockBuilder
{
    private const int MaxEntries = ushort.MaxValue + 1;

    private ushort[] _nodeIds;
    private ulong[] _counters;
    private int _count;

    public VectorClockBuilder(int initialCapacity = 4)
    {
        if (initialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));

        _nodeIds = initialCapacity == 0 ? [] : new ushort[initialCapacity];
        _counters = initialCapacity == 0 ? [] : new ulong[initialCapacity];
        _count = 0;
    }

    public bool IsEmpty => _count == 0;

    public void Reset(VectorClock snapshot)
    {
        var count = snapshot.Count;
        EnsureCapacity(count);
        _count = count;

        if (count == 0)
            return;

        snapshot.CopyTo(_nodeIds.AsSpan(0, count), _counters.AsSpan(0, count));
    }

    public void Increment(ushort nodeId)
    {
        if (_count == 0)
        {
            EnsureCapacity(1);
            _nodeIds[0] = nodeId;
            _counters[0] = 1;
            _count = 1;
            return;
        }

        var index = Array.BinarySearch(_nodeIds, 0, _count, nodeId);
        if (index >= 0)
        {
            _counters[index]++;
            return;
        }

        if (_count >= MaxEntries)
            throw new InvalidOperationException($"Cannot increment: vector clock at max capacity ({MaxEntries})");

        var insertIndex = ~index;
        EnsureCapacity(_count + 1);

        if (insertIndex < _count)
        {
            Array.Copy(_nodeIds, insertIndex, _nodeIds, insertIndex + 1, _count - insertIndex);
            Array.Copy(_counters, insertIndex, _counters, insertIndex + 1, _count - insertIndex);
        }

        _nodeIds[insertIndex] = nodeId;
        _counters[insertIndex] = 1;
        _count++;
    }

    public void Merge(VectorClock other)
    {
        if (other.IsEmpty)
            return;

        // Decode directly from canonical binary format to avoid reliance on VectorClock internals.
        // This is linear in number of entries and does not allocate per entry.
        var size = other.GetBinarySize();
        byte[]? pooled = null;
        Span<byte> buffer = size <= 1024 ? stackalloc byte[size] : (pooled = new byte[size]);

        other.WriteTo(buffer);

        var count = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        var offset = 4;
        for (var i = 0; i < count; i++)
        {
            var nodeId = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
            offset += 2;
            var counter = BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(offset, 8));
            offset += 8;

            MergeEntry(nodeId, counter);
        }
    }

    public VectorClock ToSnapshot()
    {
        if (_count == 0)
            return new VectorClock();

        var nodeIds = new ushort[_count];
        var counters = new ulong[_count];
        Array.Copy(_nodeIds, nodeIds, _count);
        Array.Copy(_counters, counters, _count);
        return VectorClock.CreateUnsafe(nodeIds, counters);
    }

    private void MergeEntry(ushort nodeId, ulong counter)
    {
        if (_count == 0)
        {
            EnsureCapacity(1);
            _nodeIds[0] = nodeId;
            _counters[0] = counter;
            _count = 1;
            return;
        }

        var index = Array.BinarySearch(_nodeIds, 0, _count, nodeId);
        if (index >= 0)
        {
            if (counter > _counters[index])
                _counters[index] = counter;
            return;
        }

        if (_count >= MaxEntries)
            throw new InvalidOperationException($"Cannot merge: vector clock at max capacity ({MaxEntries})");

        var insertIndex = ~index;
        EnsureCapacity(_count + 1);

        if (insertIndex < _count)
        {
            Array.Copy(_nodeIds, insertIndex, _nodeIds, insertIndex + 1, _count - insertIndex);
            Array.Copy(_counters, insertIndex, _counters, insertIndex + 1, _count - insertIndex);
        }

        _nodeIds[insertIndex] = nodeId;
        _counters[insertIndex] = counter;
        _count++;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed > MaxEntries)
            throw new InvalidOperationException($"Vector clock cannot contain more than {MaxEntries} entries.");

        if (_nodeIds.Length >= needed)
            return;

        var next = Math.Max(needed, _nodeIds.Length == 0 ? 4 : _nodeIds.Length * 2);
        if (next > MaxEntries)
            next = MaxEntries;

        Array.Resize(ref _nodeIds, next);
        Array.Resize(ref _counters, next);
    }
}
