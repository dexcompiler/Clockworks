---
title: Why Clockworks?
---

# Why Clockworks?

In real systems, **time is a dependency**:

- Timers and timeouts are the backbone of retries, backoff, leases, heartbeats, and SLAs.
- Distributed logic often relies on ordering and “happened-before” relationships.
- Tests that touch real time can become flaky, slow, and hard to reproduce.

Clockworks is built around `.NET`’s `TimeProvider` so that time becomes injectable and controllable. It also provides *time-ordered identifiers* and *causal timestamping* primitives used in distributed-system simulations and testing.

## What Clockworks gives you

- **Deterministic `TimeProvider`** via `SimulatedTimeProvider`
  - Controllable wall time (`SetUtcNow`, etc.)
  - Monotonic scheduler time that advances only via `Advance(...)`
  - Predictable timer ordering and periodic behavior

- **`TimeProvider`-driven timeouts**
  - `Timeouts.CreateTimeout(...)` and `Timeouts.CreateTimeoutHandle(...)`
  - Timeouts that follow your provider (real or simulated), not the wall clock

- **Time-ordered identifiers**
  - RFC 9562 UUIDv7 via `UuidV7Factory`
  - Optional HLC-guid factory for causality-preserving IDs in simulations

- **Causality tracking**
  - Hybrid Logical Clock (HLC): low overhead, stays close to physical time
  - Vector Clock: exact causality and concurrency detection

- **Lightweight instrumentation**
  - Counters for timer/advance/timeout activity you can assert on in tests

