# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.3.0] - 2026-02-19

### Fixed
- Fix packed HLC decode for high-bit wall times.
- Enforce HLC drift bounds when the clock moves backwards.
- Harden `VectorClock` string parsing and coordinator locking.
- Fix `VectorClock` overflow behavior.
- Fix thread safety in demo `FailureInjector`.
- Preserve correlation IDs correctly in at-least-once demo flows.
- Prevent a template dictionary memory leak in the integration demo.
- Stabilize demo `MessageId` generation.

### Changed
- Optimize HLC timestamp serialization/parsing, and simplify witness max selection.
- Make HLC message header `TryParse` non-exceptional.
- Split HLC coordinator supporting types into separate files.
- Optimize `VectorClock` merge/compare/increment and serialization.
- Use `ArrayPool<T>` for vector clock merge buffers.
- Use `CollectionsMarshal` for vector clock canonicalization.
- UUIDv7 packing now uses `BinaryPrimitives` and a tighter packing path.
- UUIDv7 factory batch generation throughput improvements.
- Adopt C# 14 extension member blocks.

### Added
- Add high-value property tests for HLC and vector clocks.
- Demo: add distributed-systems at-least-once simulation with HLC/VC stats.

### Documentation
- Update README with HLC and `VectorClock` wire formats.
- Clarify HLC drift bounds and ordering scope.

### Build
- Infrastructure scripts: improve setup/maintenance harness and dotnet installation step.

## [1.2.0] - 2026-01-27

### Changed
- HLC receive semantics now witness the full remote `HlcTimestamp` `(wallTime, counter, nodeId)` (including nodeId tie-breaking) rather than only the remote wall time.
- HLC coordinator receive statistics now treat "remote ahead" based on full timestamp ordering.

### Added
- Additional property tests for `HlcCoordinator` covering remote-ahead by counter, nodeId tie-breaking, remote-behind behavior, and mixed send/receive/time interleavings.
- Unit test coverage for receiving a remote timestamp with higher counter at the same wall time.

### Build
- Centralized common build properties in `Directory.Build.props`.

[Unreleased]: https://github.com/dexcompiler/Clockworks/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/dexcompiler/Clockworks/releases/tag/v1.3.0
[1.2.0]: https://github.com/dexcompiler/Clockworks/releases/tag/v1.2.0
