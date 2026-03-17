---
title: Getting Started
---

# Getting Started

Clockworks is a .NET library for deterministic, fully controllable time in distributed-system simulations and tests. It is built around `TimeProvider` so that time becomes an injectable dependency you can control — including timers and timeouts — while also providing time-ordered identifiers and causal timestamps.

## Installation

Install via the .NET CLI:

```bash
dotnet add package Clockworks
```

Or add directly to your project file:

```xml
<PackageReference Include="Clockworks" Version="1.3.0" />
```

## Requirements

- **.NET 10.0+** (`net10.0` target framework)
- No additional runtime dependencies

## Quick Start

### Deterministic timers with simulated time

```csharp
var tp = new SimulatedTimeProvider();

var fired = 0;
using var timer = tp.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

tp.Advance(TimeSpan.FromSeconds(1));
// fired == 1
```

### TimeProvider-driven timeouts

```csharp
var tp = new SimulatedTimeProvider();

using var timeout = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(5));

tp.Advance(TimeSpan.FromSeconds(5));
// timeout.Token.IsCancellationRequested == true
```

### UUIDv7 generation

```csharp
var factory = new UuidV7Factory(TimeProvider.System);
var id = factory.NewGuid();
```

### Vector Clock usage

```csharp
// Create coordinators for two nodes
var nodeA = new VectorClockCoordinator(nodeId: 1);
var nodeB = new VectorClockCoordinator(nodeId: 2);

// Node A sends a message
var clockA = nodeA.BeforeSend();

// Node B receives the message
nodeB.BeforeReceive(clockA);

// Node B sends a reply
var clockB = nodeB.BeforeSend();

// Verify causality
Console.WriteLine(clockA.HappensBefore(clockB)); // true
```

## Next Steps

- [Simulated Time](/guide/simulated-time) — control wall time and drive timers deterministically
- [Timeouts](/guide/timeouts) — `CancellationTokenSource` driven by your `TimeProvider`
- [UUIDv7 Generation](/guide/uuidv7) — RFC 9562 compliant, monotonic, sortable identifiers
- [Hybrid Logical Clock](/guide/hlc) — O(1) causality tracking close to physical time
- [Vector Clock](/guide/vector-clock) — exact causality and concurrency detection
- [Instrumentation](/guide/instrumentation) — built-in counters for simulation assertions
