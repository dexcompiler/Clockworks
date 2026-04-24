using Clockworks.Distributed;
using Clockworks.Instrumentation;
using Clockworks.Demo.Infrastructure;
using System.Diagnostics;

namespace Clockworks.Demo.Workloads;

public sealed class OrderPipelineWorkloadScenario : IWorkloadScenario
{
    private readonly OrderPipelineSimulationOptions _options;

    public OrderPipelineWorkloadScenario(OrderPipelineSimulationOptions? options = null)
    {
        _options = options ?? new OrderPipelineSimulationOptions(
            Orders: 250,
            MaxSteps: 40_000,
            TickMs: 5,
            RetryAfterMs: 30,
            DropRate: 0.03,
            DuplicateRate: 0.08,
            ReorderRate: 0.08,
            MaxAdditionalDelayMs: 35,
            DedupeRetentionLimit: 1_024,
            PruneEvery: 64);
    }

    public string Name => "order-pipeline";
    public string Description => "High-volume at-least-once order pipeline with retries, HLC, vector clocks, and fault injection.";
    public WorkloadRuntimeMode SupportedModes => WorkloadRuntimeMode.Both;

    public async Task<WorkloadResult> ExecuteAsync(WorkloadExecutionContext context, CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        var simulation = new OrderPipelineSimulation(
            Name,
            _options,
            context.RuntimeMode,
            context.Seed);

        var snapshot = await simulation.RunAsync(cancellationToken);

        sw.Stop();
        var metrics = new[]
        {
            new WorkloadMetric("confirmed-rate", snapshot.ConfirmedRate, "ratio", HigherIsBetter: true),
            new WorkloadMetric("runtime", sw.Elapsed.TotalMilliseconds, "ms"),
            new WorkloadMetric("retries-scheduled", snapshot.NetworkStats.RetriesScheduled, "count"),
            new WorkloadMetric("duplicates-deduped", snapshot.NetworkStats.Deduped, "count", HigherIsBetter: true),
            new WorkloadMetric("max-in-flight", snapshot.NetworkStats.MaxInFlight, "messages"),
            new WorkloadMetric("simulated-max-queue-length", snapshot.MaxQueueLength, "timers"),
            new WorkloadMetric("causal-violations", snapshot.CausalViolations, "count"),
            new WorkloadMetric("max-observed-drift", snapshot.MaxObservedDriftMs, "ms"),
            new WorkloadMetric("vector-merges", snapshot.TotalVectorMerges, "count", HigherIsBetter: true)
        };

        var invariants = new[]
        {
            new WorkloadInvariant("all-orders-confirmed", snapshot.UnconfirmedOrders == 0, $"{snapshot.ConfirmedOrders}/{_options.Orders} orders confirmed."),
            new WorkloadInvariant("causal-ordering-preserved", snapshot.CausalViolations == 0, $"Observed {snapshot.CausalViolations} causal violations across request/ack exchanges."),
            new WorkloadInvariant("network-drained", snapshot.InFlightCount == 0, $"Simulation ended with {snapshot.InFlightCount} messages still in flight."),
            new WorkloadInvariant("deterministic-replay-stable", context.RuntimeMode != WorkloadRuntimeMode.Simulated || snapshot.DeterministicReplayHash != 0, $"Replay hash={snapshot.DeterministicReplayHash}.")
        };

        return WorkloadCommand.FinalizeResult(
            Name,
            context.RuntimeMode,
            context.Seed,
            snapshot.UnconfirmedOrders == 0
                ? $"Confirmed {_options.Orders} orders with {snapshot.NetworkStats.RetriesScheduled} retries under injected faults."
                : $"{snapshot.UnconfirmedOrders} orders remained unconfirmed after {_options.MaxSteps} steps.",
            sw.Elapsed,
            metrics,
            invariants,
            context.Baselines,
            startedUtc,
            DateTimeOffset.UtcNow,
            $"dotnet run --project demo/Clockworks.Demo -- workloads {Name} --mode {context.RuntimeMode} --seed {context.Seed}");
    }
}

public sealed class TimerTimeoutStormWorkloadScenario : IWorkloadScenario
{
    private readonly int _timeoutCount;
    private readonly int _periodicTimerCount;
    private readonly int _burstRescheduleCount;
    private readonly int _steps;
    private readonly int _tickMs;

    public TimerTimeoutStormWorkloadScenario(
        int timeoutCount = 6_000,
        int periodicTimerCount = 800,
        int burstRescheduleCount = 1_200,
        int steps = 900,
        int tickMs = 5)
    {
        _timeoutCount = timeoutCount;
        _periodicTimerCount = periodicTimerCount;
        _burstRescheduleCount = burstRescheduleCount;
        _steps = steps;
        _tickMs = tickMs;
    }

    public string Name => "timer-timeout-storm";
    public string Description => "Timer and timeout storm with bursty reschedules and periodic coalescing pressure.";
    public WorkloadRuntimeMode SupportedModes => WorkloadRuntimeMode.Simulated;

