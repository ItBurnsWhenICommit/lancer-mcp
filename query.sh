#!/usr/bin/env bash
# Wrapper for the DirectQuery tool that runs from anywhere in the repo.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: dotnet CLI not found. Install the .NET 9 SDK to continue." >&2
  exit 1
fi

dotnet run --project "$SCRIPT_DIR/DirectQuery/DirectQuery.csproj" -- "$@"
