---
title: Determinism Model
---

# Determinism Model

Clockworks is designed so **simulated scheduler time deterministically drives timers/timeouts**, while **wall time can be manipulated independently** to model clock skew, rewinds, and other wall-clock effects.

## Two notions of time

When using `SimulatedTimeProvider`, there are two separate “clocks”:

- **Wall time** (`GetUtcNow()`): controllable, may move backwards via `SetUtcNow(...)`.
- **Scheduler time** (timers + `GetTimestamp()`): monotonic, advances only via `Advance(...)`.

This split is intentional:

- Timers/timeouts are driven by scheduler time so they’re reproducible and ordered deterministically.
- Wall time remains available for scenarios where you want to simulate clock drift, skew, or rewinds without changing timer ordering.

## Deterministic timer behavior

Timers created via `SimulatedTimeProvider.CreateTimer(...)` fire when scheduler time reaches their due time. Advancing time:

```csharp
var tp = new SimulatedTimeProvider();
var fired = 0;

using var timer = tp.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

tp.Advance(TimeSpan.FromSeconds(1));
// fired == 1
```

Periodic timers default to **coalescing** on large time jumps: if you advance by a long duration, the timer is rescheduled from “now” (rather than firing repeatedly for every missed period). This keeps simulations fast and avoids unbounded callback loops.

## Timeout determinism

`Timeouts.CreateTimeout(...)` and `Timeouts.CreateTimeoutHandle(...)` schedule cancellation using the provided `TimeProvider`. If that provider is simulated, timeouts are fast-forwardable and fully deterministic.

