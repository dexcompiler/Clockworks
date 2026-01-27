module Clockworks.PropertyTests.HlcCoordinatorProperties

open System
open FsCheck.FSharp
open FsCheck
open FsCheck.Xunit
open Clockworks
open Clockworks.Distributed

/// Property: Sequential sends should always produce increasing timestamps.
[<Property(MaxTest = 100)>]
let ``BeforeSend timestamps are strictly increasing`` (advances: uint16 list) =
    let safeAdvances =
        advances
        |> List.map (fun ms -> int64 (ms % 5us))
        |> List.truncate 20

    let steps = if List.isEmpty safeAdvances then [0L] else safeAdvances
    let timeProvider = new SimulatedTimeProvider()
    use factory = new HlcGuidFactory(timeProvider, nodeId = 1us)
    let coordinator = new HlcCoordinator(factory)

    let mutable timestamps = [ coordinator.BeforeSend() ]
    for advanceMs in steps do
        if advanceMs > 0L then
            timeProvider.Advance(TimeSpan.FromMilliseconds(float advanceMs))
        timestamps <- coordinator.BeforeSend() :: timestamps

    timestamps
    |> List.rev
    |> List.pairwise
    |> List.forall (fun (prev, curr) -> prev < curr)

/// Property: Receiving any remote timestamp always advances the local timestamp.
[<Property>]
let ``BeforeReceive advances the local timestamp`` (delta: int) =
    let startMs = 1_700_000_000_000L
    let timeProvider = SimulatedTimeProvider.FromUnixMs(startMs)
    use factory = new HlcGuidFactory(timeProvider, nodeId = 1us)
    let coordinator = new HlcCoordinator(factory)

    let before = coordinator.CurrentTimestamp
    let deltaMs = (abs (int64 delta) % 2000L) - 1000L
    let remoteMs = before.WallTimeMs + deltaMs

    coordinator.BeforeReceive(HlcTimestamp(remoteMs))

    let after = coordinator.CurrentTimestamp
    after > before

/// Property: When the remote wall time is ahead, we adopt it and start counter at 1.
[<Property>]
let ``BeforeReceive adopts remote wall time when remote is ahead`` (deltaMs: uint16) =
    let startMs = 1_700_000_000_000L
    let timeProvider = SimulatedTimeProvider.FromUnixMs(startMs)
    use factory = new HlcGuidFactory(timeProvider, nodeId = 1us)
    let coordinator = new HlcCoordinator(factory)

    let advanceMs = int64 (deltaMs % 1000us) + 1L
    let remote = HlcTimestamp(coordinator.CurrentTimestamp.WallTimeMs + advanceMs)

    coordinator.BeforeReceive(remote)

    let after = coordinator.CurrentTimestamp
    after.WallTimeMs = remote.WallTimeMs && after.Counter = 1us

/// Property: When remote is ahead within the same wall-time, we should adopt the higher counter (and exceed it).
[<Property>]
let ``BeforeReceive adopts remote counter when remote is ahead at same wall time`` (remoteCounter: uint16) =
    let startMs = 1_700_000_000_000L
    let timeProvider = SimulatedTimeProvider.FromUnixMs(startMs)
    use factory = new HlcGuidFactory(timeProvider, nodeId = 1us)
    let coordinator = new HlcCoordinator(factory)

    // Ensure we have a stable local wall time.
    let _ = coordinator.BeforeSend()
    let local = coordinator.CurrentTimestamp

    let safeRemoteCounter = remoteCounter &&& 0x0FFFus

    // Make the remote strictly greater than local, still at same wall time.
    let remote =
        if safeRemoteCounter > local.Counter then
            HlcTimestamp(local.WallTimeMs, counter = safeRemoteCounter, nodeId = 2us)
        else
            HlcTimestamp(local.WallTimeMs, counter = local.Counter + 1us, nodeId = 2us)

    coordinator.BeforeReceive(remote)
    let after = coordinator.CurrentTimestamp

    after.WallTimeMs = remote.WallTimeMs
    && after.Counter = remote.Counter + 1us
    && after > remote

/// Property: When remote is ahead only by nodeId (same wall time and counter), witnessing should still advance.
[<Property>]
let ``BeforeReceive uses nodeId as tie-breaker`` () =
    let startMs = 1_700_000_000_000L
    let timeProvider = SimulatedTimeProvider.FromUnixMs(startMs)
    use factory = new HlcGuidFactory(timeProvider, nodeId = 1us)
    let coordinator = new HlcCoordinator(factory)

    // Put local at a known (wall,counter).
    let _ = coordinator.BeforeSend()
    let local = coordinator.CurrentTimestamp

    // Same wall/counter but higher node id => remote > local by timestamp ordering.
    let remote = HlcTimestamp(local.WallTimeMs, counter = local.Counter, nodeId = local.NodeId + 1us)

    coordinator.BeforeReceive(remote)
    let after = coordinator.CurrentTimestamp

    after.WallTimeMs = remote.WallTimeMs
    && after.Counter = remote.Counter + 1us
    && after > remote

