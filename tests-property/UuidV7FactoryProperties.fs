module Clockworks.PropertyTests.UuidV7FactoryProperties

open System
open System.Security.Cryptography
open System.Threading.Tasks
open Xunit
open FsCheck
open FsCheck.Xunit
open Clockworks

type FixedRandomNumberGenerator() =
    inherit RandomNumberGenerator()
    override _.GetBytes(data: byte[]) =
        for i in 0 .. data.Length - 1 do
            data[i] <- 0xFFuy
    override _.GetNonZeroBytes(data: byte[]) =
        for i in 0 .. data.Length - 1 do
            data[i] <- 0xFFuy

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
    use rng = new FixedRandomNumberGenerator()
    use factory = new UuidV7Factory(timeProvider, rng, CounterOverflowBehavior.SpinWait)
    
    let startCounter = 0x7FF
    let maxCounter = 0xFFF
    let iterations = (maxCounter - startCounter) + 2
    
    let task = Task.Run(fun () ->
        for _ in 1..iterations do
            factory.NewGuid() |> ignore
    )
    
    // Without advancing time, the overflow should block
    let completedEarly = task.Wait(50)
    Assert.False(completedEarly)
    
    // Advance time to release the SpinWait
    timeProvider.Advance(TimeSpan.FromMilliseconds(1.0))
    
    let completed = task.Wait(1000)
    if not completed then
        // Ensure we do not leave a spinning task behind
        timeProvider.Advance(TimeSpan.FromMilliseconds(1.0))
    Assert.True(completed)

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
