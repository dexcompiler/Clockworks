# FsCheck Property Testing - Implementation Plan & Summary

## Executive Summary

This document outlines the planning, implementation, and identification of property-based testing opportunities for the Clockworks library. We have successfully created an F# property test project using FsCheck that tests the core features of Clockworks through 54 property tests.

## Project Background

**Clockworks** is a .NET library for deterministic, fully controllable time in distributed-system simulations and tests. It provides:
- Simulated TimeProvider with deterministic timer scheduling
- UUIDv7 generation with monotonicity guarantees
- Hybrid Logical Clock (HLC) timestamps for distributed systems
- TimeProvider-driven timeouts and instrumentation

## Features Identified for Property Testing

### High Priority Features

#### 1. **UuidV7Factory** ⭐⭐⭐
**Why Property Testing?**
- Monotonicity is a critical invariant that must hold across all scenarios
- Sub-millisecond ordering via counter requires rigorous testing
- Lock-free concurrency needs verification across random thread patterns
- RFC 9562 compliance (version/variant bits) should hold for all UUIDs

**Properties Tested:**
- Sequential monotonicity (100 tests)
- Timestamp prefix consistency at same millisecond (50 tests)
- Time advancement changes UUID ordering (50 tests)
- Counter overflow spin-wait resumes after time advances (1 deterministic test)
- UUID uniqueness (50 tests with 10-265 UUIDs each)
- UUIDs change with time (50 tests)
- Concurrent generation uniqueness (20 tests)

**Test Coverage:** 7 tests

#### 2. **HlcTimestamp** ⭐⭐⭐
**Why Property Testing?**
- Causality preservation is mathematically defined and must always hold
- Multiple encoding formats (64-bit packed, 80-bit full) must round-trip
- Comparison operators form a total order with specific mathematical properties
- Distributed clock synchronization depends on correct ordering

**Properties Tested:**
- Packing/unpacking round-trip (ToPackedInt64 ↔ FromPackedInt64)
- Comparison transitivity: a ≤ b ∧ b ≤ c → a ≤ c
- Comparison reflexivity: a = a
- Comparison antisymmetry: a ≤ b ∧ b ≤ a → a = b
- Wall time ordering: higher wallTime → later timestamp
- Counter ordering: same wallTime, higher counter → later timestamp
- NodeId ordering: same wallTime & counter, higher nodeId → later
- Packed representation ordering preservation
- Full 80-bit encoding round-trip (WriteTo ↔ ReadFrom)

**Test Coverage:** 9 properties

#### 3. **SimulatedTimeProvider** ⭐⭐
**Why Property Testing?**
- Determinism is the core value proposition - time must be fully controllable
- Timer scheduling order must be predictable for reproducible tests
- Time advancement must be atomic and consistent
- GetTimestamp monotonicity is a fundamental requirement

**Properties Tested:**
- GetUtcNow consistency (no implicit advancement)
- Deterministic time advancement (exact duration)
- Cumulative advances (multiple small = one large)
- Timer callback ordering (by scheduled time)
- Relative time advancement
- GetTimestamp monotonicity across advances
- Negative advancement rejection
- Timer functionality (callbacks fire at correct times)
- GetElapsedTime accuracy

**Test Coverage:** 9 properties

#### 4. **VectorClock** ⭐⭐
**Why Property Testing?**
- Merge is a pure function with well-defined algebraic properties
- Partial ordering should be consistent across merges and increments
- Serialization formats should round-trip exactly

**Properties Tested:**
- Merge commutativity: Merge(a, b) = Merge(b, a)
- Merge associativity: Merge(Merge(a, b), c) = Merge(a, Merge(b, c))
- Merge idempotence: Merge(a, a) = a
- Merge dominance: Merged clock is ≥ each input
- Parse/ToString round-trip
- WriteTo/ReadFrom round-trip
- Increment advances the clock for a node

**Test Coverage:** 7 properties

#### 5. **Timeouts** ⭐⭐
**Why Property Testing?**
- Cancellation timing should be driven by the provided TimeProvider
- Non-positive durations should cancel immediately and deterministically

**Properties Tested:**
- Token cancels after due time for positive timeouts
- Immediate cancellation for zero/negative timeouts

**Test Coverage:** 2 properties

