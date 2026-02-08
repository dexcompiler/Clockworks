# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction
- Prefer updating documentation comments for mathematical/complexity accuracy when refactoring; keep wire format and semantics unchanged unless explicitly requested.

## Versioning and Git Practices
- Treat recent Clockworks changes as a minor version bump (semver).
- Group commits for a clean git history.
- Consider adopting git tags for versioning.
- Move common build properties from `Clockworks.csproj` into `Directory.Build.props`.