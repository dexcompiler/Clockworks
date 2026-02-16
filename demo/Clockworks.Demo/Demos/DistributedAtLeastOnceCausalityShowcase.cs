using Clockworks.Distributed;
using Clockworks.Instrumentation;
using Clockworks.Demo.Infrastructure;
using System.Collections.Concurrent;
using static System.Console;

namespace Clockworks.Demo.Demos;

internal static class DistributedAtLeastOnceCausalityShowcase
{
    public static async Task Run(string[] args)
    {
        WriteLine("Distributed simulation: at-least-once delivery + idempotency + HLC + vector clocks");
        WriteLine(new string('=', 86));
        WriteLine();
        WriteLine("This is a single-process deterministic simulation driven by SimulatedTimeProvider.");
        WriteLine("TODO: add a multi-process mode (multiple OS processes) to demonstrate non-determinism and real scheduling effects.");
        WriteLine();

        var options = SimulationOptions.Parse(args);

        var tp = new SimulatedTimeProvider(DateTimeOffset.UtcNow);
        var failures = new FailureInjector(options.Seed)
        {
            DropRate = options.DropRate,
            DuplicateRate = options.DuplicateRate,
            ReorderRate = options.ReorderRate,
            MaxAdditionalDelayMs = options.MaxAdditionalDelayMs,
        };

        var network = new SimulatedNetwork(tp, failures);

        var orders = new Node("orders", nodeId: 1, tp, network);
        var payments = new Node("payments", nodeId: 2, tp, network);
        var inventory = new Node("inventory", nodeId: 3, tp, network);

        network.Register(orders);
        network.Register(payments);
        network.Register(inventory);

        WriteLine("Fault injection:");
        WriteLine($"  drop={options.DropRate:P1} dup={options.DuplicateRate:P1} reorder={options.ReorderRate:P1} maxDelayMs={options.MaxAdditionalDelayMs}");
        WriteLine();

        WriteLine("Scenario:");
        WriteLine("  1) orders emits PlaceOrder (root event)");
        WriteLine("  2) payments + inventory process it idempotently and reply with acks");
        WriteLine("  3) orders waits for both acks; retries if acks don't arrive (Timeouts)");
        WriteLine("  4) all messages carry both an HLC header and a VectorClock header");
        WriteLine();

        // fire a few orders to generate concurrency and reorderings
        for (var i = 0; i < options.Orders; i++)
        {
            orders.StartNewOrder($"order-{i}");
            tp.AdvanceMs(1);
        }

        WriteLine("Running simulation...");
        WriteLine();

        for (var step = 0; step < options.MaxSteps; step++)
        {
            network.DeliverDue();

            orders.Poll();
            payments.Poll();
            inventory.Poll();

            tp.AdvanceMs(options.TickMs);

            if (step % options.PrintEverySteps == 0)
            {
                PrintSnapshot(tp, network, orders, payments, inventory);
            }

            if (orders.IsDone && network.InFlightCount == 0)
            {
                break;
            }

            await Task.Yield(); 
        }

        WriteLine();
        WriteLine("Final snapshot:");
        PrintSnapshot(tp, network, orders, payments, inventory);
    }

    private static void PrintSnapshot(
        SimulatedTimeProvider tp,
        SimulatedNetwork network,
        params Node[] nodes)
    {
        WriteLine($"t={tp.GetUtcNow():O} inFlight={network.InFlightCount} nextDueMs={network.NextDueUtcMs?.ToString() ?? "-"}");

        var stp = tp.Statistics;
        WriteLine($"  SimulatedTimeProvider: TimersCreated={stp.TimersCreated} TimerChanges={stp.TimerChanges} TimersDisposed={stp.TimersDisposed} CallbacksFired={stp.CallbacksFired} MaxQueueLength={stp.MaxQueueLength} QueueEnqueues={stp.QueueEnqueues} AdvanceCalls={stp.AdvanceCalls}");
        WriteLine($"  Network:               {network.Stats} ConcurrentPairs={network.ConcurrentEventPairs} Sample={(network.LastConcurrencySample ?? "-")}");
        foreach (var n in nodes)
        {
            WriteLine($"  Node {n.Name}:");
            WriteLine($"    HLC:    {n.Hlc.Statistics}");
            WriteLine($"    VC:     {n.Vector.Statistics}");
            WriteLine($"    Timeouts Created={n.TimeoutStats.Created} Fired={n.TimeoutStats.Fired} Disposed={n.TimeoutStats.Disposed}");
            WriteLine($"    Inbox:  processed={n.ProcessedCount} deduped={n.DedupedCount}");
        }

        WriteLine();
    }