/// Property: Receive followed by send yields a timestamp after the remote.
[<Property>]
let ``Receive then send yields timestamp after remote`` (deltaMs: uint16) =
    let startMs = 1_700_000_000_000L
    let timeProvider = SimulatedTimeProvider.FromUnixMs(startMs)
    use factory = new HlcGuidFactory(timeProvider, nodeId = 1us)
    let coordinator = new HlcCoordinator(factory)

    let advanceMs = int64 (deltaMs % 1000us) + 1L
    let remote = HlcTimestamp(coordinator.CurrentTimestamp.WallTimeMs + advanceMs, counter = 0us, nodeId = 2us)

    coordinator.BeforeReceive(remote)
    let next = coordinator.BeforeSend()

    next > remote

/// Property: If physical time jumps ahead, the counter resets to zero on send.
[<Property>]
let ``BeforeSend resets counter when physical time jumps ahead`` (jumpMs: uint16) =
    let jump = int64 (jumpMs % 10_000us) + 1L
    let timeProvider = new SimulatedTimeProvider()
    use factory = new HlcGuidFactory(timeProvider, nodeId = 1us)
    let coordinator = new HlcCoordinator(factory)

    let _ = coordinator.BeforeSend()
    let _ = coordinator.BeforeSend()
    let before = coordinator.CurrentTimestamp

    let newPhysical = DateTimeOffset.FromUnixTimeMilliseconds(before.WallTimeMs + jump)
    timeProvider.SetUtcNow(newPhysical)

    let after = coordinator.BeforeSend()

    after.WallTimeMs = newPhysical.ToUnixTimeMilliseconds()
    && after.Counter = 0us
    && after > before

/// Property: When the remote timestamp is behind the local timestamp, local wall time should not change.
[<Property>]
let ``BeforeReceive with remote behind does not change wall time`` (behindMs: uint16) =
    let startMs = 1_700_000_000_000L
    let timeProvider = SimulatedTimeProvider.FromUnixMs(startMs)
    use factory = new HlcGuidFactory(timeProvider, nodeId = 1us)
    let coordinator = new HlcCoordinator(factory)

    let _ = coordinator.BeforeSend()
    let _ = coordinator.BeforeSend()
    let before = coordinator.CurrentTimestamp

    let delta = int64 (behindMs % 1000us) + 1L
    let remote = HlcTimestamp(before.WallTimeMs - delta, counter = 0us, nodeId = 2us)

    coordinator.BeforeReceive(remote)
    let after = coordinator.CurrentTimestamp

    after.WallTimeMs = before.WallTimeMs
    && after > before

type HlcStep =
    | Send
    | ReceiveDeltaMs of int
    | PhysicalAdvanceMs of int
    | PhysicalSetDeltaMs of int

type HlcStepArb =
    static member HlcStep() : Arbitrary<HlcStep> =
        let gen =
            Gen.frequency
                [ 5, Gen.constant Send
                  4, Gen.choose (-2000, 2000) |> Gen.map ReceiveDeltaMs
                  2, Gen.choose (0, 500) |> Gen.map PhysicalAdvanceMs
                  2, Gen.choose (-2000, 2000) |> Gen.map PhysicalSetDeltaMs ]
        Arb.fromGen gen

/// Property: Mixed sequences of send/receive/time-changes never break monotonicity of successive sends,
/// and each receive advances local timestamp.
[<Property(Arbitrary = [| typeof<HlcStepArb> |], MaxTest = 100)>]
let ``Mixed send/receive/time steps preserve invariants`` (steps: HlcStep list) =
    let startMs = 1_700_000_000_000L
    let tp = SimulatedTimeProvider.FromUnixMs(startMs)
    use factory = new HlcGuidFactory(tp, nodeId = 1us, options = HlcOptions.HighThroughput)
    let coord = new HlcCoordinator(factory)

    let mutable lastSend : HlcTimestamp option = None
    let mutable ok = true

    for step in (steps |> List.truncate 100) do
        match step with
        | Send ->
            let t = coord.BeforeSend()
            match lastSend with
            | None -> lastSend <- Some t
            | Some prev ->
                ok <- ok && (prev < t)
                lastSend <- Some t

        | ReceiveDeltaMs delta ->
            let before = coord.CurrentTimestamp
            let remote = HlcTimestamp(before.WallTimeMs + int64 delta, counter = 0us, nodeId = 2us)
            coord.BeforeReceive(remote)
            let after = coord.CurrentTimestamp
            ok <- ok && (after > before)

        | PhysicalAdvanceMs ms ->
            tp.Advance(TimeSpan.FromMilliseconds(float ms))

        | PhysicalSetDeltaMs delta ->
            let now = tp.GetUtcNow().ToUnixTimeMilliseconds()
            tp.SetUnixMs(now + int64 delta)

    ok
