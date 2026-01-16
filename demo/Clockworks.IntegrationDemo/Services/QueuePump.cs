using Clockworks.IntegrationDemo.Infrastructure;

namespace Clockworks.IntegrationDemo.Services;

public sealed class QueuePump
{
    private readonly InMemoryQueue _queue;
    private readonly FailureInjector _failures;
    private readonly IReadOnlyDictionary<string, WorkflowNode> _nodes;

    public QueuePump(InMemoryQueue queue, FailureInjector failures, IEnumerable<WorkflowNode> nodes)
    {
        _queue = queue;
        _failures = failures;
        _nodes = nodes.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<int> ProcessOnceAsync(long nowMs, CancellationToken ct)
    {
        // For demo simplicity: scan the queue in FIFO order and process messages that are due.
        // Reordering is simulated by occasionally re-enqueuing the message at the tail.
        if (!_queue.TryDequeue(out var msg))
            return 0;

        if (msg.DeliverAtUtcMs > nowMs)
        {
            _queue.Enqueue(msg);
            return 0;
        }

        if (_failures.ShouldReorder())
        {
            _queue.Enqueue(msg with { DeliverAtUtcMs = nowMs });
            Console.WriteLine($"[pump] REORDER msgId={msg.MessageId:N}");
            return 0;
        }

        if (!_nodes.TryGetValue(msg.Destination, out var node))
        {
            Console.WriteLine($"[pump] Unknown destination '{msg.Destination}' for msgId={msg.MessageId:N}");
            return 0;
        }

        var env = OutboxDispatcher.ToEnvelope(msg);
        var ok = await node.HandleAsync(env, ct);
        return ok ? 1 : 0;
    }
}
