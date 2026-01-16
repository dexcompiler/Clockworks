using Clockworks;
using Clockworks.Distributed;
using Clockworks.IntegrationDemo.Domain;
using Clockworks.IntegrationDemo.Infrastructure;
using Clockworks.IntegrationDemo.Services;

namespace Clockworks.IntegrationDemo.Simulation;

public static class SimulatedRunner
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("Clockworks Integration Demo (ASP.NET Core + SQLite + Outbox/Inbox + HLC)");
        Console.WriteLine(new string('=', 78));
        Console.WriteLine();

        var tp = SimulatedTimeProvider.FromEpoch();
        AppClock.TimeProvider = tp;

        await using var store = await SqliteStore.OpenAsync("Data Source=:memory:;Cache=Shared", ct);

        var outbox = new OutboxRepository(store);
        var inbox = new InboxRepository(store);
        var orders = new OrderRepository(store);

        var queue = new InMemoryQueue();
        var failures = new FailureInjector
        {
            // a little noisy by default; keep fairly low
            DropRate = 0.02,
            DuplicateRate = 0.05,
            ReorderRate = 0.08,
            MaxAdditionalDelayMs = 200
        };

        using var ordersFactory = new HlcGuidFactory(tp, nodeId: 1);
        using var paymentsFactory = new HlcGuidFactory(tp, nodeId: 2);
        using var inventoryFactory = new HlcGuidFactory(tp, nodeId: 3);

        var ordersNode = new WorkflowNode("orders", new HlcCoordinator(ordersFactory), outbox, inbox, orders, queue);
        var paymentsNode = new WorkflowNode("payments", new HlcCoordinator(paymentsFactory), outbox, inbox, orders, queue);
        var inventoryNode = new WorkflowNode("inventory", new HlcCoordinator(inventoryFactory), outbox, inbox, orders, queue);

        var dispatcher = new OutboxDispatcher(outbox, queue, failures);
        var pump = new QueuePump(queue, failures, new[] { ordersNode, paymentsNode, inventoryNode });

        Console.WriteLine("Placing 3 orders...\n");
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var id = await ordersNode.PlaceOrderAsync(new PlaceOrderRequest($"cust-{i}", Amount: 10 + i), ct);
            ids.Add(id);
            tp.AdvanceMs(1);
        }

        Console.WriteLine();
        Console.WriteLine("Pumping simulated time and processing queue until orders confirm...\n");

        var remaining = new HashSet<Guid>(ids);

        for (var step = 0; step < 5_000 && remaining.Count > 0; step++)
        {
            var nowMs = tp.GetUtcNow().ToUnixTimeMilliseconds();

            _ = await dispatcher.DispatchOnceAsync(nowMs, ct);
            _ = await pump.ProcessOnceAsync(nowMs, ct);

            // advance time in small ticks so delays/timeouts are meaningful
            tp.AdvanceMs(10);

            if (step % 50 == 0)
            {
                foreach (var id in remaining.ToArray())
                {
                    var row = await orders.GetAsync(id, ct);
                    if (row?.Status == "Confirmed")
                    {
                        remaining.Remove(id);
                        Console.WriteLine($"[simulation] done orderId={id:N} status=Confirmed lastHlc={row.LastHlc}");
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Simulation complete. Confirmed={(ids.Count - remaining.Count)}/{ids.Count}");
    }
}
