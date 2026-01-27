# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2026-01-27

### Changed
- HLC receive semantics now witness the full remote `HlcTimestamp` `(wallTime, counter, nodeId)` (including nodeId tie-breaking) rather than only the remote wall time.
- HLC coordinator receive statistics now treat "remote ahead" based on full timestamp ordering.

### Added
- Additional property tests for `HlcCoordinator` covering remote-ahead by counter, nodeId tie-breaking, remote-behind behavior, and mixed send/receive/time interleavings.
- Unit test coverage for receiving a remote timestamp with higher counter at the same wall time.

### Build
- Centralized common build properties in `Directory.Build.props`.

[Unreleased]: https://github.com/dexcompiler/Clockworks/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/dexcompiler/Clockworks/releases/tag/v1.2.0