    public Task<WorkloadResult> ExecuteAsync(WorkloadExecutionContext context, CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        var tp = new SimulatedTimeProvider(DateTimeOffset.UnixEpoch);
        var stats = tp.Statistics;
        var timeoutStats = new TimeoutStatistics();
        var rng = new Random(context.Seed);
        var periodicCallbacks = 0;
        var rescheduled = 0;

        using var scope = new DisposableBag();

        for (var i = 0; i < _timeoutCount; i++)
        {
            var dueMs = rng.Next(1, _steps * _tickMs);
            var handle = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromMilliseconds(dueMs), timeoutStats);
            scope.Add(handle);
        }

        var periodicTimers = new List<ITimer>(_periodicTimerCount);
        for (var i = 0; i < _periodicTimerCount; i++)
        {
            var dueMs = rng.Next(1, 25);
            var periodMs = rng.Next(10, 40);
            var timer = tp.CreateTimer(
                static state => ((Action)state!).Invoke(),
                () => { Interlocked.Increment(ref periodicCallbacks); },
                TimeSpan.FromMilliseconds(dueMs),
                TimeSpan.FromMilliseconds(periodMs));
            periodicTimers.Add(timer);
            scope.Add(timer);
        }

        for (var step = 0; step < _steps; step++)
        {
            tp.AdvanceMs(_tickMs);

            if (step > 0 && step % Math.Max(1, _steps / 6) == 0)
            {
                for (var i = 0; i < _burstRescheduleCount && periodicTimers.Count > 0; i++)
                {
                    var timer = periodicTimers[rng.Next(periodicTimers.Count)];
                    timer.Change(
                        TimeSpan.FromMilliseconds(rng.Next(1, 20)),
                        TimeSpan.FromMilliseconds(rng.Next(8, 35)));
                    rescheduled++;
                }
            }
        }

        tp.AdvanceMs(_tickMs * 4L);

        sw.Stop();

        var firedTimeouts = (int)timeoutStats.Fired;
        var expectedTimeouts = _timeoutCount;
        var completionRate = expectedTimeouts == 0 ? 1d : firedTimeouts / (double)expectedTimeouts;

        var invariants = new[]
        {
            new WorkloadInvariant("all-timeouts-fired", firedTimeouts == expectedTimeouts, $"{firedTimeouts}/{expectedTimeouts} timeout callbacks fired."),
            new WorkloadInvariant("storm-produced-callbacks", periodicCallbacks > 0, $"Observed {periodicCallbacks} periodic callbacks."),
            new WorkloadInvariant("queue-pressure-observed", stats.MaxQueueLength >= _periodicTimerCount, $"Max queue length {stats.MaxQueueLength}.")
        };

        var metrics = new[]
        {
            new WorkloadMetric("completion-rate", completionRate, "ratio", HigherIsBetter: true),
            new WorkloadMetric("runtime", sw.Elapsed.TotalMilliseconds, "ms"),
            new WorkloadMetric("periodic-callbacks", periodicCallbacks, "count", HigherIsBetter: true),
            new WorkloadMetric("timeouts-fired", firedTimeouts, "count", HigherIsBetter: true),
            new WorkloadMetric("reschedules", rescheduled, "count", HigherIsBetter: true),
            new WorkloadMetric("max-queue-length", stats.MaxQueueLength, "timers"),
            new WorkloadMetric("callbacks-fired", stats.CallbacksFired, "count", HigherIsBetter: true),
            new WorkloadMetric("advance-calls", stats.AdvanceCalls, "count")
        };

        return Task.FromResult(WorkloadCommand.FinalizeResult(
            Name,
            context.RuntimeMode,
            context.Seed,
            $"Fired {firedTimeouts} timeouts and {periodicCallbacks} periodic callbacks with queue peak {stats.MaxQueueLength}.",
            sw.Elapsed,
            metrics,
            invariants,
            context.Baselines,
            startedUtc,
            DateTimeOffset.UtcNow,
            $"dotnet run --project demo/Clockworks.Demo -- workloads {Name} --seed {context.Seed}"));
    }
}

public sealed class UuidHlcHotPathWorkloadScenario : IWorkloadScenario
{
    private readonly int _uuidThreads;
    private readonly int _uuidPerThread;
    private readonly int _hlcNodes;
    private readonly int _hlcEventsPerNode;

    public UuidHlcHotPathWorkloadScenario(
        int uuidThreads = 4,
        int uuidPerThread = 20_000,
        int hlcNodes = 4,
        int hlcEventsPerNode = 5_000)
    {
        _uuidThreads = uuidThreads;
        _uuidPerThread = uuidPerThread;
        _hlcNodes = hlcNodes;
        _hlcEventsPerNode = hlcEventsPerNode;
    }

    public string Name => "uuid-hlc-hot-path";
    public string Description => "High-rate UUIDv7 and HLC generation across threads and nodes with overflow/drift stress.";
    public WorkloadRuntimeMode SupportedModes => WorkloadRuntimeMode.Both;

