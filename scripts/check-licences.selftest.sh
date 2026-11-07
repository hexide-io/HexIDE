#!/usr/bin/env bash
# Exercise interpreter selection through the complete guard, with stubs ahead of PATH.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts
fixture=$(mktemp -d "$PWD/artifacts/licence-python.XXXXXX")
trap 'rm -rf "$fixture"' EXIT
# The real interpreter the stubs hand over to, found the way the guard finds one: on Windows, python3 can be
# the Microsoft Store stub, which is the case under test (#2897).
export REAL_PYTHON=
for candidate in python3 python; do
  path=$(command -v "$candidate") || continue
  if version=$("$path" --version 2>&1) && [[ "$version" == Python\ 3.* ]]; then REAL_PYTHON=$path; break; fi
done
[ -n "$REAL_PYTHON" ] || { echo "check-licences self-test: needs a working python3 or python on PATH"; exit 1; }
mkdir -p "$fixture/scripts" "$fixture/bin"
cp scripts/check-licences.sh scripts/check-source-licences.py scripts/package-licences.tsv "$fixture/scripts/"
cp Directory.Packages.props "$fixture/"
git -C "$fixture" init -q
git -C "$fixture" add .
export CALLS="$fixture/calls"
for candidate in python3 python py; do
  cat > "$fixture/bin/$candidate" <<'EOF'
#!/usr/bin/env bash
candidate=${0##*/}
if [ "$candidate" = py ]; then
  [ "${1:-}" = -3 ] || exit 1
  shift
fi
if [ "$candidate" != "$AVAILABLE" ]; then
  case "$STUB" in
    silent) exit 0 ;;
    python2) echo 'Python 2.7.18'; exit 0 ;;
    *) exit 127 ;;
  esac
fi
if [ "${1:-}" = --version ]; then
  echo 'Python 3.12.0' >&2
  exit 0
fi
printf '%s\n' "$candidate" >> "$CALLS"
exec "$REAL_PYTHON" "$@"
EOF
  chmod +x "$fixture/bin/$candidate"
done

fail=0
passed=0
check() {
  local label=$1 available=$2 stub=$3 notice=$4 expected=$5 got=0
  : > "$CALLS"
  if [ "$notice" = yes ]; then
    printf '// SPDX-License-Identifier: Unknown-1.0\n' > "$fixture/planted.cs"
  else
    printf '// SPDX-License-Identifier: MIT\n' > "$fixture/planted.cs"
  fi
  git -C "$fixture" add planted.cs
  AVAILABLE="$available" STUB="$stub" PATH="$fixture/bin:$PATH" \
    bash "$fixture/scripts/check-licences.sh" > "$fixture/output" 2>&1 || got=$?
  if [ "$got" -ne "$expected" ] ||
     { [ "$available" != none ] && [ "$(cat "$CALLS")" != "$available" ]; } ||
     { [ "$available" = none ] && ! grep -q 'Python 3.*python3.*python.*py -3' "$fixture/output"; } ||
     { [ "$notice" = yes ] && ! grep -q 'planted.cs' "$fixture/output"; }; then
    echo "  WRONG  $label (exit $got, expected $expected)"
    cat "$fixture/output"
    fail=$((fail + 1))
  else
    echo "  ok     $label"
    passed=$((passed + 1))
  fi
}

check 'python3 runs the scan'                 python3 missing no  0
check 'failed python3 falls back to python'   python  missing no  0
check 'failed aliases fall back to py -3'     py      missing no  0
check 'no interpreter gives a useful error'   none    missing no  1
check 'silent stub falls back to python'      python  silent  no  0
check 'silent stubs cannot pass the guard'    none    silent  no  1
check 'Python 2 falls back to Python 3'       python  python2 no  0
check 'fallback still rejects bad notices'    py      missing yes 1
echo "check-licences self-test: $passed passed, $fail failed"
[ "$fail" -eq 0 ]
