using Clockworks.Distributed;
using Clockworks.IntegrationDemo.Domain;
using Clockworks.IntegrationDemo.Infrastructure;

namespace Clockworks.IntegrationDemo.Services;

public sealed class OutboxDispatcher
{
    private readonly OutboxRepository _outbox;
    private readonly InMemoryQueue _queue;
    private readonly FailureInjector _failures;

    public OutboxDispatcher(OutboxRepository outbox, InMemoryQueue queue, FailureInjector failures)
    {
        _outbox = outbox;
        _queue = queue;
        _failures = failures;
    }

    public async Task<int> DispatchOnceAsync(long nowMs, CancellationToken ct)
    {
        var batch = await _outbox.DequeueAvailableAsync(nowMs, max: 32, ct);
        foreach (var row in batch)
        {
            if (_failures.ShouldDrop())
            {
                Console.WriteLine($"[dispatcher] DROP outbox msgId={row.Id:N} dest={row.Destination} type={row.MessageType}");
                await _outbox.DeleteAsync(row.Id, ct);
                continue;
            }

            var deliverAt = nowMs + _failures.AdditionalDelayMs();
            var queued = new InMemoryQueue.QueuedMessage(
                row.Destination,
                row.Id,
                row.MessageType,
                row.Json,
                row.HlcHeader,
                DeliverAtUtcMs: deliverAt);

            _queue.Enqueue(queued);

            if (_failures.ShouldDuplicate())
            {
                _queue.Enqueue(queued);
                Console.WriteLine($"[dispatcher] DUPLICATE outbox msgId={row.Id:N}");
            }

            await _outbox.DeleteAsync(row.Id, ct);
        }

        return batch.Count;
    }

    public static MessageEnvelope ToEnvelope(InMemoryQueue.QueuedMessage m)
    {
        var header = HlcMessageHeader.Parse(m.HlcHeader);
        return new MessageEnvelope(m.MessageId, m.MessageType, m.Json, header);
    }
}