    public async Task<WorkloadResult> ExecuteAsync(WorkloadExecutionContext context, CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        var timeProvider = context.RuntimeMode == WorkloadRuntimeMode.Simulated
            ? new SimulatedTimeProvider(DateTimeOffset.UnixEpoch)
            : TimeProvider.System;

        var uuidFactory = new UuidV7Factory(
            timeProvider,
            rng: new DeterministicRandomNumberGenerator(context.Seed),
            overflowBehavior: context.RuntimeMode == WorkloadRuntimeMode.Simulated
                ? CounterOverflowBehavior.IncrementTimestamp
                : CounterOverflowBehavior.SpinWait);

        var uuidStopwatch = Stopwatch.StartNew();
        var uuidBatches = new Guid[_uuidThreads][];
        var uuidTasks = Enumerable.Range(0, _uuidThreads)
            .Select(async thread =>
            {
                uuidBatches[thread] = new Guid[_uuidPerThread];
                await Task.Run(() =>
                {
                    for (var i = 0; i < _uuidPerThread; i++)
                    {
                        uuidBatches[thread][i] = uuidFactory.NewGuid();
                        if (timeProvider is SimulatedTimeProvider simulated && i % 2_048 == 0)
                        {
                            simulated.AdvanceMs(1);
                        }
                    }
                }, cancellationToken);
            })
            .ToArray();

        await Task.WhenAll(uuidTasks);
        uuidStopwatch.Stop();

        var allUuids = uuidBatches.SelectMany(static x => x).ToArray();
        var uniqueUuidCount = allUuids.Distinct().Count();
        var invalidUuidCount = allUuids.Count(g => !g.IsVersion7());

        var hlcStopwatch = Stopwatch.StartNew();
        var maxObservedDrift = 0L;
        var driftFailures = 0;
        var monotonicViolations = 0;

        using var scope = new DisposableBag();
        var nodes = new List<(HlcGuidFactory Factory, HlcCoordinator Coordinator)>(_hlcNodes);
        for (ushort nodeId = 1; nodeId <= _hlcNodes; nodeId++)
        {
            var options = nodeId % 2 == 0 ? HlcOptions.HighThroughput : HlcOptions.Strict;
            var factory = new HlcGuidFactory(timeProvider, nodeId, options, new DeterministicRandomNumberGenerator(HashCode.Combine(context.Seed, nodeId)));
            scope.Add(factory);
            nodes.Add((factory, new HlcCoordinator(factory)));
        }

        foreach (var (factory, coordinator) in nodes)
        {
            var previous = coordinator.CurrentTimestamp;
            for (var i = 0; i < _hlcEventsPerNode; i++)
            {
                var local = coordinator.BeforeSend();
                if (local < previous)
                {
                    monotonicViolations++;
                }

                previous = local;

                var peer = nodes[(i + factory.CurrentTimestamp.NodeId) % nodes.Count];
                try
                {
                    peer.Coordinator.BeforeReceive(local);
                    maxObservedDrift = Math.Max(maxObservedDrift, peer.Coordinator.Statistics.MaxObservedDriftMs);
                }
                catch (HlcDriftException)
                {
                    driftFailures++;
                }

                if (timeProvider is SimulatedTimeProvider simulated && i % 256 == 0)
                {
                    simulated.AdvanceMs(1);
                }
            }
        }

        hlcStopwatch.Stop();
        sw.Stop();

        var uuidOpsPerSec = allUuids.Length / Math.Max(uuidStopwatch.Elapsed.TotalSeconds, 0.000001);
        var hlcOps = (double)(_hlcNodes * _hlcEventsPerNode);
        var hlcOpsPerSec = hlcOps / Math.Max(hlcStopwatch.Elapsed.TotalSeconds, 0.000001);

        var invariants = new[]
        {
            new WorkloadInvariant("all-uuids-unique", uniqueUuidCount == allUuids.Length, $"{uniqueUuidCount}/{allUuids.Length} UUIDs were unique."),
            new WorkloadInvariant("all-uuids-version7", invalidUuidCount == 0, $"Observed {invalidUuidCount} invalid UUIDv7 values."),
            new WorkloadInvariant("hlc-monotonic", monotonicViolations == 0, $"Observed {monotonicViolations} HLC monotonicity violations."),
            new WorkloadInvariant("strict-drift-holds", driftFailures == 0, $"Observed {driftFailures} HLC drift exceptions.")
        };

        var metrics = new[]
        {
            new WorkloadMetric("uuid-ops-per-sec", uuidOpsPerSec, "ops/s", HigherIsBetter: true),
            new WorkloadMetric("hlc-ops-per-sec", hlcOpsPerSec, "ops/s", HigherIsBetter: true),
            new WorkloadMetric("runtime", sw.Elapsed.TotalMilliseconds, "ms"),
            new WorkloadMetric("uuid-count", allUuids.Length, "count", HigherIsBetter: true),
            new WorkloadMetric("hlc-events", hlcOps, "count", HigherIsBetter: true),
            new WorkloadMetric("max-observed-drift", maxObservedDrift, "ms"),
            new WorkloadMetric("drift-failures", driftFailures, "count")
        };

        return WorkloadCommand.FinalizeResult(
            Name,
            context.RuntimeMode,
            context.Seed,
            $"Generated {allUuids.Length} UUIDv7 values and {hlcOps:N0} HLC events.",
            sw.Elapsed,
            metrics,
            invariants,
            context.Baselines,
            startedUtc,
            DateTimeOffset.UtcNow,
            $"dotnet run --project demo/Clockworks.Demo -- workloads {Name} --mode {context.RuntimeMode} --seed {context.Seed}");
    }
}