    private sealed class Node
    {
        private readonly SimulatedNetwork _network;
        private readonly TimeProvider _tp;
        private readonly ConcurrentDictionary<Guid, byte> _inbox = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _acks = new();
        private readonly ConcurrentDictionary<string, Timeouts.TimeoutHandle> _retries = new();
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; }
        public HlcCoordinator Hlc { get; }
        public VectorClockCoordinator Vector { get; }
        public TimeoutStatistics TimeoutStats { get; } = new();

        private long _processed;
        private long _deduped;

        public long ProcessedCount => Volatile.Read(ref _processed);
        public long DedupedCount => Volatile.Read(ref _deduped);

        public Node(string name, ushort nodeId, SimulatedTimeProvider tp, SimulatedNetwork network)
        {
            Name = name;
            _network = network;
            _tp = tp;
            var factory = new HlcGuidFactory(tp, nodeId);
            Hlc = new HlcCoordinator(factory);
            Vector = new VectorClockCoordinator(nodeId);
        }

        public bool IsDone => _acks.IsEmpty && _retries.IsEmpty;

        public void StartNewOrder(string orderId)
        {
            var correlationId = Guid.CreateVersion7();
            Vector.NewLocalEvent();
            var vc = Vector.BeforeSend();
            var ts = Hlc.BeforeSend();

            _acks[orderId] = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            _attempts[orderId] = 1;

            Send(new Message(
                Kind: MessageKind.PlaceOrder,
                OrderId: orderId,
                From: Name,
                To: "payments",
                CorrelationId: correlationId,
                Hlc: ts,
                VectorClock: vc,
                Attempt: 1));

            Send(new Message(
                Kind: MessageKind.PlaceOrder,
                OrderId: orderId,
                From: Name,
                To: "inventory",
                CorrelationId: correlationId,
                Hlc: ts,
                VectorClock: vc,
                Attempt: 1));

            ScheduleRetry(orderId, attempt: 1);
        }

        public void OnReceive(Message msg)
        {
            if (!_inbox.TryAdd(msg.MessageId, 0))
            {
                Interlocked.Increment(ref _deduped);
                return;
            }

            Interlocked.Increment(ref _processed);

            Hlc.BeforeReceive(msg.Hlc);
            Vector.BeforeReceive(msg.VectorClock);

            switch (msg.Kind)
            {
                case MessageKind.PlaceOrder:
                    HandlePlaceOrder(msg);
                    break;
                case MessageKind.Ack:
                    HandleAck(msg);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown kind: {msg.Kind}");
            }
        }

        private void HandlePlaceOrder(Message msg)
        {
            // idempotent processing is enforced by the inbox above
            Vector.NewLocalEvent();
            var vc = Vector.BeforeSend();
            var ts = Hlc.BeforeSend();

            Send(new Message(
                Kind: MessageKind.Ack,
                OrderId: msg.OrderId,
                From: Name,
                To: msg.From,
                CorrelationId: msg.CorrelationId,
                Hlc: ts,
                VectorClock: vc,
                Attempt: 1));
        }

        private void HandleAck(Message msg)
        {
            if (!_acks.TryGetValue(msg.OrderId, out var fromSet))
            {
                return;
            }

            fromSet.TryAdd(msg.From, 0);

            if (fromSet.ContainsKey("payments") && fromSet.ContainsKey("inventory"))
            {
                if (_retries.TryRemove(msg.OrderId, out var handle))
                {
                    handle.Dispose();
                }

                _acks.TryRemove(msg.OrderId, out _);
                _attempts.TryRemove(msg.OrderId, out _);
            }
        }

        public void Poll()
        {
            foreach (var (orderId, handle) in _retries)
            {
                if (!handle.Token.IsCancellationRequested)
                    continue;

                TryRetry(orderId);
            }
        }

        private void ScheduleRetry(string orderId, int attempt)
        {
            if (_retries.ContainsKey(orderId))
            {
                return;
            }

            var handle = Timeouts.CreateTimeoutHandle(_tp, TimeSpan.FromMilliseconds(50), statistics: TimeoutStats);
            if (!_retries.TryAdd(orderId, handle))
            {
                handle.Dispose();
                return;
            }
        }

