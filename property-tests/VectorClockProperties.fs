module Clockworks.PropertyTests.VectorClockProperties

open System
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
    clock.WriteTo(System.Span<byte>(buffer))
    VectorClock.ReadFrom(System.ReadOnlySpan<byte>(buffer)) = clock

/// Property: Increment should advance the clock for that node
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Increment moves clock forward`` (clock: VectorClock) (nodeId: uint16) =
    let incremented = clock.Increment(nodeId)
    incremented.Compare(clock) = VectorClockOrder.After

/// Property: If a is before-or-equal b, Merge(a,b) = b (b is an upper bound)
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Merge returns the upper bound when one input dominates`` (a: VectorClock) (b: VectorClock) =
    match a.Compare(b) with
    | VectorClockOrder.Before
    | VectorClockOrder.Equal -> a.Merge(b) = b
    | VectorClockOrder.After -> a.Merge(b) = a
    | VectorClockOrder.Concurrent -> true
    | _ -> true

/// Property: Merge is monotone: merged clock is never before either input
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Merge is monotone with respect to inputs`` (a: VectorClock) (b: VectorClock) =
    let m = a.Merge(b)
    let ra = m.Compare(a)
    let rb = m.Compare(b)
    (ra = VectorClockOrder.After || ra = VectorClockOrder.Equal)
    && (rb = VectorClockOrder.After || rb = VectorClockOrder.Equal)

/// Property: Increment only advances the chosen node; merging original with incremented equals incremented
[<Property(Arbitrary = [| typeof<VectorClockArb> |])>]
let ``Increment is stable under merge with original`` (clock: VectorClock) (nodeId: uint16) =
    let inc = clock.Increment(nodeId)
    clock.Merge(inc) = inc && inc.Merge(clock) = inc
