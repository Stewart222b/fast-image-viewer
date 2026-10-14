#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
if [[ -x "${DOTNET_ROOT:-}/dotnet" ]]; then
  export PATH="$DOTNET_ROOT:$PATH"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
mkdir -p "$root/artifacts"
dotnet run -c Release --project "$root/tests/FastImageViewer.Cross.Tests/FastImageViewer.Cross.Tests.csproj" -- "$root/artifacts"