public sealed class CausalFanoutFanInWorkloadScenario : IWorkloadScenario
{
    private readonly int _nodes;
    private readonly int _rounds;

    public CausalFanoutFanInWorkloadScenario(int nodes = 8, int rounds = 24)
    {
        _nodes = nodes;
        _rounds = rounds;
    }

    public string Name => "causal-fanout-fanin";
    public string Description => "Multi-node fan-out/fan-in simulation with partitions, concurrency, vector clocks, and HLC merges.";
    public WorkloadRuntimeMode SupportedModes => WorkloadRuntimeMode.Simulated;

    public Task<WorkloadResult> ExecuteAsync(WorkloadExecutionContext context, CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        var tp = new SimulatedTimeProvider(DateTimeOffset.UnixEpoch);
        var rng = new Random(context.Seed);

        using var scope = new DisposableBag();
        var nodes = new List<CausalNode>(_nodes);
        for (ushort nodeId = 1; nodeId <= _nodes; nodeId++)
        {
            var factory = new HlcGuidFactory(tp, nodeId, HlcOptions.HighThroughput, new DeterministicRandomNumberGenerator(HashCode.Combine(context.Seed, nodeId)));
            scope.Add(factory);
            nodes.Add(new CausalNode(nodeId, factory));
        }

        var leader = nodes[0];
        var concurrentPairs = 0;
        var maxVectorEntries = 0;

        for (var round = 0; round < _rounds; round++)
        {
            var rootHlc = leader.Hlc.BeforeSend();
            var rootVector = leader.Vector.BeforeSend();

            for (var i = 1; i < nodes.Count; i++)
            {
                nodes[i].Hlc.BeforeReceive(rootHlc);
                nodes[i].Vector.BeforeReceive(rootVector);
            }

            for (var i = 1; i < nodes.Count; i++)
            {
                nodes[i].Hlc.BeforeSend();
                nodes[i].Vector.NewLocalEvent();
                tp.AdvanceMs(rng.Next(1, 4));
            }

            var left = nodes.Skip(1).Take((nodes.Count - 1) / 2).ToArray();
            var right = nodes.Skip(1 + left.Length).ToArray();

            foreach (var node in left)
            {
                node.Vector.NewLocalEvent();
            }

            foreach (var node in right)
            {
                node.Vector.NewLocalEvent();
            }

            for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
            {
                var leftClock = left[i].Vector.Current;
                var rightClock = right[i].Vector.Current;
                if (leftClock.IsConcurrentWith(rightClock))
                {
                    concurrentPairs++;
                }
            }

            foreach (var node in nodes.Skip(1))
            {
                leader.Hlc.BeforeReceive(node.Hlc.BeforeSend());
                leader.Vector.BeforeReceive(node.Vector.BeforeSend());
                maxVectorEntries = Math.Max(maxVectorEntries, VectorClockExtensionsForWorkloads.CountEntries(leader.Vector.Current));
            }

            tp.AdvanceMs(rng.Next(1, 10));
        }

        sw.Stop();

        var leaderVector = leader.Vector.Current;
        var leaderDominates = nodes.Skip(1).All(n => n.Vector.Current.HappensBefore(leaderVector) || n.Vector.Current == leaderVector);

        var invariants = new[]
        {
            new WorkloadInvariant("concurrency-detected", concurrentPairs > 0, $"Observed {concurrentPairs} concurrent vector-clock pairs."),
            new WorkloadInvariant("leader-converged-after-fanin", leaderDominates, "Leader merged all downstream clocks after fan-in."),
            new WorkloadInvariant("hlc-receive-pressure", leader.Hlc.Statistics.ReceiveCount >= nodes.Count - 1, $"Leader processed {leader.Hlc.Statistics.ReceiveCount} receive events.")
        };

        var metrics = new[]
        {
            new WorkloadMetric("runtime", sw.Elapsed.TotalMilliseconds, "ms"),
            new WorkloadMetric("concurrent-pairs", concurrentPairs, "count", HigherIsBetter: true),
            new WorkloadMetric("max-vector-entries", maxVectorEntries, "entries"),
            new WorkloadMetric("leader-clock-merges", leader.Vector.Statistics.ClockMerges, "count", HigherIsBetter: true),
            new WorkloadMetric("leader-max-drift", leader.Hlc.Statistics.MaxObservedDriftMs, "ms"),
            new WorkloadMetric("leader-receives", leader.Hlc.Statistics.ReceiveCount, "count", HigherIsBetter: true)
        };

        return Task.FromResult(WorkloadCommand.FinalizeResult(
            Name,
            context.RuntimeMode,
            context.Seed,
            $"Observed {concurrentPairs} concurrent pairs with leader vector width {maxVectorEntries}.",
            sw.Elapsed,
            metrics,
            invariants,
            context.Baselines,
            startedUtc,
            DateTimeOffset.UtcNow,
            $"dotnet run --project demo/Clockworks.Demo -- workloads {Name} --seed {context.Seed}"));
    }
}

public sealed class LongRunningSoakWorkloadScenario : IWorkloadScenario
{
    private readonly OrderPipelineSimulationOptions _options;

