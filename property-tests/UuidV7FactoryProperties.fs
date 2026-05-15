module Clockworks.PropertyTests.UuidV7FactoryProperties

open System
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open Xunit
open FsCheck
open FsCheck.Xunit
open Clockworks
open Clockworks.Instrumentation

type private DeterministicRandomNumberGenerator(seed: int) =
    inherit RandomNumberGenerator()

    let random = Random(seed)

    override _.GetBytes(data: byte[]) =
        random.NextBytes(data)

    override _.GetBytes(data: Span<byte>) =
        random.NextBytes(data)

    override _.GetBytes(data: byte[], offset: int, count: int) =
        random.NextBytes(data.AsSpan(offset, count))

/// Property: Sequential UUIDs should maintain monotonic ordering
[<Property(MaxTest = 100)>]
let ``Sequential UUIDs are monotonically increasing`` (count: uint16) =
    let safeCount = int (count % 100us) + 2 // Test with 2-100 UUIDs
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider)
    
    let uuids = [| for _ in 1..safeCount -> factory.NewGuid() |]
    
    // Check that each UUID is strictly greater than the previous
    let isMonotonic = 
        uuids
        |> Array.pairwise
        |> Array.forall (fun (prev, curr) -> prev < curr)
    
    isMonotonic

/// Property: Deterministic RNG injection should replay the same UUID sequence for the same seed/time/call pattern.
[<Property(MaxTest = 50)>]
let ``Deterministic RNG replays UUID sequence`` (seed: int) (count: byte) =
    let safeCount = int (count % 32uy) + 1
    let startMs = 1_700_000_000_000L

    use leftRng = new DeterministicRandomNumberGenerator(seed)
    use rightRng = new DeterministicRandomNumberGenerator(seed)
    use left = new UuidV7Factory(SimulatedTimeProvider.FromUnixMs(startMs), leftRng)
    use right = new UuidV7Factory(SimulatedTimeProvider.FromUnixMs(startMs), rightRng)

    let leftIds = [| for _ in 1..safeCount -> left.NewGuid() |]
    let rightIds = [| for _ in 1..safeCount -> right.NewGuid() |]

    leftIds = rightIds

/// Property: UUIDs generated at the same millisecond should differ only in counter/random parts
[<Property(MaxTest = 50)>]
let ``UUIDs at same millisecond have same timestamp prefix`` () =
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider)
    
    // Generate multiple UUIDs without advancing time
    let uuid1 = factory.NewGuid()
    let uuid2 = factory.NewGuid()
    
    // Extract the timestamp portion (first 48 bits, big-endian)
    let ts1 = uuid1.GetTimestampMs()
    let ts2 = uuid2.GetTimestampMs()
    
    ts1.HasValue && ts2.HasValue && ts1.Value = ts2.Value

/// Property: Advancing time should result in UUIDs with different timestamps
[<Property(MaxTest = 50)>]
let ``Advancing time changes UUID timestamp`` (advanceMs: uint16) =
    let safeAdvanceMs = int64 (advanceMs % 1000us) + 1L
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider)
    
    let uuid1 = factory.NewGuid()
    timeProvider.Advance(TimeSpan.FromMilliseconds(float safeAdvanceMs))
    let uuid2 = factory.NewGuid()
    
    // UUIDs should be different and uuid2 > uuid1
    uuid1 <> uuid2 && uuid2 > uuid1

