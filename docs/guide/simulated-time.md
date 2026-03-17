---
title: Simulated Time
---

# Simulated Time Provider

`SimulatedTimeProvider` is a fully controllable implementation of .NET's `TimeProvider` abstraction. It solves the core problem of non-deterministic test timing: rather than waiting for real wall clock time to pass, you advance the simulated clock explicitly and drive all timers and timeouts in a predictable, reproducible order.

## Core Concepts

`SimulatedTimeProvider` maintains two independent time dimensions:

- **Wall time** — the simulated "current time" returned by `GetUtcNow()`. You can set this freely with `SetUtcNow()`, move it backward for clock-skew tests, or advance it with `Advance()`.
- **Scheduler time** — the monotonic clock that governs when timers fire. It advances only when you call `Advance()`, and drives all `CreateTimer` callbacks in due-time order.

The independence of these two clocks means you can simulate clock skew, time rewinding, and drift without affecting timer-firing semantics.

::: tip
Scheduler time advances only via `Advance()`. Wall time can be modified independently for clock-skew and rewind simulations.
:::

## API

| Method | Description |
|---|---|
| `SetUtcNow(DateTimeOffset)` | Set the wall clock to an arbitrary time |
| `Advance(TimeSpan)` | Advance scheduler (and wall) time, firing all due timers in order |
| `GetTimestamp()` | Returns the current monotonic scheduler timestamp |
| `GetElapsedTime(long)` | Returns elapsed time since a previous `GetTimestamp()` value |

## Deterministic Timers

`CreateTimer` creates a timer whose callback is driven by simulated scheduler time. All due timers fire synchronously during `Advance()`, in ascending due-time order.

```csharp
var tp = new SimulatedTimeProvider();

var fired = 0;
using var timer = tp.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

tp.Advance(TimeSpan.FromSeconds(1));
// fired == 1
```

Multiple timers with different delays fire in the correct order:

```csharp
var tp = new SimulatedTimeProvider();
var log = new List<string>();

using var t1 = tp.CreateTimer(_ => log.Add("first"),  null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
using var t2 = tp.CreateTimer(_ => log.Add("second"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

tp.Advance(TimeSpan.FromSeconds(2));
// log == ["first", "second"]
```

## Periodic Timers

Periodic timers are rescheduled after each callback. If a single `Advance()` spans multiple periods, all callbacks for that period are coalesced and fired in order — no callbacks are skipped.

```csharp
var tp = new SimulatedTimeProvider();
var ticks = 0;

using var timer = tp.CreateTimer(_ => ticks++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

tp.Advance(TimeSpan.FromSeconds(5));
// ticks == 5
```

## Instrumentation

`SimulatedTimeProvider` exposes instrumentation counters via `InstrumentationStatistics`:

| Counter | Description |
|---|---|
| `AdvanceCallCount` | Number of times `Advance()` was called |
| `TotalTicksAdvanced` | Total scheduler ticks advanced across all calls |
| `TimerQueueDepth` | Current number of timers scheduled |
| `TimersCreated` | Total timers created via `CreateTimer` |
| `TimersFired` | Total timer callbacks that have fired |
| `TimersDisposed` | Total timers that have been disposed |

## Running the Demo

```bash
dotnet run --project demo/Clockworks.Demo -- simulated-time
```

This demo shows simulated timers, periodic timer coalescing, and scheduler statistics in action.
