---
title: Instrumentation
---

# Instrumentation

Clockworks includes **lightweight, allocation-free counters** intended for test assertions and simulation diagnostics. They are safe to read concurrently without external synchronization.

## SimulatedTimeProvider statistics

`SimulatedTimeProvider` exposes `Statistics`:

```csharp
var tp = new SimulatedTimeProvider();

// create timers / Advance() / etc...

Console.WriteLine(tp.Statistics.TimersCreated);
Console.WriteLine(tp.Statistics.CallbacksFired);
Console.WriteLine(tp.Statistics.AdvanceCalls);
```

Key counters include:

- `TimersCreated`, `TimersDisposed`, `TimerChanges`
- `CallbacksFired`, `PeriodicReschedules`
- `AdvanceCalls`, `AdvanceTicks`
- `QueueEnqueues`, `MaxQueueLength`

You can reset counters between test phases:

```csharp
tp.Statistics.Reset();
```

## UUIDv7 factory statistics

`UuidV7Factory` can update opt-in `UuidV7FactoryStatistics` counters:

```csharp
var stats = new UuidV7FactoryStatistics();
using var factory = new UuidV7Factory(
    TimeProvider.System,
    rng: null,
    overflowBehavior: CounterOverflowBehavior.SpinWait,
    statistics: stats);

factory.NewGuid();

var snapshot = stats.Snapshot();
Console.WriteLine(snapshot.GeneratedCount);
Console.WriteLine(snapshot.ClockRollbackCount);
Console.WriteLine(snapshot.CasRetryCount);
```

Statistics are disabled unless you pass an instance to the factory. When enabled, counters use atomic operations so they can be read safely while UUIDs are being generated concurrently. `Snapshot()` and `Reset()` operate counter-by-counter; they are suitable for diagnostics and phase-isolated tests, not as a linearizable multi-counter transaction.

Useful counters include:

- `GeneratedCount`
- `ClockRollbackCount`
- `CounterOverflowCount`
- `SpinWaitCount`
- `LogicalTimestampAdvanceCount`
- `MaxLogicalDriftMs`
- `CasRetryCount`
- `RandomBufferRefillCount`

Use `Snapshot()` when you need a point-in-time value object, and `Reset()` to isolate test phases:

```csharp
stats.Reset();
```

## Timeout statistics

Timeout factory helpers (`Timeouts`) can record aggregate timeout activity via `TimeoutStatistics`.

By default, `Timeouts` records into `Timeouts.DefaultStatistics`:

```csharp
var tp = new SimulatedTimeProvider();

Timeouts.DefaultStatistics.Reset();

using var handle = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(5));
tp.Advance(TimeSpan.FromSeconds(5));

Console.WriteLine(Timeouts.DefaultStatistics.Created);
Console.WriteLine(Timeouts.DefaultStatistics.Fired);
Console.WriteLine(Timeouts.DefaultStatistics.Disposed);
```

If you prefer isolation per test (or per component), pass your own `TimeoutStatistics` instance:

```csharp
var stats = new TimeoutStatistics();
using var handle = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(1), stats);
```