/// Property: SpinWait overflow resumes once time advances
[<Fact>]
let ``SpinWait overflow resumes after time advances`` () =
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider, overflowBehavior = CounterOverflowBehavior.SpinWait)
    
    // Generate enough UUIDs to potentially overflow the counter (4096 max)
    // In practice, the factory should handle this by spinning
    let mutable success = true
    let maxCounter = 0x0FFFus
    let mutable reachedMax = false
    let mutable attempts = 0

    try
        while not reachedMax && attempts < 5000 do
            let guid = factory.NewGuid()
            let counter = guid.GetCounter()
            if counter.HasValue && counter.Value = maxCounter then
                reachedMax <- true
            attempts <- attempts + 1
    with
    | _ -> success <- false

    if success && reachedMax then
        use gate = new ManualResetEventSlim(false)
        use ready = new ManualResetEventSlim(false)
        let pending =
            Task.Run(fun () ->
                ready.Set()
                gate.Wait()
                factory.NewGuid())

        try
            // Ensure the task is waiting before releasing the gate.
            ready.Wait()
            gate.Set()

            // Advance simulated time while waiting, capped to avoid hanging forever.
            let mutable waitIterations = 0
            let maxWaitIterations = 10000
            while not pending.IsCompleted && waitIterations < maxWaitIterations do
                timeProvider.Advance(TimeSpan.FromMilliseconds(1.0))
                waitIterations <- waitIterations + 1

            // Wait for completion with a real-time timeout to prevent deadlock
            let completed = pending.Wait(TimeSpan.FromSeconds(5.0))
            if not completed then
                success <- false
        finally
            // Only dispose if the task is in a completion state
            if pending.IsCompleted then
                pending.Dispose()
    elif not reachedMax then
        success <- false
    
    // Should either succeed or time out gracefully
    // For this test, we just verify it doesn't throw unexpected exceptions
    success

/// Property: UUIDs are unique across many generations
[<Property(MaxTest = 50)>]
let ``UUIDs are unique`` (count: byte) =
    let safeCount = int count + 10 // Test with 10-265 UUIDs
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider)
    
    let uuids = [| for _ in 1..safeCount -> factory.NewGuid() |]
    let uniqueCount = uuids |> Array.distinct |> Array.length
    
    uniqueCount = safeCount

/// Property: UUIDv7 statistics count every successfully generated UUID exactly once.
[<Property(MaxTest = 50)>]
let ``Statistics generated count matches successful UUID generation`` (count: uint16) =
    let safeCount = int (count % 512us) + 1
    let statistics = UuidV7FactoryStatistics()
    let timeProvider = new SimulatedTimeProvider()
    use factory =
        new UuidV7Factory(
            timeProvider,
            null,
            CounterOverflowBehavior.IncrementTimestamp,
            statistics)

    statistics.Reset()

    for _ in 1..safeCount do
        factory.NewGuid() |> ignore

    statistics.Snapshot().GeneratedCount = int64 safeCount

/// Property: UUIDv7 statistics expose the maximum logical drift caused by wall-clock rollback.
[<Property(MaxTest = 50)>]
let ``Statistics max drift tracks wall clock rollback`` (rollbackMs: uint16) =
    let safeRollbackMs = int64 (rollbackMs % 1000us) + 1L
    let startMs = 1_700_000_000_000L
    let statistics = UuidV7FactoryStatistics()
    let timeProvider = SimulatedTimeProvider.FromUnixMs(startMs)
    use factory = new UuidV7Factory(timeProvider, null, CounterOverflowBehavior.SpinWait, statistics)

    statistics.Reset()

    factory.NewGuid() |> ignore
    timeProvider.SetUnixMs(startMs - safeRollbackMs)
    factory.NewGuid() |> ignore

    let snapshot = statistics.Snapshot()
    snapshot.GeneratedCount = 2L
    && snapshot.ClockRollbackCount = 1L
    && snapshot.LogicalTimestampAdvanceCount = 1L
    && snapshot.MaxLogicalDriftMs = safeRollbackMs

/// Property: node-partitioned UUIDv7 embeds and round-trips the configured node ID.
[<Property(MaxTest = 50)>]
let ``Node partition round trips configured node ID`` (rawWidth: byte) (rawNodeId: uint16) =
    let width = byte ((int rawWidth % int UuidV7NodePartition.MaxNodeIdBitWidth) + 1)
    let maxNodeId = UuidV7NodePartition.GetMaxNodeId(width)
    let nodeId = uint16 (int rawNodeId % (int maxNodeId + 1))
    let partition = UuidV7NodePartition(nodeId, width)
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider, partition)

    let uuid = factory.NewGuid()
    let extracted = uuid.GetNodePartitionId(width)

    extracted.HasValue && extracted.Value = nodeId

