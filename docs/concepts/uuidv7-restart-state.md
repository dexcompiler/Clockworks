---
title: UUIDv7 Restart State
---

# UUIDv7 Restart State

`UuidV7Factory` is monotonic within a live factory instance. `UuidV7FactoryState` extends that guarantee across a controlled restart by letting an application persist the logical frontier and restore it into the replacement factory.

## Model

The factory frontier is ordered lexicographically:

```text
(timestampMs, counter)
```

where `timestampMs` is the 48-bit UUIDv7 Unix millisecond field and `counter` is the 12-bit `rand_a` monotonic counter. Internally, the factory stores this pair in one atomic 64-bit word, so restoring state is a compare-and-swap max operation rather than a lock.

```mermaid
flowchart LR
    A["Generate UUID"] --> B["GetState()"]
    B --> C["Persist 8-byte frontier"]
    C --> D["Restart process"]
    D --> E["ReadFrom(bytes)"]
    E --> F["new UuidV7Factory(timeProvider, restoredState)"]
    F --> G["Next UUID is above restored frontier"]
```

## Correctness Invariant

For a restored frontier `R`, the next successfully generated UUID observes:

```text
generated_frontier > R
```

If physical time is ahead of `R.timestampMs`, the factory starts from physical time and a random counter start. If physical time is equal to or behind `R.timestampMs`, the factory starts from `R` and advances the counter or logical timestamp on the next allocation.

This preserves monotonicity relative to the persisted cursor without adding work to the normal `NewGuid()` hot path. `GetState()` reads one atomic word, `RestoreState()` only advances the current state, and `UuidV7FactoryState.WriteTo(...)` writes the canonical 8-byte encoding into caller-provided memory.

## Failure Modes

Restart state is a local recovery mechanism, not a distributed allocator.

| Scenario | Behavior |
|---|---|
| Crash after durable UUID write but before state persistence | The latest frontier may be lost. Store state in the same durability boundary as the data protected by the UUID. |
| Multiple live factories restore the same state | They can overlap in logical `(timestamp, counter)` space. Use node partitioning, HLC UUIDs, external coordination, or storage uniqueness constraints. |
| Restored timestamp is far ahead of physical time | The factory continues from the restored logical time and reports logical drift through statistics when enabled. |
| Restored same-millisecond counter is already maxed | `SpinWait` waits for physical time to advance; `IncrementTimestamp` advances logical time. |

For high-assurance systems, treat restart state as one layer in a deterministic issuance protocol:

1. Generate UUIDs from a singleton factory.
2. Commit application data and the latest factory state in the same durable transaction or checkpoint.
3. Keep a storage uniqueness constraint on the identifier column.
4. Use node partitioning or `HlcGuidFactory` when several writers intentionally share a namespace.
