module Clockworks.PropertyTests.UuidV7FactoryProperties

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open FsCheck
open FsCheck.Xunit
open Clockworks

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
