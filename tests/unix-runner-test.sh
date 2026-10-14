#!/usr/bin/env bash
# Exercise host selection and lazy builds without executing foreign binaries.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
sandbox="$root/artifacts/unix-runner-test-$$"
mkdir -p "$sandbox/bin"
cp "$root/run-unix.sh" "$sandbox/run-unix.sh"
cat > "$sandbox/bin/uname" <<'MOCK'
#!/usr/bin/env bash
case "$1" in
  -s) printf '%s\n' "$TEST_OS" ;;
  -m) printf '%s\n' "$TEST_ARCH" ;;
  *) exit 1 ;;
esac
MOCK
chmod +x "$sandbox/bin/uname"
export PATH="$sandbox/bin:$PATH"
cat > "$sandbox/build-unix.sh" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
printf '%s\n' "$1" > "$root/built-rid"
mkdir -p "$root/artifacts/publish/$1"
cat > "$root/artifacts/publish/$1/FastImageViewer" <<'APP'
#!/usr/bin/env bash
printf '%s\n' "$@"
APP
chmod +x "$root/artifacts/publish/$1/FastImageViewer"
MOCK

for host in 'Linux x86_64 linux-x64' 'Linux aarch64 linux-arm64' \
    'Linux arm64 linux-arm64' 'Darwin x86_64 osx-x64' 'Darwin arm64 osx-arm64'; do
  read -r TEST_OS TEST_ARCH expected <<< "$host"
  export TEST_OS TEST_ARCH
  # Rebuild each case in its own directory, including the two ARM64 aliases.
  caseRoot="$sandbox/$TEST_OS-$TEST_ARCH"
  mkdir -p "$caseRoot"
  cp "$sandbox/run-unix.sh" "$sandbox/build-unix.sh" "$caseRoot/"
  output="$(bash "$caseRoot/run-unix.sh" 'photo with spaces.png' '--sample')"
  [[ "$(cat "$caseRoot/built-rid")" == "$expected" ]]
  [[ "$output" == $'photo with spaces.png\n--sample' ]]
  printf 'already built\n' > "$caseRoot/built-rid"
  bash "$caseRoot/run-unix.sh" 'another photo.png' >/dev/null
  [[ "$(cat "$caseRoot/built-rid")" == 'already built' ]]
  printf 'PASS %s/%s selects %s, forwards arguments, and reuses builds\n' "$TEST_OS" "$TEST_ARCH" "$expected"
done
export TEST_OS=Linux TEST_ARCH=riscv64
if bash "$sandbox/run-unix.sh" > "$sandbox/unsupported.log" 2>&1; then
  echo 'FAIL unsupported architecture must be rejected' >&2
  exit 1
fi
[[ ! -e "$sandbox/built-rid" ]]
printf 'PASS unsupported host is rejected before publishing\n'
printf 'RESULT: 6 passed, 0 failed\n'
