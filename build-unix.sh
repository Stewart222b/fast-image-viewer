#!/usr/bin/env bash
# Publish self-contained Fast Image Viewer builds and pack each one into a tarball.
# Usage: ./build-unix.sh [rid...]
# Default rid is linux-x64. Example: ./build-unix.sh linux-x64 osx-x64 osx-arm64
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
if [[ -x "${DOTNET_ROOT:-}/dotnet" ]]; then
  export PATH="$DOTNET_ROOT:$PATH"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
if ! command -v dotnet >/dev/null 2>&1; then
  echo "The .NET 8 SDK was not found. Install it, then run this script again." >&2
  exit 1
fi

rids=("$@")
if [[ ${#rids[@]} -eq 0 ]]; then
  rids=(linux-x64)
fi

project="$root/cross/FastImageViewer.Desktop.csproj"
for rid in "${rids[@]}"; do
  case "$rid" in
    linux-x64|linux-arm64|osx-x64|osx-arm64) ;;
    *)
      echo "Unsupported rid: $rid" >&2
      echo "Use linux-x64, linux-arm64, osx-x64, or osx-arm64." >&2
      exit 1
      ;;
  esac
  out="$root/artifacts/publish/$rid"
  rm -rf "$out"
  dotnet publish "$project" -c Release -r "$rid" --self-contained true \
    -p:DebugType=None -p:DebugSymbols=false -o "$out"
  cp -f "$root/cross/DIST.txt" "$out/README.txt"
  archive="$root/artifacts/FastImageViewer-0.3.0-$rid.tar.gz"
  tar -C "$out" -czf "$archive" .
  echo "Built $archive"
done
