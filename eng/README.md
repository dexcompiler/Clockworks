# Cloud Agent Setup Scripts

This directory contains scripts for setting up and maintaining the Clockworks development environment in cloud-based AI agent environments (e.g., GitHub Codespaces, cloud IDEs, CI/CD runners).

## Scripts

### `cloud-agent-install.sh`

**Purpose**: Full environment setup and initialization

**When to use**: 
- First-time environment setup
- Clean environment initialization
- System dependencies need to be installed or updated

**What it does**:
1. Installs required OS packages (build tools, libraries)
2. Installs .NET SDK 10.0 if not present
3. Creates `/etc/profile.d/dotnet-cloud-agent.sh` profile script
4. Configures `.bashrc` for interactive shells
5. Restores NuGet packages, workloads, and .NET tools

**Requirements**:
- `sudo` access (for system packages and profile script installation)
- Internet connectivity (for package downloads and .NET SDK)

**Usage**:
```bash
./eng/cloud-agent-install.sh
```

### `cloud-agent-start.sh`

**Purpose**: Quick startup and maintenance

**When to use**:
- Starting a new shell session
- Refreshing environment variables
- Running routine package restores

**What it does**:
1. Creates profile script if missing (best-effort, requires sudo)
2. Sources `/etc/profile.d/dotnet-cloud-agent.sh` to set environment variables
3. Verifies .NET installation
4. Performs quick `dotnet restore` if in a git repository

**Requirements**:
- None (degrades gracefully without sudo or .NET)

**Usage**:
```bash
./eng/cloud-agent-start.sh
```

## Architecture

### Profile Script Template (`dotnet-profile-template.sh`)

The unified profile template provides:

- **Deterministic .NET configuration**: Disables telemetry, suppresses logos
- **Stable NuGet package cache**: Uses `$HOME/.nuget/packages`
- **Idempotent PATH management**: Prevents duplicate entries on repeated sourcing
- **Runtime detection**: Automatically finds .NET in user-local or system locations

The template is used by both install and start scripts to ensure consistency.

### Common Functions (`common.sh`)

Shared utility functions:

- `resolve_dotnet()`: Locates .NET runtime with fallback logic
  1. User-local installation (`$HOME/.dotnet/dotnet`)
  2. System PATH
  3. System-wide installation (`/usr/share/dotnet/dotnet`)

## Design Goals

1. **Deterministic**: Same script runs produce same environment state
2. **Idempotent**: Safe to run multiple times without side effects
3. **Resilient**: Graceful degradation when resources unavailable
4. **Non-interactive**: Works in CI/CD and automated environments
5. **Fast**: Minimal overhead for startup scripts

## Environment Variables

The profile script sets:

- `DOTNET_CLI_TELEMETRY_OPTOUT=1`: Disable telemetry
- `DOTNET_NOLOGO=1`: Suppress .NET logo
- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`: Skip first-run experience
- `NUGET_PACKAGES=$HOME/.nuget/packages`: Stable package cache location
- `DOTNET_ROOT`: .NET installation directory (auto-detected)
- `PATH`: Prepends .NET root and tools (idempotent)

## Target Platforms

These scripts target:
- GitHub Codespaces
- Cloud-based AI coding agents
- Ubuntu-based container environments
- CI/CD runners with Debian/Ubuntu base images

## Troubleshooting

**"sudo: a terminal is required to read the password"**
- The start script degrades gracefully when sudo is unavailable
- Run the install script manually when you have interactive sudo access

**".NET not found"**
- Verify `/etc/profile.d/dotnet-cloud-agent.sh` exists and is readable
- Check `$HOME/.dotnet/dotnet` or `/usr/share/dotnet/dotnet` exist
- Run `cloud-agent-install.sh` to install .NET

**"PATH keeps growing"**
- This is fixed in the current version using idempotent PATH management
- Source the profile script multiple times to verify: `source /etc/profile.d/dotnet-cloud-agent.sh`

## Maintenance

When updating the scripts:
1. Keep `dotnet-profile-template.sh` as the single source of truth
2. Use `common.sh` for shared logic
3. Run `shellcheck` on all scripts before committing
4. Test idempotency: running scripts multiple times should be safe
