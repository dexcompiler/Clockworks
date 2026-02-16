#!/usr/bin/env bash
set -euo pipefail

echo "[install] OS deps + .NET sanity"

sudo apt-get update -y
sudo apt-get install -y --no-install-recommends \
  ca-certificates curl git unzip jq \
  build-essential clang lld cmake pkg-config \
  zlib1g-dev libssl-dev libicu-dev

# Install .NET if missing (robust across images)
if ! command -v dotnet >/dev/null 2>&1 && [[ ! -x "$HOME/.dotnet/dotnet" ]]; then
  echo "[install] Installing .NET SDK via dotnet-install.sh"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
fi

# Write profile script with deterministic permissions
tmp="$(mktemp)"
cat >"$tmp" <<'EOF'
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Keep NuGet packages in a stable path (fast incremental restores)
export NUGET_PACKAGES="$HOME/.nuget/packages"

if [ -z "${DOTNET_ROOT:-}" ]; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export DOTNET_ROOT="$HOME/.dotnet"
  else
    export DOTNET_ROOT="/usr/share/dotnet"
  fi
fi

export PATH="$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH"
EOF

sudo install -m 0644 "$tmp" /etc/profile.d/dotnet-cloud-agent.sh
rm -f "$tmp"

# Persist for interactive shells (do this once, not inside profile.d)
if ! grep -q 'dotnet-cloud-agent.sh' "$HOME/.bashrc" 2>/dev/null; then
  echo 'if [ -r /etc/profile.d/dotnet-cloud-agent.sh ]; then . /etc/profile.d/dotnet-cloud-agent.sh; fi' >> "$HOME/.bashrc"
fi

# Apply vars for this script run
if [[ -r /etc/profile.d/dotnet-cloud-agent.sh ]]; then
  # shellcheck disable=SC1091
  source /etc/profile.d/dotnet-cloud-agent.sh
else
  echo "[install] Warning: /etc/profile.d/dotnet-cloud-agent.sh is not readable; continuing"
fi

# Prefer the known install location if present
dotnet_cmd=""
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
  dotnet_cmd="$HOME/.dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
  dotnet_cmd="dotnet"
elif [[ -x "/usr/share/dotnet/dotnet" ]]; then
  dotnet_cmd="/usr/share/dotnet/dotnet"
fi

if [[ -n "$dotnet_cmd" ]]; then
  "$dotnet_cmd" --version
  "$dotnet_cmd" --info
else
  echo "[install] Warning: dotnet not found; skipping dotnet checks"
fi

if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "[install] Repo detected: $(git rev-parse --show-toplevel)"
  cd "$(git rev-parse --show-toplevel)"

  if [[ -f global.json ]]; then
    echo "[install] global.json present:"
    cat global.json
  fi

  if [[ -n "$dotnet_cmd" ]]; then
    if [[ -f .config/dotnet-tools.json ]]; then
      "$dotnet_cmd" tool restore
    fi

    "$dotnet_cmd" workload restore || true
    "$dotnet_cmd" restore
  else
    echo "[install] Warning: dotnet not available; skipping restore"
  fi
fi

echo "[install] done"
