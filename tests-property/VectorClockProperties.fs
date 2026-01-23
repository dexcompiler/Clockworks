module Clockworks.PropertyTests.VectorClockProperties

open FsCheck
open FsCheck.Xunit
open Clockworks.Distributed

type VectorClockArb =
    static member VectorClock() : Arbitrary<VectorClock> =
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

/// Property: Merge should be commutative
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Merge is commutative`` (a: VectorClock) (b: VectorClock) =
    a.Merge(b) = b.Merge(a)

/// Property: Merge should be associative
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Merge is associative`` (a: VectorClock) (b: VectorClock) (c: VectorClock) =
    a.Merge(b).Merge(c) = a.Merge(b.Merge(c))

/// Property: Merge should be idempotent
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Merge is idempotent`` (a: VectorClock) =
    a.Merge(a) = a

/// Property: Merge should dominate its inputs
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Merge dominates inputs`` (a: VectorClock) (b: VectorClock) =
    let merged = a.Merge(b)
    let relationToA = merged.Compare(a)
    let relationToB = merged.Compare(b)

    (relationToA = VectorClockOrder.After || relationToA = VectorClockOrder.Equal)
    && (relationToB = VectorClockOrder.After || relationToB = VectorClockOrder.Equal)

/// Property: Parsing a clock from its string form should be a round-trip
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Parse and ToString round-trip`` (clock: VectorClock) =
    VectorClock.Parse(clock.ToString()) = clock

/// Property: WriteTo and ReadFrom should round-trip
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``WriteTo and ReadFrom round-trip`` (clock: VectorClock) =
    let buffer = Array.zeroCreate<byte> (clock.GetBinarySize())
    clock.WriteTo(buffer.AsSpan())
    VectorClock.ReadFrom(buffer.AsSpan()) = clock

/// Property: Increment should advance the clock for that node
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Increment moves clock forward`` (clock: VectorClock) (nodeId: uint16) =
    let incremented = clock.Increment(nodeId)
    incremented.Compare(clock) = VectorClockOrder.After
