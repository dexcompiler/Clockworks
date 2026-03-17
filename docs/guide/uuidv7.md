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

// id1 < id2 (byte-wise comparison)
Console.WriteLine(id1.CompareTo(id2) < 0); // true
```

## Monotonicity Guarantees

Within a single millisecond, uniqueness and ordering are maintained by a **12-bit monotonic counter** appended to the 48-bit timestamp:

- The counter starts at a random value each millisecond.
- Each successive `NewGuid()` within the same millisecond increments the counter.
- If the counter overflows (4,096 values exhausted), the factory **SpinWaits** until the next millisecond.

This guarantees strict monotonicity without locks.

## Counter Overflow Behavior

The overflow behavior is configurable:

```csharp
var factory = new UuidV7Factory(TimeProvider.System, new UuidV7Options
{
    OverflowBehavior = UuidV7OverflowBehavior.SpinWait  // default
});
```

| Behavior | Description |
|---|---|
| `SpinWait` | Busy-waits until the next millisecond (default) |

## Thread Safety

`UuidV7Factory` uses a **lock-free** generation path. Multiple threads can call `NewGuid()` concurrently and receive unique, monotonically ordered results.

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
