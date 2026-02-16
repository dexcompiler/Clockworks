#!/usr/bin/env bash
# Common functions for cloud agent scripts

# Resolve dotnet command with fallback logic
# Returns the path to dotnet executable or empty string if not found
resolve_dotnet() {
  local dotnet_cmd=""
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    dotnet_cmd="$HOME/.dotnet/dotnet"
  elif command -v dotnet >/dev/null 2>&1; then
    dotnet_cmd="dotnet"
  elif [[ -x "/usr/share/dotnet/dotnet" ]]; then
    dotnet_cmd="/usr/share/dotnet/dotnet"
  fi
  echo "$dotnet_cmd"
}
