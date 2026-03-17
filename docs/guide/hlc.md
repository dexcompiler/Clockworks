---
title: Hybrid Logical Clock
---

# Hybrid Logical Clock

A Hybrid Logical Clock (HLC) combines a physical wall-clock timestamp with a logical counter to achieve causality tracking that stays close to real time. Based on the [Kulkarni et al. 2014](https://cse.buffalo.edu/tech-reports/2014-04.pdf) paper, HLC provides O(1) causality tracking with configurable drift enforcement — making it ideal for high-throughput distributed systems where wall-clock proximity matters.

## Key Types

| Type | Description |
|---|---|
| `HlcTimestamp` | Immutable `(wallTime, counter, nodeId)` tuple with total ordering |
| `HlcGuidFactory` | Creates `HlcTimestamp` values bound to a `TimeProvider` and node ID |
| `HlcCoordinator` | Manages `BeforeSend` / `BeforeReceive` coordination for a node |
| `HlcMessageHeader` | Wire format for propagating an `HlcTimestamp` across service boundaries |
| `HlcStatistics` | Counters for send/receive operations and drift observations |
| `HlcClusterRegistry` | Manages a set of `HlcCoordinator` instances for a simulated cluster |

## Timestamp Structure

An `HlcTimestamp` is a `(wallTime, counter, nodeId)` tuple that defines a **total order**:

1. Compare `wallTime` (higher = later)
2. If equal, compare `counter` (higher = later)
3. If equal, compare `nodeId` (higher = later)

This total order means every timestamp is unambiguously before or after every other timestamp.

## Encoding Formats

### 64-bit packed (`ToPackedInt64` / `FromPackedInt64`)

An optimized single-`long` encoding suitable for compact storage:

- 48 bits for wall time (milliseconds)
- 12 bits for logical counter
- 4 bits for node ID (**node ID is truncated** — only the lowest 4 bits are preserved)

Use this when you need compact storage and don't need full node ID fidelity.

### 80-bit canonical (`WriteTo` / `ReadFrom`)

A full-fidelity 10-byte big-endian encoding:

- 48 bits for wall time
- 16 bits for counter
- 16 bits for node ID

Use this for wire formats and persistent storage where full fidelity is required.

## BeforeSend / BeforeReceive

The `HlcCoordinator` implements the two-operation protocol for causality propagation:

```csharp
var tp = new SimulatedTimeProvider();

using var aFactory = new HlcGuidFactory(tp, nodeId: 1);
using var bFactory = new HlcGuidFactory(tp, nodeId: 2);

var a = new HlcCoordinator(aFactory);
var b = new HlcCoordinator(bFactory);

// A sends a message
var t1 = a.BeforeSend();
var header = new HlcMessageHeader(t1, correlationId: Guid.NewGuid());
var headerValue = header.ToString(); // e.g. "1700000000000.0@1;{guid}"

// B receives the message — witnesses A's timestamp and advances its own
var parsed = HlcMessageHeader.Parse(headerValue);
b.BeforeReceive(parsed.Timestamp);

// B sends a reply — guaranteed to be causally after the received timestamp
var t2 = b.BeforeSend();
Console.WriteLine(t1 < t2); // true
```

`BeforeReceive` witnesses the full remote `HlcTimestamp` including its node ID for tie-breaking.

## Drift Configuration

`HlcOptions` controls how the coordinator handles clock drift:

```csharp
var tp = TimeProvider.System;

// Strict: throw HlcDriftException when drift exceeds the bound
using var strict = new HlcGuidFactory(tp, nodeId: 1, options: new HlcOptions
{
    MaxDriftMs = 1_000,
    ThrowOnExcessiveDrift = true
});

// High-throughput: silently allow drift beyond the bound (maintains monotonicity)
using var highThroughput = new HlcGuidFactory(tp, nodeId: 1, options: new HlcOptions
{
    MaxDriftMs = 60_000,
    ThrowOnExcessiveDrift = false
});
```

## Trade-offs

| Property | HLC |
|---|---|
| Space complexity | ✅ O(1) — single timestamp regardless of cluster size |
| Time complexity | ✅ O(1) — send and receive are constant time |
| Wall-clock proximity | ✅ Bounded drift from physical time |
| Causality tracking | ✅ Preserved via happens-before ordering |
| Concurrency detection | ❌ Cannot detect concurrent events |
| Physical clock dependency | ❌ Requires reasonably synchronized clocks |

See [HLC vs Vector Clocks](/concepts/hlc-vs-vector) for a full comparison and decision guide.

## Running the Demos

```bash
# Propagating HLC across service boundaries (header format)
dotnet run --project demo/Clockworks.Demo -- hlc-messaging

# BeforeSend/BeforeReceive workflow with coordinator statistics
dotnet run --project demo/Clockworks.Demo -- hlc-coordinator
```