#### 6. **HlcCoordinator** ⭐
**Why Property Testing?**
- Message send/receive is the core causality mechanism
- Local monotonicity must hold across mixed send/receive sequences
- Counter behavior depends on physical time and remote timestamps

**Properties Tested:**
- Sequential send timestamps are strictly increasing
- Receiving any remote timestamp advances the local timestamp
- Remote-ahead wall time is adopted with counter = 1
- Receive then send yields a timestamp after the remote
- Counter resets to 0 when physical time jumps ahead

**Test Coverage:** 5 properties

#### 7. **VectorClockCoordinator** ⭐
**Why Property Testing?**
- Message flow relies on correct merge + increment behavior
- Causality and concurrency detection must hold under send/receive

**Properties Tested:**
- Send increments local counter and advances the clock
- Receive merges then increments the local counter
- Receive then send preserves happens-before ordering
- Independent sends from distinct nodes are concurrent

**Test Coverage:** 4 properties

#### 8. **Instrumentation/Statistics** ⭐
**Why Property Testing?**
- Counters must match observed events for diagnostics to be trusted
- Statistics are updated concurrently and must stay monotonic

**Properties Tested:**
- Advance call/tick totals match inputs
- Timer creation updates queue counts
- Timer change operations are recorded
- One-shot timers record fired/disposed counts
- Periodic timers record reschedules
- Timeout statistics update after positive timeouts
- Non-positive timeouts record fired/disposed immediately
- Disposing a timeout handle records a dispose without firing

**Test Coverage:** 8 properties

#### 9. **Performance** ⭐
**Why Property Testing?**
- Catch performance regressions in critical paths
- Ensure deterministic simulations remain fast

**Properties Tested:**
- Bulk UUID generation completes within 1 second (60k UUIDs)
- Concurrent UUID generation completes within 1 second (50k UUIDs)
- Advancing 10k timers completes within 1 second

**Test Coverage:** 3 properties

### Medium Priority Features (Not Yet Implemented)

## Implementation Details

### Technology Stack

| Component | Version | Purpose |
|-----------|---------|---------|
| **F#** | 10.0 | Test language (testing C# code) |
| **FsCheck** | 3.3.2 | Property-based testing framework |
| **FsCheck.Xunit.v3** | 3.3.2 | xUnit v3 integration |
| **xunit.v3** | 3.2.2 | Test framework |
| **.NET** | 10.0 | Runtime |

### Project Structure

```
Clockworks/
├── src/                              # C# library code
│   ├── UuidV7Factory.cs
│   ├── SimulatedTimeProvider.cs
│   └── Distributed/
│       ├── HlcTimestamp.cs
│       └── HlcCoordinator.cs
├── tests/                            # Existing C# xUnit tests (44 tests)
│   └── Clockworks.Tests.csproj
└── tests-property/                   # NEW: F# property tests (54 tests)
    ├── Clockworks.PropertyTests.fsproj
    ├── HlcTimestampProperties.fs     # 9 properties
    ├── HlcCoordinatorProperties.fs   # 5 properties
    ├── InstrumentationStatisticsProperties.fs # 8 properties
    ├── PerformanceProperties.fs      # 3 properties
    ├── UuidV7FactoryProperties.fs    # 7 tests
    ├── SimulatedTimeProviderProperties.fs  # 9 properties
    ├── VectorClockProperties.fs      # 7 properties
    ├── VectorClockCoordinatorProperties.fs # 4 properties
    ├── TimeoutsProperties.fs         # 2 properties
    ├── PROPERTY_TESTING_PLAN.md      # This plan
    └── README.md                     # Documentation
```

### Test Counts

- **Total Property Tests**: 54
- **Total Example Tests** (existing): 44
- **Combined Coverage**: 98 tests

### Property Test Distribution

```
HlcTimestamp:            9 tests (17%)
SimulatedTimeProvider:   9 tests (17%)
Instrumentation:         8 tests (15%)
UuidV7Factory:           7 tests (13%)
VectorClock:             7 tests (13%)
HlcCoordinator:          5 tests (9%)
VectorClockCoordinator:  4 tests (7%)
Performance:             3 tests (6%)
Timeouts:                2 tests (4%)
```

