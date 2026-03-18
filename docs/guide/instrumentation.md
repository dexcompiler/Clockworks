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

