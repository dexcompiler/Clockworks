---
title: Contributing
---

# Contributing

Issues and pull requests are welcome.

## What to include in PRs

- A clear description of the behavior change and motivation.
- Tests for behavioral changes where feasible.
- Updates to docs/examples when public APIs change.

## Running the build/tests

From the repo root:

```bash
dotnet build
dotnet test
```

Property tests:

```bash
dotnet test property-tests/Clockworks.PropertyTests.fsproj
```

