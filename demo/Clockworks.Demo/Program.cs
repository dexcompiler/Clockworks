using System.Diagnostics;
using Clockworks;
using Clockworks.Distributed;

// ============================================================================
// TimeControlledIds - UUIDv7 with Complete Time Control
// ============================================================================

Console.WriteLine("TimeControlledIds - UUIDv7 with Complete Time Control");
Console.WriteLine(new string('=', 60));
Console.WriteLine();

// ----------------------------------------------------------------------------
// 1. Basic Usage Demo
// ----------------------------------------------------------------------------
Console.WriteLine("1. BASIC USAGE");
Console.WriteLine(new string('-', 40));

// Production usage with system time
using var productionFactory = new UuidV7Factory(TimeProvider.System);
var guid1 = productionFactory.NewGuid();
var guid2 = productionFactory.NewGuid();

Console.WriteLine($"Generated UUIDv7s:");
Console.WriteLine($"  {guid1}");
Console.WriteLine($"  {guid2}");
Console.WriteLine($"  Timestamp: {guid1.GetTimestamp():O}");
Console.WriteLine($"  Counter: {guid1.GetCounter()}");
Console.WriteLine($"  Is V7: {guid1.IsVersion7()}");
Console.WriteLine();

// ----------------------------------------------------------------------------
// 2. Deterministic Testing Demo
// ----------------------------------------------------------------------------
Console.WriteLine("2. DETERMINISTIC TESTING");
Console.WriteLine(new string('-', 40));

static (UuidV7Factory Factory, SimulatedTimeProvider Time) CreateDeterministicFactory(int seed, long startTimeUnixMs)
{
    var time = SimulatedTimeProvider.FromUnixMs(startTimeUnixMs);
    var rng = new Clockworks.Demo.DeterministicRandomNumberGenerator(seed);
    var factory = new UuidV7Factory(time, rng);
    return (factory, time);
}

var (factory1, time1) = CreateDeterministicFactory(seed: 42, startTimeUnixMs: 1704067200000);
var (factory2, time2) = CreateDeterministicFactory(seed: 42, startTimeUnixMs: 1704067200000);

var deterministicGuid1 = factory1.NewGuid();
var deterministicGuid2 = factory2.NewGuid();

Console.WriteLine($"Same seed produces identical GUIDs:");
Console.WriteLine($"  Factory 1: {deterministicGuid1}");
Console.WriteLine($"  Factory 2: {deterministicGuid2}");
Console.WriteLine($"  Match: {deterministicGuid1 == deterministicGuid2}");
Console.WriteLine();

// Advance time and generate more
time1.AdvanceMs(100);
time2.AdvanceMs(100);

var nextGuid1 = factory1.NewGuid();
var nextGuid2 = factory2.NewGuid();
Console.WriteLine($"After advancing time by 100ms:");
Console.WriteLine($"  Factory 1: {nextGuid1}");
Console.WriteLine($"  Factory 2: {nextGuid2}");
Console.WriteLine($"  Match: {nextGuid1 == nextGuid2}");
Console.WriteLine($"  Temporally ordered: {deterministicGuid1.CompareByTimestamp(nextGuid1) < 0}");
Console.WriteLine();

// ----------------------------------------------------------------------------
// 3. Hybrid Logical Clock Demo
// ----------------------------------------------------------------------------
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

// Simulate receiving a message from the future (clock skew)
var futureTimestamp = hlcTimestamp.WallTimeMs + 5000; // 5 seconds ahead
Console.WriteLine($"Witnessing remote timestamp 5s ahead: {futureTimestamp}");
hlcFactory.Witness(futureTimestamp);

var (_, afterWitness) = hlcFactory.NewGuidWithHlc();
Console.WriteLine($"After witness:");
Console.WriteLine($"  HLC Timestamp: {afterWitness}");
Console.WriteLine($"  Clock advanced: {afterWitness.WallTimeMs >= futureTimestamp}");
Console.WriteLine();

// ----------------------------------------------------------------------------
// 4. Distributed System Simulation
// ----------------------------------------------------------------------------
Console.WriteLine("4. DISTRIBUTED SYSTEM SIMULATION");
Console.WriteLine(new string('-', 40));

