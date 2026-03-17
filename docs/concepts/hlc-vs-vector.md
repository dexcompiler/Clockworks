---
title: HLC vs Vector Clocks
---

# HLC vs Vector Clocks

Clockworks provides two complementary approaches for tracking ordering/causality in distributed systems and simulations:

- **Hybrid Logical Clock (HLC)**: a *total order* that stays close to physical time with \(O(1)\) metadata.
- **Vector Clock (VC)**: a *partial order* with exact causality and *concurrency detection*.

## Hybrid Logical Clock (HLC)

HLC timestamps combine a wall-clock time with a logical counter (and node ID tie-breaking). In Clockworks, drift bounds are configurable via `HlcOptions.MaxDriftMs`, and strict enforcement can be enabled with `ThrowOnExcessiveDrift`.

Best for:

- Systems where wall-clock proximity matters (time-based SLAs, time-window queries)
- High-throughput paths where \(O(1)\) overhead is critical
- “Good enough” ordering when you don’t need to detect concurrency

Trade-offs:

- ✅ \(O(1)\) space/time overhead per event
- ✅ Close to physical time; bounded drift enforcement is configurable
- ❌ Cannot detect concurrency (only ordering)
- ❌ Benefits from reasonably synchronized clocks

## Vector Clock (VC)

Vector clocks track a counter per node. They can prove that one event happened-before another, and can also detect concurrency.

Best for:

- Exact happens-before relationships
- Conflict/concurrency detection in replicated state
- Debugging distributed races (seeing where concurrency occurred)
- Scenarios where relying on physical time is undesirable

Trade-offs:

- ✅ Exact causality
- ✅ Detects concurrency
- ✅ No dependency on physical time
- ❌ Metadata grows with number of nodes
- ❌ Merge/compare costs scale with entries present in the clock

## Practical guidance

- Prefer **HLC** if you primarily want a cheap, monotonic ordering that correlates with wall time.
- Prefer **Vector Clocks** when concurrency detection is important (e.g., last-writer-wins vs merge, replicated workflows, debugging).
- In simulations, you can use both:
  - HLC for cheap global ordering / time-ordered IDs
  - Vector clocks for precise causality assertions

