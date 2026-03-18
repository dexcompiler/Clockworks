---
title: Vector Clock
---

# Vector Clock

Vector clocks give you **exact** causality tracking across distributed nodes. Unlike purely time-based approaches, they can also detect **concurrency** (neither event happened-before the other).

In Clockworks, the core type is `VectorClock`, and the recommended integration surface is `VectorClockCoordinator` (per node).

## When to use Vector Clocks

Vector clocks are a good fit when you need:

- Precise *happens-before* relationships.
- Concurrency/conflict detection (e.g., replicated state, outbox/inbox flows, debugging race conditions).
- Causality tracking that does **not** rely on synchronized wall clocks.

Trade-offs:

- Space grows with the number of nodes represented in the clock.
- Merge/compare operations are \(O(n)\) in the number of entries (Clockworks uses a sparse, sorted representation to keep overhead low).

## Coordinator API (recommended)

Create one coordinator per node/service instance (node IDs are `ushort`):

```csharp
using Clockworks.Distributed;

var nodeA = new VectorClockCoordinator(nodeId: 1);
var nodeB = new VectorClockCoordinator(nodeId: 2);
```

### Sending

Call `BeforeSend()` to increment your local node counter and return a snapshot to attach to the outgoing message:

```csharp
var outbound = nodeA.BeforeSend();
// attach `outbound` to message headers/payload
```

### Receiving

Call `BeforeReceive(remoteClock)` before processing a message. This merges the remote clock into the local clock and increments the local node counter:

```csharp
nodeB.BeforeReceive(outbound);
```

### Local events (no message passing)

For events that should advance causality locally (e.g., writes) without sending a message:

```csharp
nodeA.NewLocalEvent();
```

## Comparing clocks

`VectorClock` implements the standard partial order:

```csharp
var a = nodeA.BeforeSend();
var b = nodeB.BeforeSend();

var order = a.Compare(b); // Equal | Before | After | Concurrent
var concurrent = a.IsConcurrentWith(b);
```

## Propagation via headers

Clockworks includes `VectorClockMessageHeader` for a compact HTTP/gRPC-friendly representation.

Serialize:

```csharp
var header = new VectorClockMessageHeader(
    clock: nodeA.BeforeSend(),
    correlationId: Guid.NewGuid()
);

var value = header.ToString();
// Send as: X-VectorClock: {value}
```

Parse:

```csharp
var parsed = VectorClockMessageHeader.Parse(value);
nodeB.BeforeReceive(parsed.Clock);
```

The header format is:

- Clock: `"node:counter,node:counter"` (sorted by node ID)
- Optional IDs: `";{correlationId:N}[;{causationId:N}]"`

## Binary wire format

For compact, canonical binary encoding, use:

- `VectorClock.WriteTo(...)` / `VectorClock.ReadFrom(...)`
- `VectorClock.GetBinarySize()` to allocate buffers correctly

Binary format:

`[count:u32 big-endian][(nodeId:u16 big-endian, counter:u64 big-endian)]*`

`ReadFrom(...)` also canonicalizes unsorted or duplicate node IDs by taking the maximum counter per node.

