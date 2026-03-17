---
title: Testing
---

# Testing

Clockworks has example-based tests and a strong set of **property-based tests**.

## Run all tests

From the repository root:

```bash
dotnet test
```

## Property-based tests

Property tests live under `property-tests/` and are implemented with FsCheck + xUnit.

Run them directly:

```bash
dotnet test property-tests/Clockworks.PropertyTests.fsproj
```

Helpful commands:

```bash
# Run with verbose output
dotnet test property-tests/Clockworks.PropertyTests.fsproj -v detailed

# Run a specific module
dotnet test --filter "FullyQualifiedName~UuidV7FactoryProperties"

# List tests
dotnet test --list-tests
```

For more detail (invariants covered, project structure), see `property-tests/README.md`.

