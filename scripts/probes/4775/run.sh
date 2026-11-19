#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$(printf '%s' "$PATH" | tr ':' '\n' | grep -v '^/mnt/' | paste -sd:)"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1
export NUGET_HTTP_CACHE_PATH="$PWD/artifacts/4775/nuget-cache"
export XDG_RUNTIME_DIR="$(mktemp -d)"
trap 'rm -rf "$XDG_RUNTIME_DIR"' EXIT
mkdir -p artifacts/4775
export HEXIDE_RECURSION_PROBE=1
unset HEXIDE_RECURSION_RUNAWAY HEXIDE_RECURSION_NO_GC HEXIDE_RECURSION_STACK_GC_PROBE DOTNET_GCgen0size
run() {
    export HEXIDE_RECURSION_OUTPUT="$PWD/artifacts/4775/$1.csv"
    rm -f "$HEXIDE_RECURSION_OUTPUT"
    dotnet test IDE/HexIDE.Runtime.Tests/ -c Release -- --filter-class '*RecursionCostProbe'
}
run default
export DOTNET_GCgen0size=0x10000000
run raised
unset DOTNET_GCgen0size
export HEXIDE_RECURSION_NO_GC=1
run no-gc
unset HEXIDE_RECURSION_NO_GC
export HEXIDE_RECURSION_RUNAWAY=1
run runaway
export HEXIDE_RECURSION_NO_GC=1
run runaway-no-gc
unset HEXIDE_RECURSION_PROBE HEXIDE_RECURSION_NO_GC HEXIDE_RECURSION_RUNAWAY
export HEXIDE_RECURSION_STACK_GC_PROBE=1
run retained
