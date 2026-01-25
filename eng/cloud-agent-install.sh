#!/usr/bin/env bash
set -euo pipefail

echo "[install] OS deps + .NET sanity"

# ---- OS packages you typically need for modern .NET builds ----
# (Especially important if you ever do NativeAOT, or hit packages with native assets.)
sudo apt-get update -y
sudo apt-get install -y --no-install-recommends \
  ca-certificates curl git unzip jq \
  build-essential clang lld cmake pkg-config \
  zlib1g-dev libssl-dev libicu-dev

# ---- Make dotnet predictable + less chatty ----
# Put these into a profile.d script so every shell gets them.
sudo tee /etc/profile.d/dotnet-cloud-agent.sh >/dev/null <<'EOF'
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Keep NuGet packages in a stable path (fast incremental restores)
export NUGET_PACKAGES="$HOME/.nuget/packages"

export DOTNET_ROOT="${DOTNET_ROOT:-/usr/share/dotnet}"
export PATH="${PATH:+$PATH:}$HOME/.dotnet/tools"
EOF

# Ensure the file is readable before sourcing it
sudo chmod 644 /etc/profile.d/dotnet-cloud-agent.sh

# Apply vars for this script run too
if [[ -r /etc/profile.d/dotnet-cloud-agent.sh ]]; then
  source /etc/profile.d/dotnet-cloud-agent.sh
else
  echo "[install] Warning: cannot read /etc/profile.d/dotnet-cloud-agent.sh"
fi

# ---- Verify SDK is present & show info (fail fast if not) ----
dotnet --version
dotnet --info

# ---- Repo bootstrap (only if we're inside a repo checkout) ----
# Cursor agents typically run in the repo workspace; this makes it robust either way.
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "[install] Repo detected: $(git rev-parse --show-toplevel)"
  cd "$(git rev-parse --show-toplevel)"

  # If you pin SDK with global.json, ensure it's respected (informational)
  if [[ -f global.json ]]; then
    echo "[install] global.json present:"
    cat global.json
  fi

  # Restore local tools (e.g., dotnet-format, husky, etc.)
  if [[ -f .config/dotnet-tools.json ]]; then
    dotnet tool restore
  fi

  # If you use workloads (wasm, maui, aspire, etc.)
  # This is safe even if none are declared; it just no-ops.
  dotnet workload restore || true

  # Warm NuGet cache once (speeds up later builds)
  dotnet restore
fi

echo "[install] done"
