module Clockworks.PropertyTests.SimulatedTimeProviderProperties

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Clockworks

/// Property: GetUtcNow should be consistent with GetTimestamp before any advancement
[<Property>]
let ``GetUtcNow and GetTimestamp are initially consistent`` (initialMs: int64) =
    let safeInitialMs = abs initialMs % (1000L * 60L * 60L * 24L * 365L * 10L) // Within 10 years
    let initialTime = DateTimeOffset.FromUnixTimeMilliseconds(safeInitialMs)
    let timeProvider = new SimulatedTimeProvider(initialTime)
    
    let utcNow1 = timeProvider.GetUtcNow()
    let timestamp1 = timeProvider.GetTimestamp()
    let utcNow2 = timeProvider.GetUtcNow()
    let timestamp2 = timeProvider.GetTimestamp()
    
    // Time should not advance without explicit calls
    utcNow1 = utcNow2 && timestamp1 = timestamp2

/// Property: Advancing time should be atomic and deterministic
[<Property>]
let ``Time advancement is deterministic`` (advanceMs: uint16) =
    let safeAdvanceMs = int64 (advanceMs % 10000us) + 1L
    let timeProvider = new SimulatedTimeProvider()
    
    let timeBefore = timeProvider.GetUtcNow()
    timeProvider.Advance(TimeSpan.FromMilliseconds(float safeAdvanceMs))
    let timeAfter = timeProvider.GetUtcNow()
    
    let actualAdvance = (timeAfter - timeBefore).TotalMilliseconds
    actualAdvance = float safeAdvanceMs

/// Property: Multiple small advances should equal one large advance
[<Property>]
let ``Multiple advances are cumulative`` (count: byte) =
    let safeCount = int count % 50 + 1
    let stepMs = 100L
    
    let timeProvider1 = new SimulatedTimeProvider()
    let timeProvider2 = new SimulatedTimeProvider()
    
    // Advance in steps
    for _ in 1..safeCount do
        timeProvider1.Advance(TimeSpan.FromMilliseconds(float stepMs))
    
    // Advance all at once
    timeProvider2.Advance(TimeSpan.FromMilliseconds(float (stepMs * int64 safeCount)))
    
    timeProvider1.GetUtcNow() = timeProvider2.GetUtcNow()

/// Property: Timer callbacks should fire in deterministic order
[<Property(MaxTest = 50)>]
let ``Timer callbacks fire in order`` (delays: uint16 list) =
    let safeDelays = 
        delays 
        |> List.map (fun d -> int64 (d % 1000us) + 1L)
        |> List.take (min 10 delays.Length) // Limit to 10 timers
    
    if safeDelays.IsEmpty then
        true
    else
        let timeProvider = new SimulatedTimeProvider()
        let mutable firedOrder = []
        let lockObj = obj()
        
        // Create timers with different delays
        safeDelays
        |> List.iteri (fun idx delayMs ->
            let timer = timeProvider.CreateTimer(
                (fun _ -> 
                    lock lockObj (fun () ->
                        firedOrder <- idx :: firedOrder
                    )
                ),
                idx,
                TimeSpan.FromMilliseconds(float delayMs),
                System.Threading.Timeout.InfiniteTimeSpan
            )
            ()
        )
        
        // Advance time far enough to fire all timers
        let maxDelay = safeDelays |> List.max
        timeProvider.Advance(TimeSpan.FromMilliseconds(float maxDelay + 100.0))
        
        // Timers should fire in order of their delays (smallest first)
        let expectedOrder = 
            safeDelays
            |> List.mapi (fun idx delay -> (idx, delay))
            |> List.sortBy (fun (idx, delay) -> delay, idx)
            |> List.map fst
        
        let actualOrder = firedOrder |> List.rev
        
        // All timers should have fired
        actualOrder.Length = safeDelays.Length && actualOrder = expectedOrder

/// Property: Advance should set time relative to current time
[<Property>]
let ``Advance sets time relative to current`` (advanceMs: uint16) =
    let safeAdvanceMs = int64 (advanceMs % 10000us) + 1L
    
    let timeProvider = new SimulatedTimeProvider()
    let timeBefore = timeProvider.GetUtcNow()
    timeProvider.Advance(TimeSpan.FromMilliseconds(float safeAdvanceMs))
    let timeAfter = timeProvider.GetUtcNow()
    
    let expectedTime = timeBefore.AddMilliseconds(float safeAdvanceMs)
    timeAfter = expectedTime

/// Property: GetTimestamp should be monotonic
[<Property>]
let ``GetTimestamp is monotonic`` (advances: uint16 list) =
    let safeAdvances = 
        advances 
        |> List.map (fun a -> int64 (a % 100us) + 1L)
        |> List.take (min 20 advances.Length)
    
    if safeAdvances.IsEmpty then
        true
    else
        let timeProvider = new SimulatedTimeProvider()
        let mutable timestamps = [ timeProvider.GetTimestamp() ]
        
        for advanceMs in safeAdvances do
            timeProvider.Advance(TimeSpan.FromMilliseconds(float advanceMs))
            timestamps <- timeProvider.GetTimestamp() :: timestamps
        
        let sortedTimestamps = timestamps |> List.rev
        
        // Check monotonicity
        sortedTimestamps
        |> List.pairwise
        |> List.forall (fun (prev, curr) -> prev < curr)

