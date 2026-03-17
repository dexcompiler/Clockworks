---
title: Timeouts
---

# TimeProvider-Driven Timeouts

The standard `new CancellationTokenSource(TimeSpan)` overload cancels based on the **wall clock** — it doesn't respect a custom `TimeProvider`. Clockworks solves this with `Timeouts`, which produces cancellation tokens driven entirely by your `TimeProvider` instance, whether real or simulated.

## `Timeouts.CreateTimeout`

Returns a `CancellationTokenSource` that will be cancelled after the given duration elapses according to the provider's scheduler time. **You are responsible for disposing it.**

```csharp
var tp = new SimulatedTimeProvider();

using var cts = Timeouts.CreateTimeout(tp, TimeSpan.FromSeconds(5));
// cts.Token is not yet cancelled

tp.Advance(TimeSpan.FromSeconds(5));
// cts.Token.IsCancellationRequested == true
```

## `Timeouts.CreateTimeoutHandle`

Returns an `IDisposable` handle. The underlying `CancellationTokenSource` and its timer are cleaned up when you dispose the handle, tying the token's lifetime to handle disposal.

```csharp
var tp = new SimulatedTimeProvider();

using var timeout = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(5));

tp.Advance(TimeSpan.FromSeconds(5));
// timeout.Token.IsCancellationRequested == true
```

When you `Dispose()` the handle before the timeout fires, the timer is cancelled and the token is not cancelled.

## When to Use Which

| | `CreateTimeout` | `CreateTimeoutHandle` |
|---|---|---|
| Returns | `CancellationTokenSource` | `IDisposable` handle |
| Token access | `cts.Token` | `handle.Token` |
| Disposal responsibility | Caller | Automatic on `Dispose()` |
| Best for | When you need full `CancellationTokenSource` control | When the handle lifetime == the scope |

::: warning Non-positive durations
Passing a non-positive `TimeSpan` (zero or negative) to either method results in the token being cancelled **immediately** — the same behaviour as `new CancellationTokenSource(TimeSpan.Zero)`.
:::

## Running the Demo

```bash
dotnet run --project demo/Clockworks.Demo -- timeouts
```

The demo shows fast-forwardable timeouts driven by simulated time, and how disposal before the deadline prevents cancellation.
