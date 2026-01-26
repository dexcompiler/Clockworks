module Clockworks.PropertyTests.VectorClockCoordinatorProperties

open FsCheck.FSharp
open FsCheck.Xunit
open Clockworks.Distributed

type VectorClockArb =
    static member VectorClock() : FsCheck.Arbitrary<VectorClock> =
        let nodeIdGen = Gen.choose (0, 50) |> Gen.map uint16
        let counterGen = Gen.choose (0, 1000) |> Gen.map uint64
        let pairGen = Gen.zip nodeIdGen counterGen

        let listGen =
            Gen.choose (0, 10)
            |> Gen.bind (fun length -> Gen.listOfLength length pairGen)

        listGen
        |> Gen.map (fun pairs ->
            if List.isEmpty pairs then
                VectorClock()
            else
                let entries =
                    pairs
                    |> List.map (fun (nodeId, counter) -> string nodeId + ":" + string counter)
                    |> String.concat ","
                VectorClock.Parse(entries))
        |> Arb.fromGen

/// Property: BeforeSend increments the local node counter and advances the clock.
[<Property>]
let ``BeforeSend increments local counter`` (nodeId: uint16) =
    let coordinator = new VectorClockCoordinator(nodeId)
    let before = coordinator.Current
    let sent = coordinator.BeforeSend()
    let after = coordinator.Current

    sent = after
    && after.Compare(before) = VectorClockOrder.After
    && after.Get(nodeId) = before.Get(nodeId) + 1UL

/// Property: BeforeReceive merges the remote clock then increments the local counter.
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``BeforeReceive merges then increments local counter`` (remote: VectorClock) (nodeId: uint16) =
    let coordinator = new VectorClockCoordinator(nodeId)
    let before = coordinator.Current

    coordinator.BeforeReceive(remote)
    let after = coordinator.Current
    let merged = before.Merge(remote)

    after.Compare(merged) = VectorClockOrder.After
    && after.Get(nodeId) = merged.Get(nodeId) + 1UL

/// Property: Receive then send preserves causality across nodes.
[<Property>]
let ``Receive then send preserves causality`` (extraA: byte) (extraB: byte) =
    let coordinatorA = new VectorClockCoordinator(1us)
    let coordinatorB = new VectorClockCoordinator(2us)

    for _ in 1..(int extraA % 5) do
        coordinatorA.NewLocalEvent()

    for _ in 1..(int extraB % 5) do
        coordinatorB.NewLocalEvent()

    let sent = coordinatorA.BeforeSend()
    coordinatorB.BeforeReceive(sent)
    let receivedThenSent = coordinatorB.BeforeSend()

    sent.HappensBefore(receivedThenSent)

/// Property: Independent sends from distinct nodes are concurrent.
[<Property>]
let ``Independent sends are concurrent`` (nodeA: uint16) (nodeB: uint16) =
    let nodeB' = if nodeB = nodeA then nodeB + 1us else nodeB
    let coordinatorA = new VectorClockCoordinator(nodeA)
    let coordinatorB = new VectorClockCoordinator(nodeB')

    let sendA = coordinatorA.BeforeSend()
    let sendB = coordinatorB.BeforeSend()

    sendA.IsConcurrentWith(sendB)
