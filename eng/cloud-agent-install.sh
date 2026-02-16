#!/usr/bin/env bash
set -euo pipefail

# Source common functions
# shellcheck disable=SC1091
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

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

# Write profile script with deterministic permissions using unified template
script_dir="$(dirname "${BASH_SOURCE[0]}")"
sudo install -m 0644 "$script_dir/dotnet-profile-template.sh" /etc/profile.d/dotnet-cloud-agent.sh

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

# Resolve dotnet command using common function
dotnet_cmd="$(resolve_dotnet)"

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