## Property Testing Benefits for Clockworks

### 1. Edge Case Discovery
Property tests automatically test edge cases that might not appear in example-based tests:
- Counter overflow at exactly 4095 (12-bit limit)
- Negative time values
- Maximum timestamp values (year 10889 for 48-bit timestamps)
- Zero-duration timers
- Concurrent UUID generation patterns

### 2. Mathematical Verification
Properties express the mathematical invariants that Clockworks depends on:
- **Monotonicity**: Sequential operations produce increasing values
- **Causality**: HLC timestamps preserve happens-before relationships  
- **Determinism**: Same inputs always produce same outputs
- **Ordering**: Comparison operations satisfy total order axioms

### 3. Regression Prevention
As Clockworks evolves, property tests continue to verify core invariants:
- Refactoring lock-free algorithms? Properties verify monotonicity still holds
- Optimizing timestamp packing? Properties verify round-trip preservation
- Adding new timer features? Properties verify ordering remains correct

### 4. Documentation
Property tests serve as executable specifications:
- "UUIDs are monotonic" is clearer than 10 example assertions
- Mathematical properties document design intent
- Test names describe invariants in plain English

## Known Issues & Future Work

### Issues to Fix

- None currently in the property test suite. All 54 tests pass locally.

### Future Property Tests

1. **Stateful property testing** (2-3 properties)
   - Model-based concurrency flows
   - Long-running simulation sequences

2. **Extended performance properties** (2-3 properties)
   - Memory usage stays bounded
   - Throughput under sustained contention

## Success Metrics

✅ **Project Setup Complete**
- F# test project created and integrated
- FsCheck.Xunit.v3 configured with xUnit v3
- 54 properties discovered and running
- Build and test infrastructure working

✅ **Documentation Complete**
- Comprehensive README with examples
- Best practices guide
- Common issues and solutions
- Future work identified

✅ **Core Features Covered**
- UuidV7Factory: 7 tests covering monotonicity, timestamp behavior, overflow handling, uniqueness, concurrency
- HlcTimestamp: 9 properties covering ordering and encoding
- HlcCoordinator: 5 properties covering message causality and counter behavior
- SimulatedTimeProvider: 9 properties covering determinism, timers, advancement
- VectorClock: 7 properties covering merge algebra and serialization
- VectorClockCoordinator: 4 properties covering send/receive causality
- Instrumentation: 8 properties covering timer/timeout statistics accuracy
- Performance: 3 properties covering throughput bounds
- Timeouts: 2 properties covering cancellation timing

✅ **Property Suite Health**
- 54/54 tests passing locally

## Recommendations

### For Immediate Use
1. **Use passing tests now** - 54/54 tests provide immediate value
2. **Keep failures actionable** - Treat property test failures as regressions
3. **Integrate into CI** - Add property tests to build pipeline

### For Future Development
1. **Consider stateful property testing** - For complex concurrent scenarios
2. **Performance properties** - Verify lock-free algorithms scale

### For Team Adoption
1. **Review README** - Comprehensive guide for contributors
2. **Pair on first new property** - Build F# confidence
3. **Start with simple properties** - Build up complexity gradually

## Conclusion

We have successfully planned and implemented a comprehensive property-based testing framework for Clockworks using FsCheck in F#. The framework includes:

- ✅ 54 property tests across 9 core components
- ✅ F# project integrated with existing C# codebase
- ✅ xUnit v3 and FsCheck.Xunit.v3 integration
- ✅ Comprehensive documentation and best practices
- ✅ Clear identification of future work

The property tests provide rigorous verification of:
- Monotonicity invariants (UuidV7Factory)
- Causality preservation (HlcTimestamp)
- Message causality coordination (HlcCoordinator)
- Deterministic behavior (SimulatedTimeProvider)
- Statistics accuracy (Instrumentation)
- Performance bounds (Performance)
- Merge algebra and serialization (VectorClock)
- Vector clock coordination (VectorClockCoordinator)
- Cancellation timing (Timeouts)

This foundation enables confident refactoring, better edge case coverage, and mathematical verification of core Clockworks guarantees.

---

**Document Status**: Complete - Ready for Review
**Date**: 2026-01-22
**Author**: GitHub Copilot
**Reviewers**: @dexcompiler