    public LongRunningSoakWorkloadScenario(OrderPipelineSimulationOptions? options = null)
    {
        _options = options ?? new OrderPipelineSimulationOptions(
            Orders: 900,
            MaxSteps: 120_000,
            TickMs: 4,
            RetryAfterMs: 25,
            DropRate: 0.015,
            DuplicateRate: 0.04,
            ReorderRate: 0.04,
            MaxAdditionalDelayMs: 20,
            DedupeRetentionLimit: 768,
            PruneEvery: 48);
    }

    public string Name => "soak-bounded-growth";
    public string Description => "Long-running soak focused on bounded growth, retained state cleanup, and stable drain behavior.";
    public WorkloadRuntimeMode SupportedModes => WorkloadRuntimeMode.Simulated;

    public async Task<WorkloadResult> ExecuteAsync(WorkloadExecutionContext context, CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        var simulation = new OrderPipelineSimulation(Name, _options, context.RuntimeMode, context.Seed);
        var snapshot = await simulation.RunAsync(cancellationToken);

        sw.Stop();

        var invariants = new[]
        {
            new WorkloadInvariant("bounded-dedupe-growth", snapshot.MaxRetainedDedupeEntries <= _options.DedupeRetentionLimit, $"Peak retained dedupe entries {snapshot.MaxRetainedDedupeEntries}, limit {_options.DedupeRetentionLimit}."),
            new WorkloadInvariant("drained-after-soak", snapshot.InFlightCount == 0, $"Simulation ended with {snapshot.InFlightCount} messages in flight."),
            new WorkloadInvariant("orders-confirmed", snapshot.UnconfirmedOrders == 0, $"{snapshot.ConfirmedOrders}/{_options.Orders} orders confirmed.")
        };

        var metrics = new[]
        {
            new WorkloadMetric("runtime", sw.Elapsed.TotalMilliseconds, "ms"),
            new WorkloadMetric("confirmed-rate", snapshot.ConfirmedRate, "ratio", HigherIsBetter: true),
            new WorkloadMetric("max-retained-dedupe", snapshot.MaxRetainedDedupeEntries, "entries"),
            new WorkloadMetric("final-retained-dedupe", snapshot.FinalRetainedDedupeEntries, "entries"),
            new WorkloadMetric("peak-in-flight", snapshot.NetworkStats.MaxInFlight, "messages"),
            new WorkloadMetric("retries-scheduled", snapshot.NetworkStats.RetriesScheduled, "count", HigherIsBetter: true)
        };

        return WorkloadCommand.FinalizeResult(
            Name,
            context.RuntimeMode,
            context.Seed,
            $"Soak run retained at most {snapshot.MaxRetainedDedupeEntries} dedupe entries across {_options.Orders} orders.",
            sw.Elapsed,
            metrics,
            invariants,
            context.Baselines,
            startedUtc,
            DateTimeOffset.UtcNow,
            $"dotnet run --project demo/Clockworks.Demo -- workloads {Name} --seed {context.Seed}");
    }
}

public sealed record OrderPipelineSimulationOptions(
    int Orders,
    int MaxSteps,
    int TickMs,
    int RetryAfterMs,
    double DropRate,
    double DuplicateRate,
    double ReorderRate,
    int MaxAdditionalDelayMs,
    int DedupeRetentionLimit,
    int PruneEvery);

internal sealed record OrderPipelineSnapshot(
    int ConfirmedOrders,
    int UnconfirmedOrders,
    int InFlightCount,
    int CausalViolations,
    long MaxQueueLength,
    long MaxObservedDriftMs,
    long TotalVectorMerges,
    int DeterministicReplayHash,
    int MaxRetainedDedupeEntries,
    int FinalRetainedDedupeEntries,
    MessagingStatistics NetworkStats)
{
    public double ConfirmedRate => (ConfirmedOrders + UnconfirmedOrders) == 0
        ? 1d
        : ConfirmedOrders / (double)(ConfirmedOrders + UnconfirmedOrders);
}

internal sealed class OrderPipelineSimulation
{
    private readonly string _scenarioName;
    private readonly OrderPipelineSimulationOptions _options;
    private readonly WorkloadRuntimeMode _runtimeMode;
    private readonly int _seed;

    public OrderPipelineSimulation(
        string scenarioName,
        OrderPipelineSimulationOptions options,
        WorkloadRuntimeMode runtimeMode,
        int seed)
    {
        _scenarioName = scenarioName;
        _options = options;
        _runtimeMode = runtimeMode;
        _seed = seed;
    }

