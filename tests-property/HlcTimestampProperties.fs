module Clockworks.PropertyTests.HlcTimestampProperties

open Xunit
open FsCheck.Xunit
open Clockworks.Distributed

/// Property: Packing and unpacking a timestamp should be an identity operation
[<Property>]
let ``ToPackedInt64 and FromPackedInt64 form a round-trip`` (wallTimeMs: int64) (counter: uint16) (nodeId: uint16) =
    // Only test non-negative wall times
    let safeWallTimeMs = abs wallTimeMs % (1L <<< 48) // 48-bit limit
    let safeCounter = counter &&& 0x0FFFus // 12-bit limit
    let safeNodeId = nodeId &&& 0x000Fus // 4-bit limit
    
    let original = HlcTimestamp(safeWallTimeMs, safeCounter, safeNodeId)
    let packed = original.ToPackedInt64()
    let unpacked = HlcTimestamp.FromPackedInt64(packed)
    
    unpacked = original

/// Property: Comparison must be transitive: if a <= b and b <= c, then a <= c
[<Property>]
let ``HlcTimestamp comparison is transitive`` (a: HlcTimestamp) (b: HlcTimestamp) (c: HlcTimestamp) =
    not (a.CompareTo(b) <= 0 && b.CompareTo(c) <= 0) || (a.CompareTo(c) <= 0)

/// Property: Comparison must be reflexive: a <= a
[<Property>]
let ``HlcTimestamp comparison is reflexive`` (ts: HlcTimestamp) =
    ts.CompareTo(ts) = 0 && ts = ts

/// Property: Comparison must be antisymmetric: if a <= b and b <= a, then a = b
[<Property>]
let ``HlcTimestamp comparison is antisymmetric`` (a: HlcTimestamp) (b: HlcTimestamp) =
    not (a.CompareTo(b) <= 0 && b.CompareTo(a) <= 0) || (a = b)

/// Property: Total ordering - for any two timestamps, one must be less than, equal to, or greater than the other
[<Property>]
let ``HlcTimestamp has total ordering`` (a: HlcTimestamp) (b: HlcTimestamp) =
    let cmp = a.CompareTo(b)
    (cmp < 0) || (cmp = 0) || (cmp > 0)

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
[<Property>]
let ``Packed representation preserves ordering`` (a: HlcTimestamp) (b: HlcTimestamp) =
    let packedA = a.ToPackedInt64()
    let packedB = b.ToPackedInt64()
    (a.CompareTo(b) < 0) = (packedA < packedB)

/// Property: WriteTo and ReadFrom should round-trip (full 80-bit encoding)
[<Property>]
let ``WriteTo and ReadFrom form a round-trip`` (wallTimeMs: int64) (counter: uint16) (nodeId: uint16) =
    let safeWallTimeMs = abs wallTimeMs % (1L <<< 48)
    let original = HlcTimestamp(safeWallTimeMs, counter, nodeId)
    
    let buffer = Array.zeroCreate<byte> 10
    original.WriteTo(System.Span<byte>(buffer))
    let unpacked = HlcTimestamp.ReadFrom(System.ReadOnlySpan<byte>(buffer))
    
    unpacked = original
