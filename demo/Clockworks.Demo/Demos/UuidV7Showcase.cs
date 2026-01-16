using System.Diagnostics;
using Clockworks;
using Clockworks.Distributed;

namespace Clockworks.Demo.Demos;

internal static class UuidV7Showcase
{
    public static async Task Run(string[] args)
    {
        // Keep original demo content as the default "big" walkthrough, but make it runnable via demo key.
        Console.WriteLine("UUIDv7 + HLC Showcase");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        Console.WriteLine("1. BASIC UUIDv7");
        Console.WriteLine(new string('-', 40));

        using var productionFactory = new UuidV7Factory(TimeProvider.System);
        var guid1 = productionFactory.NewGuid();
        var guid2 = productionFactory.NewGuid();

        Console.WriteLine("Generated UUIDv7s:");
        Console.WriteLine($"  {guid1}");
        Console.WriteLine($"  {guid2}");
        Console.WriteLine($"  Timestamp: {guid1.GetTimestamp():O}");
        Console.WriteLine($"  Counter: {guid1.GetCounter()}");
        Console.WriteLine($"  Is V7: {guid1.IsVersion7()}");
        Console.WriteLine();

        Console.WriteLine("2. DETERMINISTIC TESTING");
        Console.WriteLine(new string('-', 40));

        static (UuidV7Factory Factory, SimulatedTimeProvider Time) CreateDeterministicFactory(int seed, long startTimeUnixMs)
        {
            var time = SimulatedTimeProvider.FromUnixMs(startTimeUnixMs);
            var rng = new DeterministicRandomNumberGenerator(seed);
            var factory = new UuidV7Factory(time, rng);
            return (factory, time);
        }

        var (factory1, time1) = CreateDeterministicFactory(seed: 42, startTimeUnixMs: 1704067200000);
        var (factory2, time2) = CreateDeterministicFactory(seed: 42, startTimeUnixMs: 1704067200000);

        var deterministicGuid1 = factory1.NewGuid();
        var deterministicGuid2 = factory2.NewGuid();

        Console.WriteLine("Same seed produces identical GUIDs:");
        Console.WriteLine($"  Factory 1: {deterministicGuid1}");
        Console.WriteLine($"  Factory 2: {deterministicGuid2}");
        Console.WriteLine($"  Match: {deterministicGuid1 == deterministicGuid2}");
        Console.WriteLine();

        time1.AdvanceMs(100);
        time2.AdvanceMs(100);

        var nextGuid1 = factory1.NewGuid();
        var nextGuid2 = factory2.NewGuid();
        Console.WriteLine("After advancing time by 100ms:");
        Console.WriteLine($"  Factory 1: {nextGuid1}");
        Console.WriteLine($"  Factory 2: {nextGuid2}");
        Console.WriteLine($"  Match: {nextGuid1 == nextGuid2}");
        Console.WriteLine($"  Temporally ordered: {deterministicGuid1.CompareByTimestamp(nextGuid1) < 0}");
        Console.WriteLine();

        Console.WriteLine("3. HYBRID LOGICAL CLOCK");
        Console.WriteLine(new string('-', 40));

        var hlcTime = new SimulatedTimeProvider(DateTimeOffset.UtcNow);
        using var hlcFactory = new HlcGuidFactory(hlcTime, nodeId: 1);

        var (hlcGuid, hlcTimestamp) = hlcFactory.NewGuidWithHlc();
        Console.WriteLine($"HLC GUID: {hlcGuid}");
        Console.WriteLine($"HLC Timestamp: {hlcTimestamp}");
        Console.WriteLine($"  Wall Time: {hlcTimestamp.WallTimeMs}");
        Console.WriteLine($"  Counter: {hlcTimestamp.Counter}");
        Console.WriteLine($"  Node ID: {hlcTimestamp.NodeId}");
        Console.WriteLine();

        var futureTimestamp = hlcTimestamp.WallTimeMs + 5000;
        Console.WriteLine($"Witnessing remote timestamp 5s ahead: {futureTimestamp}");
        hlcFactory.Witness(futureTimestamp);

        var (_, afterWitness) = hlcFactory.NewGuidWithHlc();
        Console.WriteLine("After witness:");
        Console.WriteLine($"  HLC Timestamp: {afterWitness}");
        Console.WriteLine($"  Clock advanced: {afterWitness.WallTimeMs >= futureTimestamp}");
        Console.WriteLine();

        Console.WriteLine("4. DISTRIBUTED SYSTEM SIMULATION");
        Console.WriteLine(new string('-', 40));

        var sharedTime = new SimulatedTimeProvider(DateTimeOffset.UtcNow);
        var cluster = new HlcClusterRegistry(sharedTime);

        var node0 = cluster.RegisterNode(0);
        var node1 = cluster.RegisterNode(1);
        var node2 = cluster.RegisterNode(2);

        Console.WriteLine("Simulating message flow: Node0 → Node1 → Node2");
        Console.WriteLine();

        var (_, ts0) = node0.NewGuidWithHlc();
        Console.WriteLine($"Node 0 event: {ts0}");

        sharedTime.AdvanceMs(10);

        node1.Witness(ts0);
        var (_, ts1) = node1.NewGuidWithHlc();
        Console.WriteLine($"Node 1 event (after receiving): {ts1}");

        sharedTime.AdvanceMs(5);

        node2.Witness(ts1);
        var (_, ts2) = node2.NewGuidWithHlc();
        Console.WriteLine($"Node 2 event (after receiving): {ts2}");

        Console.WriteLine();
        Console.WriteLine("Causal ordering preserved:");
        Console.WriteLine($"  ts0 < ts1: {ts0 < ts1}");
        Console.WriteLine($"  ts1 < ts2: {ts1 < ts2}");
        Console.WriteLine();

        Console.WriteLine("5. COUNTER OVERFLOW HANDLING");
        Console.WriteLine(new string('-', 40));

        var overflowTime = new SimulatedTimeProvider(DateTimeOffset.UtcNow);
        using var overflowFactory = new UuidV7Factory(
            overflowTime,
            overflowBehavior: CounterOverflowBehavior.IncrementTimestamp);

        Console.WriteLine("Generating 5000 GUIDs in same millisecond (max counter = 4095)...");

        var guids = new Guid[5000];
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < guids.Length; i++)
        {
            guids[i] = overflowFactory.NewGuid();
        }
        sw.Stop();