    public async Task<OrderPipelineSnapshot> RunAsync(CancellationToken cancellationToken)
    {
        var timeProvider = _runtimeMode == WorkloadRuntimeMode.Simulated
            ? new SimulatedTimeProvider(DateTimeOffset.UnixEpoch)
            : TimeProvider.System;
        var simulated = timeProvider as SimulatedTimeProvider;
        var random = new Random(_seed);
        var faultProfile = new FaultProfile(
            _options.DropRate,
            _options.DuplicateRate,
            _options.ReorderRate,
            _options.MaxAdditionalDelayMs);

        var network = new OrderPipelineNetwork(timeProvider, random, faultProfile);
        using var scope = new DisposableBag();

        var ordersFactory = new HlcGuidFactory(timeProvider, 1, HlcOptions.HighThroughput, new DeterministicRandomNumberGenerator(HashCode.Combine(_seed, 1)));
        var paymentsFactory = new HlcGuidFactory(timeProvider, 2, HlcOptions.HighThroughput, new DeterministicRandomNumberGenerator(HashCode.Combine(_seed, 2)));
        var inventoryFactory = new HlcGuidFactory(timeProvider, 3, HlcOptions.HighThroughput, new DeterministicRandomNumberGenerator(HashCode.Combine(_seed, 3)));

        scope.Add(ordersFactory);
        scope.Add(paymentsFactory);
        scope.Add(inventoryFactory);

        var ordersNode = new OrdersNode("orders", timeProvider, network, new HlcCoordinator(ordersFactory), new VectorClockCoordinator(1), _options);
        var paymentsNode = new AckNode("payments", timeProvider, network, new HlcCoordinator(paymentsFactory), new VectorClockCoordinator(2), _options.DedupeRetentionLimit, _options.PruneEvery);
        var inventoryNode = new AckNode("inventory", timeProvider, network, new HlcCoordinator(inventoryFactory), new VectorClockCoordinator(3), _options.DedupeRetentionLimit, _options.PruneEvery);

        network.Register(ordersNode);
        network.Register(paymentsNode);
        network.Register(inventoryNode);

        for (var i = 0; i < _options.Orders; i++)
        {
            ordersNode.StartNewOrder($"order-{i}");
            if (simulated is not null)
            {
                simulated.AdvanceMs(1);
            }
        }

        for (var step = 0; step < _options.MaxSteps && !ordersNode.IsDone; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            network.DeliverDue();
            ordersNode.Poll();
            paymentsNode.Poll();
            inventoryNode.Poll();

            if (simulated is not null)
            {
                simulated.AdvanceMs(_options.TickMs);
            }
            else
            {
                await Task.Delay(Math.Clamp(_options.TickMs, 1, 15), cancellationToken);
            }
        }

        network.DeliverDue();
        ordersNode.Poll();
        paymentsNode.Poll();
        inventoryNode.Poll();

        var retainedDedupe = ordersNode.RetainedDedupeEntries + paymentsNode.RetainedDedupeEntries + inventoryNode.RetainedDedupeEntries;
        var maxRetained = Math.Max(ordersNode.MaxRetainedDedupeEntries, Math.Max(paymentsNode.MaxRetainedDedupeEntries, inventoryNode.MaxRetainedDedupeEntries));

        return new OrderPipelineSnapshot(
            ordersNode.ConfirmedOrders,
            _options.Orders - ordersNode.ConfirmedOrders,
            network.InFlightCount,
            ordersNode.CausalViolations,
            (simulated?.Statistics.MaxQueueLength ?? 0) + network.MaxQueueLength,
            ordersNode.Hlc.Statistics.MaxObservedDriftMs + paymentsNode.Hlc.Statistics.MaxObservedDriftMs + inventoryNode.Hlc.Statistics.MaxObservedDriftMs,
            ordersNode.Vector.Statistics.ClockMerges + paymentsNode.Vector.Statistics.ClockMerges + inventoryNode.Vector.Statistics.ClockMerges,
            HashCode.Combine(_scenarioName, ordersNode.ConfirmedOrders, network.Stats.Sent, network.Stats.Dropped, network.Stats.Deduped),
            maxRetained,
            retainedDedupe,
            network.Stats);
    }

    private sealed class OrdersNode : PipelineNode
    {
        private readonly Dictionary<string, OrderState> _orders = new(StringComparer.Ordinal);
        private readonly OrderPipelineSimulationOptions _options;
        private int _confirmedOrders;
        private int _causalViolations;

        public OrdersNode(
            string name,
            TimeProvider timeProvider,
            OrderPipelineNetwork network,
            HlcCoordinator hlc,
            VectorClockCoordinator vector,
            OrderPipelineSimulationOptions options)
            : base(name, timeProvider, network, hlc, vector, options.DedupeRetentionLimit, options.PruneEvery)
        {
            _options = options;
        }

        public int ConfirmedOrders => Volatile.Read(ref _confirmedOrders);
        public int CausalViolations => Volatile.Read(ref _causalViolations);
        public bool IsDone => ConfirmedOrders == _orders.Count && Network.InFlightCount == 0;

        public void StartNewOrder(string orderId)
        {
            var state = new OrderState(orderId);
            _orders.Add(orderId, state);
            SendMissing(state);
        }

        public override void Poll()
        {
            base.Poll();

            foreach (var state in _orders.Values)
            {
                if (state.IsConfirmed)
                {
                    continue;
                }

                if (state.RetryHandle?.Token.IsCancellationRequested == true)
                {
                    state.RetryHandle.Dispose();
                    state.RetryHandle = null;
                    Network.Stats.RecordRetryScheduled();
                    SendMissing(state);
                }
            }
        }