        private void TryRetry(string orderId)
        {
            if (!_acks.TryGetValue(orderId, out var fromSet))
            {
                CleanupRetry(orderId);
                return;
            }

            if (fromSet.ContainsKey("payments") && fromSet.ContainsKey("inventory"))
            {
                CleanupRetry(orderId);
                return;
            }

            var nextAttempt = _attempts.AddOrUpdate(orderId, 2, static (_, current) => current + 1);
            if (nextAttempt > 10)
            {
                CleanupRetry(orderId);
                return;
            }

            Vector.NewLocalEvent();
            var vc = Vector.BeforeSend();
            var ts = Hlc.BeforeSend();
            var correlationId = Guid.CreateVersion7();

            Send(new Message(
                Kind: MessageKind.PlaceOrder,
                OrderId: orderId,
                From: Name,
                To: "payments",
                CorrelationId: correlationId,
                Hlc: ts,
                VectorClock: vc,
                Attempt: nextAttempt));

            Send(new Message(
                Kind: MessageKind.PlaceOrder,
                OrderId: orderId,
                From: Name,
                To: "inventory",
                CorrelationId: correlationId,
                Hlc: ts,
                VectorClock: vc,
                Attempt: nextAttempt));

            CleanupRetry(orderId);
            ScheduleRetry(orderId, nextAttempt);
        }

        private void CleanupRetry(string orderId)
        {
            if (_retries.TryRemove(orderId, out var existing))
            {
                existing.Dispose();
            }
        }

