using System.Collections.Concurrent;

namespace Clockworks.IntegrationDemo.Infrastructure;

public sealed class InMemoryQueue
{
    private readonly ConcurrentQueue<QueuedMessage> _queue = new();

    public void Enqueue(QueuedMessage msg) => _queue.Enqueue(msg);

    public bool TryDequeue(out QueuedMessage msg) => _queue.TryDequeue(out msg);

    public int Count => _queue.Count;

    public sealed record QueuedMessage(
        string Destination,
        Guid MessageId,
        string MessageType,
        string Json,
        string HlcHeader,
        long DeliverAtUtcMs);
}