        protected override void HandleMessage(PipelineMessage message)
        {
            Hlc.BeforeReceive(message.HlcTimestamp);
            Vector.BeforeReceive(message.VectorClock);

            if (!_orders.TryGetValue(message.OrderId, out var state))
            {
                return;
            }

            if (state.SeenMessageIds.Add(message.MessageId) == false)
            {
                Network.Stats.RecordDeduped();
                return;
            }

            var requestKey = message.Kind == MessageKind.PaymentAck ? MessageKind.PaymentRequest : MessageKind.InventoryRequest;

            if (state.RequestHlcByAttemptAndKind.TryGetValue((message.Attempt, requestKey), out var requestHlc) &&
                message.HlcTimestamp <= requestHlc)
            {
                Interlocked.Increment(ref _causalViolations);
            }

            if (state.RequestVectorByAttemptAndKind.TryGetValue((message.Attempt, requestKey), out var requestVector) &&
                !(requestVector.HappensBefore(message.VectorClock) || requestVector == message.VectorClock))
            {
                Interlocked.Increment(ref _causalViolations);
            }

            if (message.Kind == MessageKind.PaymentAck)
            {
                state.PaymentAcked = true;
            }
            else if (message.Kind == MessageKind.InventoryAck)
            {
                state.InventoryAcked = true;
            }

            if (state.IsConfirmed)
            {
                if (state.CountedConfirmed)
                {
                    return;
                }

                state.CountedConfirmed = true;
                state.RetryHandle?.Dispose();
                state.RetryHandle = null;
                Interlocked.Increment(ref _confirmedOrders);
            }
        }

        private void SendMissing(OrderState state)
        {
            state.Attempts++;
            state.RetryHandle?.Dispose();
            state.RetryHandle = Timeouts.CreateTimeoutHandle(TimeProvider, TimeSpan.FromMilliseconds(_options.RetryAfterMs), TimeoutStats);

            if (!state.PaymentAcked)
            {
                SendRequest(state, MessageKind.PaymentRequest, "payments");
            }

            if (!state.InventoryAcked)
            {
                SendRequest(state, MessageKind.InventoryRequest, "inventory");
            }
        }

        private void SendRequest(OrderState state, MessageKind kind, string target)
        {
            var hlc = Hlc.BeforeSend();
            var vector = Vector.BeforeSend();
            state.RequestHlcByAttemptAndKind[(state.Attempts, kind)] = hlc;
            state.RequestVectorByAttemptAndKind[(state.Attempts, kind)] = vector;

            Network.Send(new PipelineMessage(
                Guid.NewGuid(),
                Name,
                target,
                state.OrderId,
                kind,
                hlc,
                vector,
                state.Attempts));
        }
    }

    private sealed class AckNode : PipelineNode
    {
        public AckNode(
            string name,
            TimeProvider timeProvider,
            OrderPipelineNetwork network,
            HlcCoordinator hlc,
            VectorClockCoordinator vector,
            int dedupeRetentionLimit,
            int pruneEvery)
            : base(name, timeProvider, network, hlc, vector, dedupeRetentionLimit, pruneEvery)
        {
        }

        protected override void HandleMessage(PipelineMessage message)
        {
            Hlc.BeforeReceive(message.HlcTimestamp);
            Vector.BeforeReceive(message.VectorClock);

            var ackHlc = Hlc.BeforeSend();
            var ackVector = Vector.BeforeSend();
            var kind = message.Kind == MessageKind.PaymentRequest ? MessageKind.PaymentAck : MessageKind.InventoryAck;

            Network.Send(new PipelineMessage(
                Guid.NewGuid(),
                Name,
                "orders",
                message.OrderId,
                kind,
                ackHlc,
                ackVector,
                message.Attempt));
        }
    }

    private abstract class PipelineNode : IReceivesPipelineMessages
    {
        private readonly Dictionary<Guid, long> _dedupeStore = new();
        private readonly Queue<(Guid MessageId, long ObservedAt)> _dedupeQueue = new();
        private readonly int _dedupeRetentionLimit;
        private readonly int _pruneEvery;
        private int _processed;

        protected PipelineNode(
            string name,
            TimeProvider timeProvider,
            OrderPipelineNetwork network,
            HlcCoordinator hlc,
            VectorClockCoordinator vector,
            int dedupeRetentionLimit,
            int pruneEvery)
        {
            Name = name;
            TimeProvider = timeProvider;
            Network = network;
            Hlc = hlc;
            Vector = vector;
            _dedupeRetentionLimit = dedupeRetentionLimit;
            _pruneEvery = pruneEvery;
        }

        protected TimeProvider TimeProvider { get; }
        protected OrderPipelineNetwork Network { get; }
        public string Name { get; }
        public HlcCoordinator Hlc { get; }
        public VectorClockCoordinator Vector { get; }
        public TimeoutStatistics TimeoutStats { get; } = new();
        public int RetainedDedupeEntries => _dedupeStore.Count;
        public int MaxRetainedDedupeEntries { get; private set; }

        public virtual void Poll() => PruneDedupe();

