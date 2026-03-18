---
layout: home

hero:
  name: Clockworks
  text: 'Time is just another dependency.'
  tagline: 'Deterministic, fully controllable time and time-ordered identifiers for distributed-system simulations and testing. Built on .NET 10 TimeProvider.'
  image:
    src: /logo.png
    alt: Clockworks
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: View on GitHub
      link: https://github.com/dexcompiler/Clockworks

features:
  - title: Deterministic TimeProvider
    details: SimulatedTimeProvider gives you full control over wall time via SetUtcNow() and monotonic scheduler time via Advance(). Deterministic timer ordering and predictable periodic behavior make testing time-sensitive code reliable.

  - title: TimeProvider Timeouts
    details: CancellationTokenSource instances driven entirely by your TimeProvider — not the wall clock. CreateTimeout() and CreateTimeoutHandle() let simulated time fast-forward through timeout scenarios.

  - title: UUIDv7 Generation
    details: RFC 9562 compliant UUIDv7 generation with 48-bit timestamp and 12-bit monotonic counter. Works with real or simulated time. Lock-free, thread-safe, configurable counter overflow behavior.

  - title: Hybrid Logical Clock
    details: O(1) causality tracking that stays close to physical time. Configurable drift enforcement, full 80-bit canonical encoding and optimized 64-bit packed encoding, BeforeSend/BeforeReceive coordination API.

  - title: Vector Clock
    details: Exact causality tracking and concurrency detection. Sorted-array representation optimized for sparse clocks. Thread-safe VectorClockCoordinator with allocation-conscious hot path. Binary and string wire formats.

  - title: Lightweight Instrumentation
    details: Built-in counters for timer creation, advances, timeouts, and more. Use instrumentation statistics in simulation assertions to verify exactly what happened during a test run.
---