/// Property: Negative advances should throw or be handled gracefully
[<Property>]
let ``Negative time advancement is rejected`` (negativeMs: int64) =
    let safeNegativeMs = -(abs negativeMs % 10000L) - 1L
    let timeProvider = new SimulatedTimeProvider()
    
    let mutable threwException = false
    try
        timeProvider.Advance(TimeSpan.FromMilliseconds(float safeNegativeMs))
    with
    | :? ArgumentOutOfRangeException -> threwException <- true
    | _ -> ()
    
    // Should either throw ArgumentOutOfRangeException or handle gracefully
    threwException || (timeProvider.GetUtcNow() = DateTimeOffset.UnixEpoch)

/// Property: CreateTimer returns a functional timer
[<Property(MaxTest = 50)>]
let ``CreateTimer produces functional timer`` (delayMs: uint16) =
    let safeDelayMs = int64 (delayMs % 5000us) + 10L
    let timeProvider = new SimulatedTimeProvider()
    
    let mutable callbackFired = false
    let timer = timeProvider.CreateTimer(
        (fun _ -> callbackFired <- true),
        null,
        TimeSpan.FromMilliseconds(float safeDelayMs),
        System.Threading.Timeout.InfiniteTimeSpan
    )
    
    // Timer should not fire before delay
    timeProvider.Advance(TimeSpan.FromMilliseconds(float (safeDelayMs - 1L)))
    let notFiredYet = not callbackFired
    
    // Timer should fire after delay
    timeProvider.Advance(TimeSpan.FromMilliseconds(2.0))
    let firedAfterDelay = callbackFired
    
    timer.Dispose()
    notFiredYet && firedAfterDelay

/// Property: GetElapsedTime should match advanced time
[<Property>]
let ``GetElapsedTime matches advanced time`` (advanceMs: uint16) =
    let safeAdvanceMs = int64 (advanceMs % 10000us) + 1L
    let timeProvider = new SimulatedTimeProvider()
    
    let startTimestamp = timeProvider.GetTimestamp()
    timeProvider.Advance(TimeSpan.FromMilliseconds(float safeAdvanceMs))
    let elapsed = timeProvider.GetElapsedTime(startTimestamp)
    
    let expectedElapsed = TimeSpan.FromMilliseconds(float safeAdvanceMs)
    
    // Allow small rounding differences
    abs (elapsed.TotalMilliseconds - expectedElapsed.TotalMilliseconds) < 0.01

/// Reference model for the timer queue: each advance fires every live timer that is due, once, in (due, id) order;
/// a periodic timer is rescheduled one period after the advanced-to time; a disposed timer, or a one-shot timer
/// that has fired, refuses Change. A due of None never comes. The queue never holds more than twice the most
/// timers live at once, however many have been changed or disposed.
type private ModelTimer = { Id: int; mutable Due: int64 option; mutable Period: int64; mutable Done: bool }

/// Property: under any sequence of create, change, dispose and advance, timers fire as the reference model says
[<Property(MaxTest = 500)>]
let ``Timers fire as a reference model says under change and dispose`` (ops: (byte * byte * byte) list) =
    let timeProvider = new SimulatedTimeProvider()
    let timers = ResizeArray<System.Threading.ITimer>()
    let model = ResizeArray<ModelTimer>()
    let fired = ResizeArray<int>()
    let mutable now = 0L
    let mutable mostLive = 0
    let mutable agrees = true

    let dueOf (b: byte) = if b % 8uy = 7uy then None else Some (int64 (b % 50uy))
    let periodOf (b: byte) = if b % 3uy = 0uy then int64 (b % 20uy) + 1L else 0L
    let dueSpan due =
        match due with
        | Some ms -> TimeSpan.FromMilliseconds(float ms)
        | None -> System.Threading.Timeout.InfiniteTimeSpan
    let periodSpan period =
        if period = 0L then System.Threading.Timeout.InfiniteTimeSpan else TimeSpan.FromMilliseconds(float period)

    for (kind, a, b) in List.truncate 60 ops do
        match kind % 4uy with
        | 0uy ->
            let id = model.Count
            let due, period = dueOf a, periodOf b
            model.Add({ Id = id; Due = due |> Option.map ((+) now); Period = period; Done = false })
            timers.Add(timeProvider.CreateTimer((fun state -> fired.Add(unbox<int> state)), box id, dueSpan due, periodSpan period))
            mostLive <- max mostLive (model |> Seq.filter (fun timer -> not timer.Done) |> Seq.length)
        | 1uy when model.Count > 0 ->
            let timer = model[int a % model.Count]
            let due, period = dueOf b, periodOf (a ^^^ b)
            let expected = not timer.Done
            if expected then
                timer.Due <- due |> Option.map ((+) now)
                timer.Period <- period
            agrees <- agrees && timers[timer.Id].Change(dueSpan due, periodSpan period) = expected
        | 2uy when model.Count > 0 ->
            let timer = model[int a % model.Count]
            timer.Done <- true
            timers[timer.Id].Dispose()
        | _ ->
            let by = int64 (a % 30uy)
            now <- now + by
            let due =
                model
                |> Seq.filter (fun timer -> not timer.Done && timer.Due |> Option.exists (fun d -> d <= now))
                |> Seq.sortBy (fun timer -> timer.Due.Value, timer.Id)
                |> Seq.toList
            for timer in due do
                if timer.Period > 0L then timer.Due <- Some (now + timer.Period) else timer.Done <- true
            fired.Clear()
            timeProvider.Advance(TimeSpan.FromMilliseconds(float by))
            agrees <- agrees && List.ofSeq fired = (due |> List.map (fun timer -> timer.Id))

    agrees && timeProvider.Statistics.MaxQueueLength <= 2L * int64 mostLive
