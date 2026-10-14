#!/usr/bin/env bash
# Run the self-contained build for this Unix host. Pass image or folder paths as arguments.
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
case "$(uname -s)/$(uname -m)" in
  Linux/x86_64) rid=linux-x64 ;;
  Linux/aarch64|Linux/arm64) rid=linux-arm64 ;;
  Darwin/x86_64) rid=osx-x64 ;;
  Darwin/arm64|Darwin/aarch64) rid=osx-arm64 ;;
  *) echo "Unsupported host: $(uname -s)/$(uname -m)" >&2; exit 1 ;;
esac
app="$root/artifacts/publish/$rid/FastImageViewer"
if [[ ! -x "$app" ]]; then
  bash "$root/build-unix.sh" "$rid"
fi
exec "$app" "$@"
