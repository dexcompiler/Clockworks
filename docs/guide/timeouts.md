---
title: Timeouts
---

# TimeProvider-Driven Timeouts

The standard `new CancellationTokenSource(TimeSpan)` overload cancels based on the **wall clock** — it doesn't respect a custom `TimeProvider`. Clockworks solves this with `Timeouts`, which produces cancellation tokens driven entirely by your `TimeProvider` instance, whether real or simulated.

## `Timeouts.CreateTimeout`

Returns a `CancellationTokenSource` that will be cancelled after the given duration elapses according to the provider's scheduler time. **You are responsible for disposing it.**

::: tip Prefer `CreateTimeoutHandle` for early-cancel scenarios
`CreateTimeout(...)` does not give you a handle to the underlying timer. If you need to end a timeout early (and ensure the timer is cleaned up), prefer `CreateTimeoutHandle(...)`.
:::

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

Disposing the handle **cancels** the token (and disposes the underlying timer and token source). This is useful when the timeout lifetime is exactly the scope you control.

## When to Use Which

| | `CreateTimeout` | `CreateTimeoutHandle` |
|---|---|---|
| Returns | `CancellationTokenSource` | `IDisposable` handle |
| Token access | `cts.Token` | `handle.Token` |
| Disposal responsibility | Caller | Dispose the handle (cancels + cleans up) |
| Best for | When you need full `CancellationTokenSource` control | When the handle lifetime == the scope |

::: warning Non-positive durations
Passing a non-positive `TimeSpan` (zero or negative) to either method results in the token being cancelled **immediately** — the same behaviour as `new CancellationTokenSource(TimeSpan.Zero)`.
:::

## Running the Demo

```bash
dotnet run --project demo/Clockworks.Demo -- timeouts
```

The demo shows fast-forwardable timeouts driven by simulated time, and how disposal before the deadline prevents cancellation.
