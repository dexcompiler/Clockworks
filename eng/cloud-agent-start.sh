#!/usr/bin/env bash
set -euo pipefail

# Source common functions
# shellcheck disable=SC1091
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

dotnet_profile="/etc/profile.d/dotnet-cloud-agent.sh"

# Ensure profile exists if possible (best-effort)
if [[ ! -f "$dotnet_profile" ]]; then
  if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
    script_dir="$(dirname "${BASH_SOURCE[0]}")"
    sudo install -m 0644 "$script_dir/dotnet-profile-template.sh" "$dotnet_profile"
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

# Resolve dotnet command using common function
dotnet_cmd="$(resolve_dotnet)"

if [[ -n "$dotnet_cmd" ]]; then
  echo "[start] dotnet: $("$dotnet_cmd" --version)"
else
  echo "[start] warning: dotnet not found"
fi

if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  cd "$(git rev-parse --show-toplevel)"

  if [[ -n "$dotnet_cmd" ]]; then
    # Optional warm-up; show stderr if restore fails
    "$dotnet_cmd" restore 2>&1 || echo "[start] warning: dotnet restore failed (check output above)"
  else
    echo "[start] warning: skipping dotnet restore (dotnet not found)"
  fi
fi

echo "[start] ready"
