module Clockworks.PropertyTests.HlcTimestampProperties

open Xunit
open System
open FsCheck.FSharp
open FsCheck.Xunit
open Clockworks.Distributed

let private compareBytesLex (a: byte[]) (b: byte[]) =
    let len = min a.Length b.Length
    let mutable i = 0
    let mutable cmp = 0
    while i < len && cmp = 0 do
        cmp <- compare a[i] b[i]
        i <- i + 1
    if cmp <> 0 then cmp else compare a.Length b.Length

type HlcTimestampArb =
    static member HlcTimestamp() : FsCheck.Arbitrary<HlcTimestamp> =
        let maxWallTime = (1L <<< 48) - 1L
        let wallTimeGen = Gen.choose64 (0L, maxWallTime)
        let counterGen = Gen.choose (0, 0x0FFF) |> Gen.map uint16
        let nodeIdGen = Gen.choose (0, 0x000F) |> Gen.map uint16

        Gen.zip3 wallTimeGen counterGen nodeIdGen
        |> Gen.map (fun (wallTimeMs, counter, nodeId) -> HlcTimestamp(wallTimeMs, counter, nodeId))
        |> Arb.fromGen

/// Property: Packing and unpacking a timestamp should be an identity operation
[<Property>]
let ``ToPackedInt64 and FromPackedInt64 form a round-trip`` (wallTimeMs: int64) (counter: uint16) (nodeId: uint16) =
    // Only test non-negative wall times
    let safeWallTimeMs = abs wallTimeMs % (1L <<< 47) // Avoid sign-bit issues in packed long
    let safeCounter = counter &&& 0x0FFFus // 12-bit limit
    let safeNodeId = nodeId &&& 0x000Fus // 4-bit limit
    
    let original = HlcTimestamp(safeWallTimeMs, safeCounter, safeNodeId)
    let packed = original.ToPackedInt64()
    let unpacked = HlcTimestamp.FromPackedInt64(packed)
    
    unpacked = original

/// Property: Comparison must be transitive: if a <= b and b <= c, then a <= c
[<Property(Arbitrary = [| typeof<HlcTimestampArb> |])>]
let ``HlcTimestamp comparison is transitive`` (a: HlcTimestamp) (b: HlcTimestamp) (c: HlcTimestamp) =
    not (a.CompareTo(b) <= 0 && b.CompareTo(c) <= 0) || (a.CompareTo(c) <= 0)

/// Property: Comparison must be reflexive: a <= a
[<Property(Arbitrary = [| typeof<HlcTimestampArb> |])>]
let ``HlcTimestamp comparison is reflexive`` (ts: HlcTimestamp) =
    ts.CompareTo(ts) = 0 && ts = ts

/// Property: Comparison must be antisymmetric: if a <= b and b <= a, then a = b
[<Property(Arbitrary = [| typeof<HlcTimestampArb> |])>]
let ``HlcTimestamp comparison is antisymmetric`` (a: HlcTimestamp) (b: HlcTimestamp) =
    not (a.CompareTo(b) <= 0 && b.CompareTo(a) <= 0) || (a = b)

/// Property: Timestamps with higher wall time should always be greater
[<Property>]
let ``Higher wall time means greater timestamp`` (wallTimeMs1: int64) (wallTimeMs2: int64) =
    let safeWallTimeMs1 = abs wallTimeMs1 % (1L <<< 47)
    let safeWallTimeMs2 = abs wallTimeMs2 % (1L <<< 47)
    
    (safeWallTimeMs1 = safeWallTimeMs2) || 
    (
        let ts1 = HlcTimestamp(safeWallTimeMs1)
        let ts2 = HlcTimestamp(safeWallTimeMs2)
        (safeWallTimeMs1 < safeWallTimeMs2) = (ts1.CompareTo(ts2) < 0)
    )

/// Property: For equal wall times, higher counter means greater timestamp
[<Property>]
let ``For equal wall time, higher counter means greater timestamp`` (wallTimeMs: int64) (counter1: uint16) (counter2: uint16) =
    let safeWallTimeMs = abs wallTimeMs % (1L <<< 47)
    
    (counter1 = counter2) ||
    (
        let ts1 = HlcTimestamp(safeWallTimeMs, counter1)
        let ts2 = HlcTimestamp(safeWallTimeMs, counter2)
        (counter1 < counter2) = (ts1.CompareTo(ts2) < 0)
    )

