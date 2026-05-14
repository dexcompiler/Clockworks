---
title: UUIDv7 Generation
---

# UUIDv7 Generation

UUIDv7 is a time-ordered UUID format defined in [RFC 9562](https://www.rfc-editor.org/rfc/rfc9562.html). Unlike UUIDv4 (random), UUIDv7 encodes a millisecond-resolution timestamp in the most-significant bits, making identifiers naturally sortable by creation time. This is especially useful in distributed systems where you want database-friendly, time-ordered primary keys without a centralised sequence generator.

## Basic Usage

```csharp
var factory = new UuidV7Factory(TimeProvider.System);
var id = factory.NewGuid();
```

`UuidV7Factory` accepts any `TimeProvider`, including `TimeProvider.System` for production use and `SimulatedTimeProvider` for tests.

## With Simulated Time

```csharp
var tp = new SimulatedTimeProvider();
var factory = new UuidV7Factory(tp);

var id1 = factory.NewGuid();

tp.Advance(TimeSpan.FromMilliseconds(1));

var id2 = factory.NewGuid();

// If you need temporal ordering, compare UUIDv7 in big-endian byte order.
Console.WriteLine(id1.CompareByTimestamp(id2) < 0); // true
```

Clockworks also includes helpers to decode UUIDv7 components:

```csharp
Console.WriteLine(id2.GetTimestampMs()); // unix ms (nullable if not v7)
Console.WriteLine(id2.GetCounter());     // 12-bit counter (nullable if not v7)
Console.WriteLine(id2.IsVersion7());     // true/false
```

## Monotonicity Guarantees

Within a single factory instance, ordering is maintained by a **12-bit monotonic counter** appended to the 48-bit timestamp:

- When physical time moves to a new millisecond, the counter is reset to a **random start value** (masked into the lower half of the counter space to leave room for increments).
- Each successive `NewGuid()` within the same millisecond increments the counter.
- If the wall clock moves backwards, the factory continues from the last logical `(timestamp, counter)` frontier instead of rewinding to the lower physical time.
- If the counter overflows (4,096 values exhausted), behavior depends on `CounterOverflowBehavior` (spin-wait, increment timestamp, or throw).

This guarantees strict per-instance monotonicity without locks. The guarantee is scoped to the live factory instance, not to every factory in a fleet.

## Collision and Clock-Skew Semantics

`UuidV7Factory` separates local deterministic allocation from distributed probabilistic uniqueness:

| Scope | Guarantee |
|---|---|
| One live factory instance | Deterministic, lock-free allocation of unique and monotonically increasing `(timestamp, counter)` pairs. Backward wall-clock movement does not rewind the logical frontier. |
| Multiple threads sharing one factory | Same per-instance guarantee; CAS retries may occur under contention, but successful allocations do not reuse a pair. |
| Multiple factories in one process | No shared logical frontier. Full UUID collisions are still extremely unlikely with independent CSPRNG state, but uniqueness is probabilistic. |
| Multiple processes or machines | No built-in global coordination or node discriminator in `UuidV7Factory`. Clock skew can increase timestamp overlap, while uniqueness depends on randomized counter starts and the 62-bit random tail. |
| Process restart | The new factory starts from current wall time and a random counter start. It does not inherit the previous logical frontier. |

For production services, prefer a single `UuidV7Factory` singleton per process or service instance. The built-in DI helpers register it this way.

For high-assurance shared namespaces, use a storage uniqueness constraint as the final guardrail and retry on conflict. If you need node-aware ordering semantics, consider `HlcGuidFactory`; it embeds a node ID and HLC timestamp, but it should be chosen for causal/node-aware ordering rather than treated as a blanket substitute for storage-level uniqueness.

## Counter Overflow Behavior

The overflow behavior is configurable via the constructor:

```csharp
var factory = new UuidV7Factory(
    timeProvider: TimeProvider.System,
    overflowBehavior: CounterOverflowBehavior.SpinWait // default
);
```

| Behavior | Description |
|---|---|
| `SpinWait` | Busy-waits until the next millisecond (default) |
| `IncrementTimestamp` | Artificially increments the timestamp to maintain throughput (timestamp may drift ahead) |
| `ThrowException` | Throws if more than 4096 UUIDs are allocated in a single millisecond |
| `Auto` | Chooses `IncrementTimestamp` for `SimulatedTimeProvider` (avoids deadlocks), otherwise `SpinWait` |

::: warning Simulated time + overflow
If you use `SimulatedTimeProvider` and generate more than 4096 UUIDs without advancing time, `SpinWait` can deadlock (simulated time won't move forward on its own). For simulations, prefer `Auto` or `IncrementTimestamp`.
:::

## Thread Safety

`UuidV7Factory` uses a **lock-free** generation path. Multiple threads can call `NewGuid()` concurrently and receive unique, monotonically ordered results.

## Batch generation

To reduce call overhead, you can fill a span in one call:

```csharp
var factory = new UuidV7Factory(TimeProvider.System);
Span<Guid> ids = stackalloc Guid[128];
factory.NewGuids(ids);
```

## Disposal

`UuidV7Factory` owns a cryptographically-secure RNG by default. Dispose the factory when you're done with it:

```csharp
using var factory = new UuidV7Factory(TimeProvider.System);
var id = factory.NewGuid();
```

## Security Considerations

::: danger UUIDv7 embeds a timestamp — do not use as an opaque token at public boundaries
UUIDv7 values **embed a millisecond-resolution timestamp** by design. Any UUIDv7 can be decoded to reveal an approximate creation time, and ordering/rate information can sometimes be inferred from sequences of IDs.
:::

If you are issuing identifiers across **untrusted/public boundaries** (URLs, externally-visible resource IDs, third-party logs), do not treat UUIDv7 as opaque. Common mitigations:

- Use a random UUID (e.g., UUIDv4) for externally-visible identifiers.
- Keep UUIDv7 as an internal primary key, and expose a separate opaque token externally.
- Wrap/encrypt identifiers for external presentation if you need internal ordering but external opacity.

See [Security Considerations](/concepts/security) for a full discussion.

## Performance

From the property-based performance tests:

- **60,000 sequential UUIDs** complete in under 1 second
- **50,000 concurrent UUIDs** (parallel generation) complete in under 1 second

## Running the Demos

```bash
# Basic UUIDv7 generation and time decoding
dotnet run --project demo/Clockworks.Demo -- uuidv7

# Sortability demonstration
dotnet run --project demo/Clockworks.Demo -- uuidv7-sortability

# Benchmark mode
dotnet run --project demo/Clockworks.Demo -- uuidv7 --bench
```
