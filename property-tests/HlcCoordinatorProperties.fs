module Clockworks.PropertyTests.HlcCoordinatorProperties

open System
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
