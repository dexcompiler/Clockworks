#!/usr/bin/env bash
set -euo pipefail

dotnet_profile="/etc/profile.d/dotnet-cloud-agent.sh"

# Ensure profile exists if possible (best-effort)
if [[ ! -f "$dotnet_profile" ]]; then
  if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
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

export PATH="$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH"
EOF
    sudo install -m 0644 "$tmp" "$dotnet_profile"
    rm -f "$tmp"
  else
    echo "[start] warning: missing $dotnet_profile and sudo is unavailable/non-interactive"
  fi
fi

# Source profile if readable (avoid set -e footguns)
if [[ -r "$dotnet_profile" ]]; then
  # shellcheck disable=SC1090
  source "$dotnet_profile"
else
  echo "[start] warning: $dotnet_profile not readable; continuing"
fi

# Resolve dotnet in a robust way
dotnet_cmd=""
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
  dotnet_cmd="$HOME/.dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
  dotnet_cmd="dotnet"
elif [[ -x "/usr/share/dotnet/dotnet" ]]; then
  dotnet_cmd="/usr/share/dotnet/dotnet"
fi

if [[ -n "$dotnet_cmd" ]]; then
  echo "[start] dotnet: $("$dotnet_cmd" --version)"
else
  echo "[start] warning: dotnet not found"
fi

if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  cd "$(git rev-parse --show-toplevel)"

  if [[ -n "$dotnet_cmd" ]]; then
    # Optional warm-up; don’t fail startup if restore fails
    "$dotnet_cmd" restore >/dev/null 2>&1 || echo "[start] warning: dotnet restore failed"
  else
    echo "[start] warning: skipping dotnet restore (dotnet not found)"
  fi
fi

echo "[start] ready"
