using System.Text.Json;
using Clockworks;
using Clockworks.Distributed;
using Clockworks.IntegrationDemo.Domain;
using Clockworks.IntegrationDemo.Infrastructure;

namespace Clockworks.IntegrationDemo.Services;

public sealed class WorkflowNode
{
    private readonly string _name;
    private readonly HlcCoordinator _hlc;
    private readonly OutboxRepository _outbox;
    private readonly InboxRepository _inbox;
    private readonly OrderRepository _orders;
    private readonly InMemoryQueue _queue;

    public WorkflowNode(
        string name,
        HlcCoordinator hlc,
        OutboxRepository outbox,
        InboxRepository inbox,
        OrderRepository orders,
        InMemoryQueue queue)
    {
        _name = name;
        _hlc = hlc;
        _outbox = outbox;
        _inbox = inbox;
        _orders = orders;
        _queue = queue;
    }

    public string Name => _name;

    public async Task<Guid> PlaceOrderAsync(PlaceOrderRequest req, CancellationToken ct)
    {
        var now = AppClock.TimeProvider.GetUtcNow();
        var ts = _hlc.BeforeSend();

        var orderId = Guid.CreateVersion7(now);
        var msgId = Guid.CreateVersion7(now);

        var evt = new OrderPlaced(orderId, req.CustomerId, req.Amount);
        var json = JsonSerializer.Serialize(evt);

        var header = new HlcMessageHeader(ts, correlationId: orderId, causationId: null);

        await _orders.UpsertPlacedAsync(orderId, req.CustomerId, req.Amount, header.ToString(), ct);
        await _outbox.EnqueueAsync(destination: "payments", msgId, nameof(OrderPlaced), json, header,
            nowMs: now.ToUnixTimeMilliseconds(),
            availableMs: now.ToUnixTimeMilliseconds(),
            ct);

        Console.WriteLine($"[{_name}] PlaceOrder orderId={orderId:N} hlc={ts}");
        return orderId;
    }

    public async Task<bool> HandleAsync(MessageEnvelope env, CancellationToken ct)
    {
        var nowMs = AppClock.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        // Idempotency
        var firstTime = await _inbox.TryMarkReceivedAsync(env.MessageId, env.MessageType, env.Header.ToString(), nowMs, ct);
        if (!firstTime)
        {
            Console.WriteLine($"[{_name}] DUPLICATE ignored messageId={env.MessageId:N} type={env.MessageType}");
            return true;
        }

        _hlc.BeforeReceive(env.Header.Timestamp);

        Console.WriteLine($"[{_name}] recv type={env.MessageType} msgId={env.MessageId:N} hlc={env.Header.Timestamp}");

        switch (env.MessageType)
        {
            case nameof(OrderPlaced):
            {
                var msg = JsonSerializer.Deserialize<OrderPlaced>(env.Json)!;
                await _orders.UpsertPlacedAsync(msg.OrderId, msg.CustomerId, msg.Amount, env.Header.ToString(), ct);

                // send PaymentReserved
                var ts = _hlc.BeforeSend();
                var pay = new PaymentReserved(msg.OrderId, Success: true);
                var json = JsonSerializer.Serialize(pay);
                var header = new HlcMessageHeader(ts, correlationId: env.Header.CorrelationId, causationId: env.MessageId);

                var msgId = Guid.CreateVersion7(AppClock.TimeProvider.GetUtcNow());
                await _outbox.EnqueueAsync("inventory", msgId, nameof(PaymentReserved), json, header,
                    nowMs: nowMs,
                    availableMs: nowMs,
                    ct);

                Console.WriteLine($"[{_name}] -> outbox PaymentReserved orderId={msg.OrderId:N} hlc={ts}");
                return true;
            }

            case nameof(PaymentReserved):
            {
                var msg = JsonSerializer.Deserialize<PaymentReserved>(env.Json)!;
                await _orders.UpdateStatusAsync(msg.OrderId, "PaymentReserved", env.Header.ToString(), ct);

                var ts = _hlc.BeforeSend();
                var alloc = new InventoryAllocated(msg.OrderId, Success: true);
                var json = JsonSerializer.Serialize(alloc);
                var header = new HlcMessageHeader(ts, correlationId: env.Header.CorrelationId, causationId: env.MessageId);
                var msgId = Guid.CreateVersion7(AppClock.TimeProvider.GetUtcNow());

                await _outbox.EnqueueAsync("orders", msgId, nameof(InventoryAllocated), json, header,
                    nowMs: nowMs,
                    availableMs: nowMs,
                    ct);

                Console.WriteLine($"[{_name}] -> outbox InventoryAllocated orderId={msg.OrderId:N} hlc={ts}");
                return true;
            }

            case nameof(InventoryAllocated):
            {
                var msg = JsonSerializer.Deserialize<InventoryAllocated>(env.Json)!;
                await _orders.UpdateStatusAsync(msg.OrderId, "InventoryAllocated", env.Header.ToString(), ct);

                var ts = _hlc.BeforeSend();
                var conf = new OrderConfirmed(msg.OrderId);
                var json = JsonSerializer.Serialize(conf);
                var header = new HlcMessageHeader(ts, correlationId: env.Header.CorrelationId, causationId: env.MessageId);
                var msgId = Guid.CreateVersion7(AppClock.TimeProvider.GetUtcNow());

                await _outbox.EnqueueAsync("orders", msgId, nameof(OrderConfirmed), json, header,
                    nowMs: nowMs,
                    availableMs: nowMs,
                    ct);

                Console.WriteLine($"[{_name}] -> outbox OrderConfirmed orderId={msg.OrderId:N} hlc={ts}");
                return true;
            }

            case nameof(OrderConfirmed):
            {
                var msg = JsonSerializer.Deserialize<OrderConfirmed>(env.Json)!;
                await _orders.UpdateStatusAsync(msg.OrderId, "Confirmed", env.Header.ToString(), ct);
                Console.WriteLine($"[{_name}] CONFIRMED orderId={msg.OrderId:N}");
                return true;
            }

            default:
                Console.WriteLine($"[{_name}] Unknown message type: {env.MessageType}");
                return false;
        }
    }
}
