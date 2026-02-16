#!/usr/bin/env bash
# shellcheck shell=bash
# Profile script template for cloud agent environments
# This file is sourced, not executed directly

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Keep NuGet packages in a stable path (fast incremental restores)
export NUGET_PACKAGES="$HOME/.nuget/packages"

if [[ -z "${DOTNET_ROOT:-}" ]]; then
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
  else
    export DOTNET_ROOT="/usr/share/dotnet"
  fi
fi

# Idempotent PATH configuration - only add if not already present
# Define function only if it doesn't exist (safe for multiple sourcings)
if ! declare -F _add_to_path >/dev/null 2>&1; then
  _add_to_path() {
    case ":$PATH:" in
      *:"$1":*) ;;
      *) export PATH="$1:$PATH" ;;
    esac
  }
fi

_add_to_path "$DOTNET_ROOT"
_add_to_path "$HOME/.dotnet/tools"