/// Property: For equal wall time and counter, higher nodeId means greater timestamp
[<Property>]
let ``For equal wall time and counter, higher nodeId means greater timestamp`` 
    (wallTimeMs: int64) (counter: uint16) (nodeId1: uint16) (nodeId2: uint16) =
    let safeWallTimeMs = abs wallTimeMs % (1L <<< 47)
    
    (nodeId1 = nodeId2) ||
    (
        let ts1 = HlcTimestamp(safeWallTimeMs, counter, nodeId1)
        let ts2 = HlcTimestamp(safeWallTimeMs, counter, nodeId2)
        (nodeId1 < nodeId2) = (ts1.CompareTo(ts2) < 0)
    )

/// Property: Packed representation preserves ordering
[<Property(Arbitrary = [| typeof<HlcTimestampArb> |])>]
let ``Packed representation preserves ordering`` (a: HlcTimestamp) (b: HlcTimestamp) =
    let packedA = uint64 (a.ToPackedInt64())
    let packedB = uint64 (b.ToPackedInt64())
    a.CompareTo(b) = compare packedA packedB

/// Property: WriteTo and ReadFrom should round-trip (full 80-bit encoding)
[<Property>]
let ``WriteTo and ReadFrom form a round-trip`` (wallTimeMs: int64) (counter: uint16) (nodeId: uint16) =
    let safeWallTimeMs = abs wallTimeMs % (1L <<< 48)
    let original = HlcTimestamp(safeWallTimeMs, counter, nodeId)
    
    let buffer = Array.zeroCreate<byte> 10
    original.WriteTo(System.Span<byte>(buffer))
    let unpacked = HlcTimestamp.ReadFrom(System.ReadOnlySpan<byte>(buffer))
    
    unpacked = original

/// Property: Packing masks counter/nodeId to their bit-widths (12-bit counter, 4-bit node)
[<Property>]
let ``ToPackedInt64 truncates counter and node id`` (wallTimeMs: int64) (counter: uint16) (nodeId: uint16) =
    let safeWallTimeMs = abs wallTimeMs % (1L <<< 47) // avoid sign-bit issues when casting packed to long
    let original = HlcTimestamp(safeWallTimeMs, counter, nodeId)
    let unpacked = HlcTimestamp.FromPackedInt64(original.ToPackedInt64())

    unpacked.WallTimeMs = safeWallTimeMs
    && unpacked.Counter = (counter &&& 0x0FFFus)
    && unpacked.NodeId = (nodeId &&& 0x000Fus)

/// Property: String representation round-trips through Parse
[<Property(Arbitrary = [| typeof<HlcTimestampArb> |])>]
let ``ToString and Parse round-trip`` (ts: HlcTimestamp) =
    HlcTimestamp.Parse(ts.ToString()) = ts

/// Property: String representation round-trips through TryParse
[<Property(Arbitrary = [| typeof<HlcTimestampArb> |])>]
let ``ToString and TryParse round-trip`` (ts: HlcTimestamp) =
    let ok, parsed = HlcTimestamp.TryParse(ts.ToString().AsSpan())
    ok && parsed = ts

/// Property: TryParse never throws and only returns true when output is stable
[<Property>]
let ``TryParse is non-throwing`` (s: string) =
    try
        let ok, parsed = HlcTimestamp.TryParse(s.AsSpan())
        (not ok) || (parsed.ToString() = s)
    with
    | _ -> false

/// Property: Operators are consistent with CompareTo
[<Property(Arbitrary = [| typeof<HlcTimestampArb> |])>]
let ``Operators are consistent with CompareTo`` (a: HlcTimestamp) (b: HlcTimestamp) =
    (a < b) = (a.CompareTo(b) < 0)
    && (a > b) = (a.CompareTo(b) > 0)
    && (a <= b) = (a.CompareTo(b) <= 0)
    && (a >= b) = (a.CompareTo(b) >= 0)

/// Property: Big-endian encoding preserves ordering under lexicographic byte comparison
[<Property(Arbitrary = [| typeof<HlcTimestampArb> |])>]
let ``WriteTo encoding preserves lexicographic ordering`` (a: HlcTimestamp) (b: HlcTimestamp) =
    let bufferA = Array.zeroCreate<byte> 10
    let bufferB = Array.zeroCreate<byte> 10
    a.WriteTo(Span<byte>(bufferA))
    b.WriteTo(Span<byte>(bufferB))

    let cmpTs = a.CompareTo(b)
    let cmpBytes = compareBytesLex bufferA bufferB
    sign cmpTs = sign cmpBytes
