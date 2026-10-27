#!/usr/bin/env bash
# Exercise the WSL runner's argument builder without copying a checkout or starting .NET.
set -euo pipefail
cd "$(dirname "$0")/.."
source_root=$PWD
builder=$(sed -n '/^add_test_floor() {/,/^}/p' scripts/test-linux.sh)
eval "$builder"
runner=$(sed -n 's/^exec dotnet test /capture /p' scripts/test-linux.sh)
capture() { test_args=("$@"); }
build_arguments() {
    local project=$1
    shift
    if declare -F add_test_floor >/dev/null; then add_test_floor "$project" "$@" || return; fi
    eval "$runner"
}
fail=0
check() {
    local expected=$1
    shift
    test_args=()
    if ! build_arguments "$@"; then
        echo "FAIL: $* (argument building failed)"; fail=$((fail + 1)); return
    fi
    local actual
    actual=$(printf '<%s>' "${test_args[@]}")
    if [ "$actual" != "$expected" ]; then
        echo "FAIL: $*: $actual, expected $expected"; fail=$((fail + 1))
    else
        echo "ok: $*"
    fi
}
for pair in 'IDE/HexIDE.Runtime.Tests RUNTIME' 'IDE/HexIDE.Tests IDE' \
            'IDE/HexIDE.Integration.Tests INTEGRATION' 'LspServer/HexIDE.VbLspServer.Tests LSP_SERVER' \
            'IDE/HexIDE.TwinBasic.Tests TWINBASIC'; do
    read -r project key <<< "$pair"
    floor=$(sed -n "s/^  MIN_TESTS_${key}: *\([0-9]*\).*/\1/p" .github/workflows/build.yml)
    check "<--><--minimum-expected-tests><$floor>" "$project"
done
check '<--no-build><--><--minimum-expected-tests><1><--filter-class><*ExampleTests>' \
    IDE/HexIDE.Tests --no-build -- --filter-class '*ExampleTests'
check '<--><--minimum-expected-tests><1><--filter-not-method><*Example>' \
    IDE/HexIDE.Tests -- --filter-not-method '*Example'
check '<--><--minimum-expected-tests><1><--filter-query></*/*/Example/*>' \
    IDE/HexIDE.Tests -- --filter-query '/*/*/Example/*'
check '<--><--minimum-expected-tests><7>' IDE/HexIDE.Tests -- --minimum-expected-tests 7
if build_arguments IDE/Unknown.Tests >/dev/null 2>&1; then
    echo 'FAIL: an unknown project must not run without a floor'; fail=$((fail + 1))
else
    echo 'ok: unknown project fails closed'
fi
mkdir -p artifacts
fixture=$(mktemp -d "$source_root/artifacts/test-floor.XXXXXX")
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/.github/workflows"
: > "$fixture/.github/workflows/build.yml"
source_root=$fixture
if build_arguments IDE/HexIDE.Tests >/dev/null 2>&1; then
    echo 'FAIL: a missing floor must not silently disable the guard'; fail=$((fail + 1))
else
    echo 'ok: missing floor fails closed'
fi
if [ "$fail" -ne 0 ]; then
    echo "$fail failed checks"; exit 1
fi