        public void Receive(PipelineMessage message)
        {
            if (_dedupeStore.ContainsKey(message.MessageId))
            {
                Network.Stats.RecordDeduped();
                return;
            }

            _dedupeStore[message.MessageId] = TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            _dedupeQueue.Enqueue((message.MessageId, TimeProvider.GetUtcNow().ToUnixTimeMilliseconds()));
            PruneDedupe();
            MaxRetainedDedupeEntries = Math.Max(MaxRetainedDedupeEntries, _dedupeStore.Count);

            HandleMessage(message);

            _processed++;
            if (_processed % _pruneEvery == 0)
            {
                PruneDedupe();
            }
        }

        protected abstract void HandleMessage(PipelineMessage message);

        private void PruneDedupe()
        {
            while (_dedupeQueue.Count > _dedupeRetentionLimit)
            {
                var expired = _dedupeQueue.Dequeue();
                _dedupeStore.Remove(expired.MessageId);
            }
        }
    }
}

internal interface IReceivesPipelineMessages
{
    string Name { get; }
    void Receive(PipelineMessage message);
}

internal sealed class OrderPipelineNetwork
{
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly FaultProfile _faultProfile;
    private readonly Dictionary<string, IReceivesPipelineMessages> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly PriorityQueue<PipelineMessage, long> _queue = new();
    private long _maxQueueLength;

    public OrderPipelineNetwork(TimeProvider timeProvider, Random random, FaultProfile faultProfile)
    {
        _timeProvider = timeProvider;
        _random = random;
        _faultProfile = faultProfile;
    }

    public MessagingStatistics Stats { get; } = new();
    public int InFlightCount => _queue.Count;
    public long MaxQueueLength => Volatile.Read(ref _maxQueueLength);

    public void Register(IReceivesPipelineMessages node) => _nodes[node.Name] = node;

    public void Send(PipelineMessage message)
    {
        if (_random.NextDouble() < _faultProfile.DropRate)
        {
            Stats.RecordDropped();
            return;
        }

        Enqueue(message, isDuplicate: false);

        if (_random.NextDouble() < _faultProfile.DuplicateRate)
        {
            Enqueue(message, isDuplicate: true);
        }
    }

    public void DeliverDue()
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        while (_queue.TryPeek(out _, out var dueAtMs) && dueAtMs <= now)
        {
            var message = _queue.Dequeue();
            if (_nodes.TryGetValue(message.To, out var node))
            {
                Stats.RecordDelivered();
                node.Receive(message);
            }
        }
    }

    private void Enqueue(PipelineMessage message, bool isDuplicate)
    {
        var delay = _random.Next(0, _faultProfile.MaxAdditionalDelayMs + 1);
        if (_random.NextDouble() < _faultProfile.ReorderRate)
        {
            delay = Math.Max(0, delay / 2);
            Stats.RecordReordered();
        }

        var dueAtMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() + delay;
        _queue.Enqueue(message, dueAtMs);
        InterlockedMax(ref _maxQueueLength, _queue.Count);

        if (isDuplicate)
        {
            Stats.RecordDuplicated(_queue.Count);
        }
        else
        {
            Stats.RecordSent(_queue.Count);
        }
    }

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

internal readonly record struct FaultProfile(
    double DropRate,
    double DuplicateRate,
    double ReorderRate,
    int MaxAdditionalDelayMs);

internal readonly record struct PipelineMessage(
    Guid MessageId,
    string From,
    string To,
    string OrderId,
    MessageKind Kind,
    HlcTimestamp HlcTimestamp,
    VectorClock VectorClock,
    int Attempt);

internal enum MessageKind
{
    PaymentRequest,
    InventoryRequest,
    PaymentAck,
    InventoryAck
}

internal sealed class OrderState
{
    public OrderState(string orderId)
    {
        OrderId = orderId;
    }

    public string OrderId { get; }
    public bool PaymentAcked { get; set; }
    public bool InventoryAcked { get; set; }
    public int Attempts { get; set; }
    public HashSet<Guid> SeenMessageIds { get; } = [];
    public Dictionary<(int Attempt, MessageKind Kind), HlcTimestamp> RequestHlcByAttemptAndKind { get; } = [];
    public Dictionary<(int Attempt, MessageKind Kind), VectorClock> RequestVectorByAttemptAndKind { get; } = [];
    public Timeouts.TimeoutHandle? RetryHandle { get; set; }
    public bool CountedConfirmed { get; set; }
    public bool IsConfirmed => PaymentAcked && InventoryAcked;
}

internal sealed class DisposableBag : IDisposable
{
    private readonly List<IDisposable> _items = [];

    public void Add(IDisposable disposable) => _items.Add(disposable);

    public void Dispose()
    {
        foreach (var item in _items.AsEnumerable().Reverse())
        {
            item.Dispose();
        }
    }
}

internal sealed record CausalNode(ushort NodeId, HlcGuidFactory Factory)
{
    public HlcCoordinator Hlc { get; } = new(Factory);
    public VectorClockCoordinator Vector { get; } = new(NodeId);
}

internal static class VectorClockExtensionsForWorkloads
{
    public static bool HappensBefore(this VectorClock left, VectorClock right) =>
        left.Compare(right) == VectorClockOrder.Before;

    public static bool IsConcurrentWith(this VectorClock left, VectorClock right) =>
        left.Compare(right) == VectorClockOrder.Concurrent;

    public static int CountEntries(VectorClock clock)
    {
        var text = clock.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return text.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
