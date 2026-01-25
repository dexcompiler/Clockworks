#!/usr/bin/env bash
set -euo pipefail

source /etc/profile.d/dotnet-cloud-agent.sh || true

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
