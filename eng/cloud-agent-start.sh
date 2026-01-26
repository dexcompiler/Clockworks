#!/usr/bin/env bash
set -euo pipefail

dotnet_profile="/etc/profile.d/dotnet-cloud-agent.sh"
if [ ! -f "$dotnet_profile" ]; then
  if command -v sudo >/dev/null 2>&1; then
    tmp="$(mktemp)"
    cat >"$tmp" <<'EOF'
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="$HOME/.nuget/packages"

if [ -z "${DOTNET_ROOT:-}" ]; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export DOTNET_ROOT="$HOME/.dotnet"
  else
    export DOTNET_ROOT="/usr/share/dotnet"
  fi
fi

export PATH="$DOTNET_ROOT:$PATH:$HOME/.dotnet/tools"
EOF
    sudo install -m 0644 "$tmp" "$dotnet_profile"
    rm -f "$tmp"
  else
    echo "[start] warning: missing $dotnet_profile and sudo is unavailable"
  fi
fi

source "$dotnet_profile" || true

if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$PATH:$HOME/.dotnet/tools"
  elif [ -x "/usr/share/dotnet/dotnet" ]; then
    export DOTNET_ROOT="/usr/share/dotnet"
    export PATH="$DOTNET_ROOT:$PATH:$HOME/.dotnet/tools"
  fi
fi

echo "[start] dotnet: $(dotnet --version)"

if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  cd "$(git rev-parse --show-toplevel)"

  # Optional: quick restore if you want “always ready”
  # (If you find this slows startup too much, remove it.)
  dotnet restore >/dev/null

  # Optional: build warm-up (I usually avoid this on start; do it only if you like)
  # dotnet build -c Release -v minimal >/dev/null
fi

echo "[start] ready"
