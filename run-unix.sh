#!/usr/bin/env bash
# Run the self-contained Linux build. Pass image or folder paths as arguments.
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
app="$root/artifacts/publish/linux-x64/FastImageViewer"
if [[ ! -x "$app" ]]; then
  "$root/build-unix.sh" linux-x64
fi
exec "$app" "$@"