/// Property: different node partitions separate otherwise identical deterministic UUID streams.
[<Property(MaxTest = 50)>]
let ``Node partitions separate identical deterministic UUID streams`` (rawWidth: byte) (count: byte) =
    let width = byte ((int rawWidth % int UuidV7NodePartition.MaxNodeIdBitWidth) + 1)
    let maxNodeId = UuidV7NodePartition.GetMaxNodeId(width)
    let safeCount = int (count % 32uy) + 1
    let startMs = 1_700_000_000_000L

    let leftPartition = UuidV7NodePartition(0us, width)
    let rightPartition = UuidV7NodePartition(maxNodeId, width)
    use leftRng = new DeterministicRandomNumberGenerator(42)
    use rightRng = new DeterministicRandomNumberGenerator(42)
    use left = new UuidV7Factory(SimulatedTimeProvider.FromUnixMs(startMs), leftPartition, leftRng)
    use right = new UuidV7Factory(SimulatedTimeProvider.FromUnixMs(startMs), rightPartition, rightRng)

    [| for _ in 1..safeCount -> left.NewGuid(), right.NewGuid() |]
    |> Array.forall (fun (leftId, rightId) ->
        leftId <> rightId
        && leftId.GetTimestampMs() = rightId.GetTimestampMs()
        && leftId.GetCounter() = rightId.GetCounter()
        && leftId.GetNodePartitionId(width).Value = 0us
        && rightId.GetNodePartitionId(width).Value = maxNodeId)

/// Property: restored UUIDv7 factory state is a monotonic lower bound for future generation.
[<Property(MaxTest = 50)>]
let ``Restored state is a monotonic lower bound`` (counter: uint16) (rollbackMs: uint16) =
    let safeCounter = counter % (UuidV7FactoryState.MaxCounter + 1us)
    let safeRollbackMs = int64 (rollbackMs % 1000us) + 1L
    let stateMs = 1_700_000_000_000L
    let restoredState = UuidV7FactoryState(stateMs, safeCounter)
    let timeProvider = SimulatedTimeProvider.FromUnixMs(stateMs - safeRollbackMs)
    use factory = new UuidV7Factory(timeProvider, restoredState)

    let uuid = factory.NewGuid()
    let timestamp = uuid.GetTimestampMs().Value
    let generatedCounter = uuid.GetCounter().Value

    timestamp > restoredState.TimestampMs
    || (timestamp = restoredState.TimestampMs && generatedCounter > safeCounter)

/// Property: restoring an older UUIDv7 factory state never lowers the current frontier.
[<Property(MaxTest = 50)>]
let ``RestoreState never lowers frontier`` (count: byte) =
    let safeCount = int (count % 64uy) + 1
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider)

    for _ in 1..safeCount do
        factory.NewGuid() |> ignore

    let current = factory.GetState()
    factory.RestoreState(UuidV7FactoryState(0L, 0us))
    let afterRestore = factory.GetState()

    afterRestore = current

/// Property: UUIDs generated at different times are different
[<Property(MaxTest = 50)>]
let ``UUIDs change with time`` (advanceMs: uint16) =
    let safeAdvanceMs = int64 (advanceMs % 1000us) + 1L
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider)
    
    let uuid1 = factory.NewGuid()
    timeProvider.Advance(TimeSpan.FromMilliseconds(float safeAdvanceMs))
    let uuid2 = factory.NewGuid()
    
    // UUIDs should be different when time changes
    uuid1 <> uuid2

/// Property: Concurrent UUID generation maintains uniqueness
[<Property(MaxTest = 20, Verbose = false)>]
let ``Concurrent generation produces unique UUIDs`` (count: byte) =
    let safeCount = int count + 10
    let timeProvider = TimeProvider.System
    use factory = new UuidV7Factory(timeProvider)
    
    // Generate UUIDs from multiple threads concurrently
    let uuids = 
        [| 1..safeCount |]
        |> Array.Parallel.map (fun _ -> factory.NewGuid())
    
    let uniqueCount = uuids |> Array.distinct |> Array.length
    uniqueCount = safeCount
