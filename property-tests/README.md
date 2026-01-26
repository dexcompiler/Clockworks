# Clockworks Property Tests

This directory contains property-based tests for the Clockworks library using [FsCheck](https://fscheck.github.io/FsCheck/), an F# implementation of QuickCheck for testing .NET code.

## Why Property Testing?

Property-based testing complements traditional example-based unit tests by:

- **Testing edge cases automatically**: FsCheck generates hundreds of random test cases, finding edge cases developers might miss
- **Verifying invariants**: Tests express mathematical properties that should always hold
- **Better coverage**: One property test can replace dozens of example-based tests
- **Shrinking failures**: When a test fails, FsCheck automatically finds the smallest failing case

## Project Structure

```
tests-property/
├── Clockworks.PropertyTests.fsproj    # F# test project with FsCheck.Xunit.v3
├── HlcTimestampProperties.fs          # Properties for Hybrid Logical Clock timestamps
├── HlcCoordinatorProperties.fs        # Properties for HLC message coordination
├── InstrumentationStatisticsProperties.fs # Properties for instrumentation counters
├── UuidV7FactoryProperties.fs         # Properties for UUIDv7 generation
├── SimulatedTimeProviderProperties.fs # Properties for deterministic time simulation
├── VectorClockProperties.fs           # Properties for Vector Clock ordering/merging
├── VectorClockCoordinatorProperties.fs # Properties for Vector Clock coordination
├── TimeoutsProperties.fs              # Properties for Timeouts cancellation behavior
├── PROPERTY_TESTING_PLAN.md           # Implementation plan and summary
└── README.md                          # This file
```

## Technologies Used

- **Language**: F# (testing C# code)
- **Test Framework**: xUnit v3 (3.2.2)
- **Property Framework**: FsCheck 3.3.2 with FsCheck.Xunit.v3 3.3.2
- **.NET Version**: .NET 10.0

## Running the Tests

```bash
# Run all property tests
dotnet test tests-property/Clockworks.PropertyTests.fsproj

# Run with verbose output
dotnet test tests-property/Clockworks.PropertyTests.fsproj -v detailed

# Run specific test module
dotnet test --filter "FullyQualifiedName~UuidV7FactoryProperties"

# List all tests
dotnet test --list-tests
```

## Property Tests Overview

### 1. HlcTimestamp Properties (`HlcTimestampProperties.fs`)

Tests for the Hybrid Logical Clock timestamp implementation, verifying causality preservation and ordering guarantees.

**Properties Tested:**
- ✅ **Round-trip packing**: `ToPackedInt64()` and `FromPackedInt64()` are inverses
- ✅ **Comparison transitivity**: If a ≤ b and b ≤ c, then a ≤ c
- ✅ **Comparison reflexivity**: Every timestamp equals itself  
- ✅ **Comparison antisymmetry**: If a ≤ b and b ≤ a, then a = b
- ✅ **Wall time ordering**: Higher wall time means later timestamp
- ✅ **Counter ordering**: For same wall time, higher counter means later
- ✅ **NodeId ordering**: For same wall time and counter, higher nodeId means later
- ✅ **Packed ordering preservation**: Packed int64 values preserve timestamp ordering
- ✅ **Full encoding round-trip**: `WriteTo()` and `ReadFrom()` preserve all 80 bits

**Invariants:**
- Timestamps form a total order with well-defined comparison semantics
- Multiple encoding formats (64-bit packed, 80-bit full) preserve ordering
- Causality is maintained through the (wallTime, counter, nodeId) tuple

### 2. HlcCoordinator Properties (`HlcCoordinatorProperties.fs`)

Tests for HLC message coordination, ensuring causality and counter behavior.

**Properties Tested:**
- ✅ **Send monotonicity**: Sequential sends produce increasing timestamps
- ✅ **Receive advances local**: Any receive advances the local timestamp
- ✅ **Remote adoption**: Remote-ahead wall time is adopted with counter = 1
- ✅ **Causal chain**: Receive then send yields a later timestamp than the remote
- ✅ **Counter reset**: Physical time jumps reset the counter to zero on send

**Invariants:**
- Local timestamps always move forward on send/receive
- Remote-ahead timestamps are adopted deterministically
- Physical time bounds reset logical counters

### 3. UuidV7Factory Properties (`UuidV7FactoryProperties.fs`)

Tests for RFC 9562 UUIDv7 generation with monotonicity guarantees and time-based ordering.

**Properties Tested:**
- ✅ **Sequential monotonicity**: UUIDs generated sequentially are monotonically increasing
- ✅ **Timestamp prefix consistency**: UUIDs at same millisecond share timestamp prefix
- ✅ **Time advancement**: Advancing time results in different timestamps
- ✅ **Counter overflow handling**: SpinWait behavior resumes after time advances
- ✅ **UUID uniqueness**: Generated UUIDs are unique across many generations
- ✅ **UUIDs change with time**: UUIDs differ when time advances
- ✅ **Concurrent uniqueness**: Parallel generation produces unique UUIDs

**Invariants:**
- UUIDs maintain monotonic ordering even with SimulatedTimeProvider
- Sub-millisecond ordering via 12-bit counter
- Lock-free generation is thread-safe

### 4. SimulatedTimeProvider Properties (`SimulatedTimeProviderProperties.fs`)

Tests for deterministic time simulation ensuring reproducible test behavior.

**Properties Tested:**
- ✅ **GetUtcNow consistency**: Time doesn't advance without explicit calls
- ✅ **Deterministic advancement**: `Advance()` changes time by exact amount
- ✅ **Cumulative advances**: Multiple small advances equal one large advance
- ✅ **Timer ordering**: Timer callbacks fire in order of their delays
- ✅ **Relative time setting**: `Advance()` sets time relative to current
- ✅ **Timestamp monotonicity**: `GetTimestamp()` never decreases
- ✅ **Negative rejection**: Negative time advancement is rejected
- ✅ **Timer functionality**: `CreateTimer()` produces working timers
- ✅ **Elapsed time accuracy**: `GetElapsedTime()` matches advanced time

**Invariants:**
- Time is fully deterministic and controllable
- Timers fire in predictable order based on scheduled times
- No hidden time advancement

### 5. VectorClock Properties (`VectorClockProperties.fs`)

Tests for the Vector Clock implementation, verifying merge algebra and serialization round-trips.

**Properties Tested:**
- ✅ **Merge commutativity**: `Merge(a, b) = Merge(b, a)`
- ✅ **Merge associativity**: `Merge(Merge(a, b), c) = Merge(a, Merge(b, c))`
- ✅ **Merge idempotence**: `Merge(a, a) = a`
- ✅ **Merge dominance**: Merged clock is ≥ each input
- ✅ **Parse/ToString round-trip**: `Parse(ToString(v)) = v`
- ✅ **Binary round-trip**: `ReadFrom(WriteTo(v)) = v`
- ✅ **Increment advancement**: `Increment(node)` moves clock forward

**Invariants:**
- Merge yields the least upper bound of two clocks
- Serialization formats preserve ordering and identity

### 6. VectorClockCoordinator Properties (`VectorClockCoordinatorProperties.fs`)

Tests for Vector Clock message coordination, ensuring causal ordering and concurrency detection.

**Properties Tested:**
- ✅ **Send increments local counter**: BeforeSend advances the local node
- ✅ **Receive merges and increments**: BeforeReceive merges then increments local
- ✅ **Causal chain**: Receive then send yields a later clock
- ✅ **Concurrency**: Independent sends from distinct nodes are concurrent

**Invariants:**
- Send/receive operations preserve happens-before relationships
- Local node counters always advance on send/receive

### 7. Instrumentation Statistics (`InstrumentationStatisticsProperties.fs`)

Tests for statistics counters on simulated time and timeouts.

**Properties Tested:**
- ✅ **Advance accounting**: Advance call/tick totals match inputs
- ✅ **Timer creation**: Queue and creation counters reflect scheduled timers
- ✅ **Timer changes**: Change operations increment the counter
- ✅ **Callback tracking**: One-shot timers record fired/disposed counts
- ✅ **Periodic reschedules**: Periodic timers record reschedule events
- ✅ **Timeout accounting**: Fired/disposed counts update for positive timeouts
- ✅ **Immediate timeout**: Non-positive timeouts record fired/disposed immediately
- ✅ **Handle disposal**: Disposing before due records disposal without firing

**Invariants:**
- Statistics counters are monotonic and reflect observed events

### 8. Timeouts Properties (`TimeoutsProperties.fs`)

Tests for `Timeouts` cancellation semantics driven by a `TimeProvider`.

**Properties Tested:**
- ✅ **Cancellation timing**: Positive timeouts cancel only after due time
- ✅ **Immediate cancellation**: Non-positive timeouts cancel immediately

**Invariants:**
- Cancellation is driven by the provided time source
- Non-positive durations are treated as already expired

## Writing New Property Tests

### Basic Property Test Pattern

```fsharp
[<Property>]
let ``Property name describing the invariant`` (input: Type) =
    // Arrange: constrain inputs if needed
    let safeInput = makeValid input
    
    // Act: perform operation
    let result = doSomething safeInput
    
    // Assert: verify property holds
    checkInvariant result = true
```

### Using Custom Generators

For complex types that FsCheck can't automatically generate:

```fsharp
open FsCheck

type Arbitraries =
    static member HlcTimestamp() =
        Arb.generate<int64 * uint16 * uint16>
        |> Gen.map (fun (wallTime, counter, nodeId) ->
            HlcTimestamp(abs wallTime % maxWallTime, counter, nodeId))
        |> Arb.fromGen

// Register at assembly level
[<assembly: Properties(Arbitrary = [| typeof<Arbitraries> |])>]
do ()
```

### Configuration Options

```fsharp
[<Property(MaxTest = 1000, Verbose = true)>]
let ``Property with custom config`` (x: int) =
    x + x = 2 * x
```

**Common Options:**
- `MaxTest`: Number of test cases to generate (default: 100)
- `Verbose`: Print all test cases (useful for debugging)
- `QuietOnSuccess`: Don't print output on success
- `Arbitrary`: Custom generators to use

## Best Practices

### 1. Make Inputs Valid

Property tests should handle all possible inputs. Use guards or transformations:

```fsharp
let ``Property`` (x: int64) =
    let safeX = abs x % 1000L + 1L  // Ensure positive, reasonable range
    doSomething safeX
```

### 2. Test Fundamental Properties

Focus on mathematical properties and invariants:
- **Commutativity**: `f(a, b) = f(b, a)`
- **Associativity**: `f(f(a, b), c) = f(a, f(b, c))`
- **Identity**: `f(a, identity) = a`
- **Idempotence**: `f(f(a)) = f(a)`
- **Round-trip**: `decode(encode(x)) = x`

### 3. Use Meaningful Test Names

Test names should describe the property being verified:
- ✅ "Sequential UUIDs are monotonically increasing"
- ❌ "Test UUID ordering"

### 4. Keep Properties Simple

Each property should test ONE invariant. Complex properties are hard to understand when they fail.

### 5. Handle Non-Determinism

When testing concurrent code or using `TimeProvider.System`:
- Use smaller test counts (`MaxTest = 20`)
- Accept that some timing-based properties may occasionally fail
- Consider using `SimulatedTimeProvider` instead for determinism

## Common Issues and Solutions

### Issue: "Type X is not handled automatically by FsCheck"

**Solution**: Create a custom generator (see "Using Custom Generators" above)

### Issue: Property fails intermittently

**Solution**: 
- Check for non-determinism (real time, randomness, threading)
- Use `SimulatedTimeProvider` instead of `TimeProvider.System`
- Add `[<Property(MaxTest = 20)>]` for expensive/flaky tests

### Issue: Shrinking takes too long

**Solution**: Reduce input complexity or use `DoNotSize` attribute

## Future Work

- [ ] Performance properties (e.g., "UUID generation completes within X ms")
- [ ] Stateful property testing for concurrent scenarios

## References

- [FsCheck Documentation](https://fscheck.github.io/FsCheck/)
- [FsCheck.Xunit.v3 on NuGet](https://www.nuget.org/packages/FsCheck.Xunit.v3/)
- [Property-Based Testing with FsCheck](https://fsharpforfunandprofit.com/posts/property-based-testing/)
- [xUnit v3 Documentation](https://xunit.net/docs/getting-started/v3)
- [RFC 9562 - UUIDv7](https://www.rfc-editor.org/rfc/rfc9562.html)
- [Hybrid Logical Clocks (Kulkarni et al., 2014)](https://cse.buffalo.edu/tech-reports/2014-04.pdf)

## Contributing

When adding new property tests:
1. Follow existing naming conventions and file structure
2. Document the property being tested with clear comments
3. Include the mathematical or logical invariant in test names
4. Test both happy paths and edge cases
5. Update this README with new properties added

## License

MIT License (same as parent project)