        private void Send(Message msg) => _network.Send(msg);
    }

    private enum MessageKind
    {
        PlaceOrder,
        Ack,
    }

    private sealed record Message(
        MessageKind Kind,
        string OrderId,
        string From,
        string To,
        Guid CorrelationId,
        HlcTimestamp Hlc,
        VectorClock VectorClock,
        int Attempt)
    {
        public Guid MessageId { get; init; } = Guid.CreateVersion7();
        public Guid EventId { get; init; } = Guid.CreateVersion7();
    }

    private sealed class SimulationOptions
    {
        public int Orders { get; set; } = 5;
        public int MaxSteps { get; set; } = 2_000;
        public int TickMs { get; set; } = 5;
        public int PrintEverySteps { get; set; } = 50;
        public int Seed { get; set; } = 123;

        public double DropRate { get; set; } = 0.02;
        public double DuplicateRate { get; set; } = 0.05;
        public double ReorderRate { get; set; } = 0.08;
        public int MaxAdditionalDelayMs { get; set; } = 200;

        public static SimulationOptions Parse(string[] args)
        {
            var o = new SimulationOptions();

            foreach (var arg in args)
            {
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                    continue;

                var i = arg.IndexOf('=');
                if (i < 0)
                    continue;

                var key = arg[2..i];
                var value = arg[(i + 1)..];

                switch (key)
                {
                    case "orders":
                        if (int.TryParse(value, out var orders)) o.Orders = orders;
                        break;
                    case "maxSteps":
                        if (int.TryParse(value, out var maxSteps)) o.MaxSteps = maxSteps;
                        break;
                    case "tickMs":
                        if (int.TryParse(value, out var tickMs)) o.TickMs = tickMs;
                        break;
                    case "printEvery":
                        if (int.TryParse(value, out var printEvery)) o.PrintEverySteps = printEvery;
                        break;
                    case "seed":
                        if (int.TryParse(value, out var seed)) o.Seed = seed;
                        break;
                    case "drop":
                        if (double.TryParse(value, out var drop)) o.DropRate = drop;
                        break;
                    case "dup":
                        if (double.TryParse(value, out var dup)) o.DuplicateRate = dup;
                        break;
                    case "reorder":
                        if (double.TryParse(value, out var reorder)) o.ReorderRate = reorder;
                        break;
                    case "maxDelayMs":
                        if (int.TryParse(value, out var maxDelayMs)) o.MaxAdditionalDelayMs = maxDelayMs;
                        break;
                }
            }

            return o;
        }
    }

    private sealed class FailureInjector(int seed)
    {
        private readonly Random _random = new(seed);

        public double DropRate { get; set; }
        public double DuplicateRate { get; set; }
        public double ReorderRate { get; set; }
        public int MaxAdditionalDelayMs { get; set; }

        public bool ShouldDrop() => _random.NextDouble() < DropRate;
        public bool ShouldDuplicate() => _random.NextDouble() < DuplicateRate;
        public bool ShouldReorder() => _random.NextDouble() < ReorderRate;

        public int AdditionalDelayMs() => MaxAdditionalDelayMs <= 0 
            ? 0 
            : _random.Next(0, MaxAdditionalDelayMs + 1);
    }

    private sealed class SimulatedNetwork(SimulatedTimeProvider tp, FailureInjector failures)
    {
        private readonly Lock _lock = new();
        private readonly SimulatedTimeProvider _tp = tp;
        private readonly FailureInjector _failures = failures;
        private readonly ConcurrentDictionary<string, Node> _nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Envelope> _inFlight = [];

        public MessagingStatistics Stats { get; } = new();

        private long _concurrentEventPairs;
        private volatile string? _lastConcurrencySample;

        public long ConcurrentEventPairs => Volatile.Read(ref _concurrentEventPairs);
        public string? LastConcurrencySample => _lastConcurrencySample;

        private readonly Queue<DeliveredEvent> _recentDelivered = new();
        private const int RecentWindowSize = 64;

        public int InFlightCount
        {
            get
            {
                lock (_lock)
                {
                    return _inFlight.Count;
                }
            }
        }

        public long? NextDueUtcMs
        {
            get
            {
                lock (_lock)
                {
                    if (_inFlight.Count == 0)
                        return null;
                    return _inFlight.Min(m => m.DeliverAtUtcMs);
                }
            }
        }

        public void Register(Node node) => _nodes[node.Name] = node;

        public void Send(Message msg)
        {
            var now = _tp.GetUtcNow().ToUnixTimeMilliseconds();

            if (_failures.ShouldDrop())
            {
                Stats.RecordDropped();
                return;
            }

            var additional = _failures.AdditionalDelayMs();
            var deliverAt = now + additional;

            lock (_lock)
            {
                _inFlight.Add(new Envelope(msg, deliverAt));

                if (_failures.ShouldDuplicate())
                {
                    _inFlight.Add(new Envelope(msg with { }, deliverAt + _failures.AdditionalDelayMs()));
                    Stats.RecordDuplicated(_inFlight.Count);
                }

                Stats.RecordSent(_inFlight.Count);

                if (_failures.ShouldReorder() && _inFlight.Count >= 2)
                {
                    var i = _inFlight.Count - 1;
                    var j = Math.Max(0, i - 1);
                    (_inFlight[i], _inFlight[j]) = (_inFlight[j], _inFlight[i]);
                    Stats.RecordReordered();
                }
            }
        }

        public void DeliverDue()
        {
            var now = _tp.GetUtcNow().ToUnixTimeMilliseconds();

            List<Envelope> due;
            lock (_lock)
            {
                if (_inFlight.Count == 0)
                    return;

                due = _inFlight.Where(m => m.DeliverAtUtcMs <= now).ToList();
                if (due.Count == 0)
                    return;

                foreach (var m in due)
                {
                    _inFlight.Remove(m);
                }
            }

            foreach (var env in due)
            {
                if (_nodes.TryGetValue(env.Message.To, out var node))
                {
                    node.OnReceive(env.Message);
                    Stats.RecordDelivered();

                    ObserveConcurrency(env.Message);
                }
            }
        }

        private void ObserveConcurrency(Message delivered)
        {
            lock (_lock)
            {
                foreach (var prev in _recentDelivered)
                {
                    if (prev.EventId == delivered.EventId)
                        continue;

                    if (prev.VectorClock.IsConcurrentWith(delivered.VectorClock))
                    {
                        _concurrentEventPairs++;
                        _lastConcurrencySample = $"{prev.From}->{prev.To}({prev.Kind}) || {delivered.From}->{delivered.To}({delivered.Kind})";
                        break;
                    }
                }

                _recentDelivered.Enqueue(new DeliveredEvent(
                    delivered.EventId,
                    delivered.Kind,
                    delivered.From,
                    delivered.To,
                    delivered.VectorClock));

                while (_recentDelivered.Count > RecentWindowSize)
                {
                    _recentDelivered.Dequeue();
                }
            }
        }

        private sealed record Envelope(Message Message, long DeliverAtUtcMs);

        private sealed record DeliveredEvent(Guid EventId, MessageKind Kind, string From, string To, VectorClock VectorClock);
    }
}