        bool monotonic = true;
        for (int i = 1; i < guids.Length; i++)
        {
            if (guids[i].CompareByTimestamp(guids[i - 1]) <= 0)
            {
                monotonic = false;
                Console.WriteLine($"  Monotonicity broken at index {i}");
                break;
            }
        }

        var overflowSeconds = sw.Elapsed.TotalSeconds;
        if (overflowSeconds <= 0)
        {
            overflowSeconds = Math.Max(1.0 / Stopwatch.Frequency, 1e-9);
        }

        Console.WriteLine($"Generated in: {sw.Elapsed.TotalMilliseconds:N3}ms ({guids.Length / overflowSeconds:N0} GUIDs/sec)");
        Console.WriteLine($"Monotonicity preserved: {monotonic}");
        Console.WriteLine($"First GUID timestamp: {guids[0].GetTimestampMs()}");
        Console.WriteLine($"Last GUID timestamp: {guids[^1].GetTimestampMs()}");
        Console.WriteLine($"Timestamp drift: {guids[^1].GetTimestampMs() - guids[0].GetTimestampMs()}ms");
        Console.WriteLine();

        // Large benchmark is optional to keep demo runs short.
        if (args.Any(a => a.Equals("--bench", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("6. PERFORMANCE BENCHMARKS");
            Console.WriteLine(new string('-', 40));

            await RunBenchmarks();
        }

        Console.WriteLine();
        Console.WriteLine("Done!");
    }

    private static async Task RunBenchmarks()
    {
        const int WarmupIterations = 50_000;
        const int BenchmarkIterations = 250_000;
        const int ThreadCount = 8;

        Console.WriteLine("Warming up...");
        using (var warmupFactory = new UuidV7Factory(TimeProvider.System))
        {
            for (int i = 0; i < WarmupIterations; i++)
            {
                _ = warmupFactory.NewGuid();
            }
        }

        Console.WriteLine();
        Console.WriteLine("Lock-Free Factory (single-threaded):");
        using (var factory = new UuidV7Factory(TimeProvider.System))
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < BenchmarkIterations; i++)
            {
                _ = factory.NewGuid();
            }
            sw.Stop();

            var seconds = sw.Elapsed.TotalSeconds;
            var opsPerSec = BenchmarkIterations / seconds;
            var nsPerOp = sw.Elapsed.TotalNanoseconds / BenchmarkIterations;
            Console.WriteLine($"  {opsPerSec:N0} ops/sec ({nsPerOp:N1} ns/op)");
        }

        Console.WriteLine();
        Console.WriteLine($"Lock-Free Factory ({ThreadCount} threads contended):");
        using (var factory = new UuidV7Factory(TimeProvider.System))
        {
            var iterationsPerThread = BenchmarkIterations / ThreadCount;
            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, ThreadCount)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        factory.NewGuid();
                    }
                }))
                .ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            var seconds = sw.Elapsed.TotalSeconds;
            var opsPerSec = BenchmarkIterations / seconds;
            Console.WriteLine($"  {opsPerSec:N0} ops/sec total ({opsPerSec / ThreadCount:N0} ops/sec/thread)");
        }

        Console.WriteLine();
        Console.WriteLine("HLC Factory (single-threaded):");
        using (var factory = new HlcGuidFactory(TimeProvider.System, nodeId: 0))
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < BenchmarkIterations; i++)
            {
                _ = factory.NewGuid();
            }
            sw.Stop();

            var seconds = sw.Elapsed.TotalSeconds;
            var opsPerSec = BenchmarkIterations / seconds;
            var nsPerOp = sw.Elapsed.TotalNanoseconds / BenchmarkIterations;
            Console.WriteLine($"  {opsPerSec:N0} ops/sec ({nsPerOp:N1} ns/op)");
        }

        Console.WriteLine();
        Console.WriteLine("System Guid.CreateVersion7() (baseline):");
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < BenchmarkIterations; i++)
            {
                _ = Guid.CreateVersion7();
            }
            sw.Stop();

            var seconds = sw.Elapsed.TotalSeconds;
            var opsPerSec = BenchmarkIterations / seconds;
            var nsPerOp = sw.Elapsed.TotalNanoseconds / BenchmarkIterations;
            Console.WriteLine($"  {opsPerSec:N0} ops/sec ({nsPerOp:N1} ns/op)");
        }
    }
}
