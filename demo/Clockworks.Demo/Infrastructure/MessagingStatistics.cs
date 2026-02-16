namespace Clockworks.Demo.Infrastructure;

internal sealed class MessagingStatistics
{
    private long _sent;
    private long _delivered;
    private long _dropped;
    private long _duplicated;
    private long _reordered;
    private long _deduped;
    private long _retriesScheduled;
    private long _maxInFlight;

    public long Sent => Volatile.Read(ref _sent);
    public long Delivered => Volatile.Read(ref _delivered);
    public long Dropped => Volatile.Read(ref _dropped);
    public long Duplicated => Volatile.Read(ref _duplicated);
    public long Reordered => Volatile.Read(ref _reordered);
    public long Deduped => Volatile.Read(ref _deduped);
    public long RetriesScheduled => Volatile.Read(ref _retriesScheduled);
    public long MaxInFlight => Volatile.Read(ref _maxInFlight);

    public void RecordSent(int inFlight)
    {
        Interlocked.Increment(ref _sent);
        InterlockedMax(ref _maxInFlight, inFlight);
    }

    public void RecordDelivered() => Interlocked.Increment(ref _delivered);

    public void RecordDropped() => Interlocked.Increment(ref _dropped);

    public void RecordDuplicated(int inFlight)
    {
        Interlocked.Increment(ref _duplicated);
        InterlockedMax(ref _maxInFlight, inFlight);
    }

    public void RecordReordered() => Interlocked.Increment(ref _reordered);

    public void RecordDeduped() => Interlocked.Increment(ref _deduped);

    public void RecordRetryScheduled() => Interlocked.Increment(ref _retriesScheduled);

    public void Reset()
    {
        Interlocked.Exchange(ref _sent, 0);
        Interlocked.Exchange(ref _delivered, 0);
        Interlocked.Exchange(ref _dropped, 0);
        Interlocked.Exchange(ref _duplicated, 0);
        Interlocked.Exchange(ref _reordered, 0);
        Interlocked.Exchange(ref _deduped, 0);
        Interlocked.Exchange(ref _retriesScheduled, 0);
        Interlocked.Exchange(ref _maxInFlight, 0);
    }

    public override string ToString() =>
        $"Sent={Sent} Delivered={Delivered} Dropped={Dropped} Duplicated={Duplicated} Reordered={Reordered} Deduped={Deduped} Retries={RetriesScheduled} MaxInFlight={MaxInFlight}";

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
}