var sharedTime = new SimulatedTimeProvider(DateTimeOffset.UtcNow);
var cluster = new HlcClusterRegistry(sharedTime);

var node0 = cluster.RegisterNode(0);
var node1 = cluster.RegisterNode(1);
var node2 = cluster.RegisterNode(2);

Console.WriteLine("Simulating message flow: Node0 → Node1 → Node2");
Console.WriteLine();

// Node 0 sends to Node 1
var (event0, ts0) = node0.NewGuidWithHlc();
Console.WriteLine($"Node 0 event: {ts0}");

// Simulate network delay
sharedTime.AdvanceMs(10);

// Node 1 receives and processes
node1.Witness(ts0);
var (event1, ts1) = node1.NewGuidWithHlc();
Console.WriteLine($"Node 1 event (after receiving): {ts1}");

// More network delay
sharedTime.AdvanceMs(5);

// Node 2 receives from Node 1
node2.Witness(ts1);
var (event2, ts2) = node2.NewGuidWithHlc();
Console.WriteLine($"Node 2 event (after receiving): {ts2}");

Console.WriteLine();
Console.WriteLine("Causal ordering preserved:");
Console.WriteLine($"  ts0 < ts1: {ts0 < ts1}");
Console.WriteLine($"  ts1 < ts2: {ts1 < ts2}");
Console.WriteLine($"  event0 < event1 < event2: {event0.CompareByTimestamp(event1) < 0 && event1.CompareByTimestamp(event2) < 0}");
Console.WriteLine();

// ----------------------------------------------------------------------------
// 5. Counter Overflow Behavior Demo
// ----------------------------------------------------------------------------
Console.WriteLine("5. COUNTER OVERFLOW HANDLING");
Console.WriteLine(new string('-', 40));

var overflowTime = new SimulatedTimeProvider(DateTimeOffset.UtcNow);
// Disable auto-advance to stay in same millisecond
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

// Check monotonicity
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

// ----------------------------------------------------------------------------
// 6. Performance Benchmarks
// ----------------------------------------------------------------------------
Console.WriteLine("6. PERFORMANCE BENCHMARKS");
Console.WriteLine(new string('-', 40));

await RunBenchmarks();

Console.WriteLine();
Console.WriteLine("Done!");

// ============================================================================
// Benchmark Implementation
// ============================================================================

static async Task RunBenchmarks()
{
    const int WarmupIterations = 100_000;
    const int BenchmarkIterations = 1_000_000;
    const int ThreadCount = 8;

    // Warmup
    Console.WriteLine("Warming up...");
    using (var warmupFactory = new UuidV7Factory(TimeProvider.System))
    {
        for (int i = 0; i < WarmupIterations; i++)
        {
            _ = warmupFactory.NewGuid();
        }
    }

    // Benchmark 1: LockFree single-threaded
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

    // Benchmark 2: LockFree multi-threaded
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

    // Benchmark 3: HLC single-threaded
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

    // Benchmark 4: System Guid.CreateVersion7 (baseline)
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

    // Benchmark 5: Batch generation
    Console.WriteLine();
    Console.WriteLine("Batch Generation (1000 GUIDs per call):");
    using (var factory = new UuidV7Factory(TimeProvider.System))
    {
        var batch = new Guid[1000];
        var iterations = BenchmarkIterations / 1000;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            factory.NewGuids(batch);
        }
        sw.Stop();

        var seconds = sw.Elapsed.TotalSeconds;
        var opsPerSec = BenchmarkIterations / seconds;
        Console.WriteLine($"  {opsPerSec:N0} ops/sec");
    }

    // Memory allocation check
    Console.WriteLine();
    Console.WriteLine("Memory Allocation Check (10K iterations):");
    using (var factory = new UuidV7Factory(TimeProvider.System))
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = factory.NewGuid();
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Console.WriteLine($"  Bytes allocated: {after - before:N0}");
        Console.WriteLine($"  Bytes per GUID: {(after - before) / 10_000.0:N2}");
    }
}
