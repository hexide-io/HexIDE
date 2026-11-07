#!/usr/bin/env bash
# Runs one test project under WSL from a copy of this working tree on WSL's own filesystem.
#
#   wsl -e bash scripts/test-linux.sh <project-dir> [dotnet test arguments...]
#   wsl -e bash scripts/test-linux.sh IDE/HexIDE.Runtime.Tests/
#   wsl -e bash scripts/test-linux.sh IDE/HexIDE.Tests/ -- --filter-class "*CodeEditorViewModelTests"
#
# Run it from the root of any checkout or worktree on the Windows drive. It copies the tree, without
# .git, bin, obj or artifacts, to ~/hexide-linux/<name>, where <name> is the checkout's folder name and
# a hash of its full path, so two worktrees never share a copy. Then it runs `dotnet test` there.
#
# WHY A COPY. Building on the /mnt/c mount itself works, but the mount is 9p, and under load a build
# writing its DLLs through it fails now and then with CS2012 "Read-only file system". A rerun alone
# passes, so it reads as a flaky test rather than a flaky filesystem. Two sessions hit it twice in one
# night on 2026-10-23. On WSL's own ext4 that write never crosses the mount.
#
# WHAT IS COPIED IS WHAT IS ON DISK, uncommitted edits included, byte for byte. A Windows checkout has
# core.autocrlf=true, so the copy has the CRLF files Windows CI checks out, as a run on /mnt/c did.
#
# The copy keeps its bin, obj and artifacts between runs, so the second run of a suite builds
# incrementally and the foreign language servers are fetched once per copy. Delete the folder to start
# clean. No --artifacts-path is needed: the copy's bin and obj are its own, and the tree around them
# still holds IDE/ and LspServer/, which GrammarParityTests.RepoRoot() walks up to find.
set -euo pipefail

if [ $# -lt 1 ]; then
    sed -n '2,6p' "$0" >&2
    exit 2
fi
project=${1%/}
shift

source_root=$(cd "$(dirname "$0")/.." && pwd -P)

# Use CI's floors for full suites (#883). Filtered runs only promise to run at least one test;
# callers can supply an explicit floor for a known slice. Raise full-suite floors in build.yml.
add_test_floor() {
    local project=$1 key floor arg filtered=false explicit=false separator=false
    shift
    case "$project" in
        IDE/HexIDE.Runtime.Tests) key=RUNTIME ;;
        IDE/HexIDE.Tests) key=IDE ;;
        IDE/HexIDE.Integration.Tests) key=INTEGRATION ;;
        IDE/HexIDE.TwinBasic.Tests) key=TWINBASIC ;;
        LspServer/HexIDE.VbLspServer.Tests) key=LSP_SERVER ;;
        *) echo "test-linux.sh: no CI test floor for $project" >&2; return 2 ;;
    esac
    for arg in "$@"; do
        case "$arg" in
            --filter-*) filtered=true ;;
            --minimum-expected-tests|--minimum-expected-tests=*) explicit=true ;;
            --) separator=true ;;
        esac
    done
    test_args=("$@")
    if [ "$explicit" = true ]; then return; fi
    floor=$(sed -n "s/^  MIN_TESTS_${key}: *\([0-9][0-9]*\).*/\1/p" "$source_root/.github/workflows/build.yml")
    if [[ ! "$floor" =~ ^[1-9][0-9]*$ ]]; then
        echo "test-linux.sh: missing or invalid MIN_TESTS_$key in build.yml" >&2; return 2
    fi
    if [ "$filtered" = true ]; then floor=1; fi
    if [ "$separator" = false ]; then test_args+=(--); fi
    # Put runner options immediately after --, leaving all original arguments intact.
    local i
    for i in "${!test_args[@]}"; do
        if [ "${test_args[$i]}" = -- ]; then
            test_args=("${test_args[@]:0:i+1}" --minimum-expected-tests "$floor" "${test_args[@]:i+1}")
            break
        fi
    done
}
add_test_floor "$project" "$@"
case "$source_root" in
    /mnt/*) ;;
    *) echo "test-linux.sh: run it from a checkout on the Windows drive, not from $source_root" >&2; exit 2 ;;
esac
[ -d "$source_root/$project" ] || { echo "test-linux.sh: no project directory $project under $source_root" >&2; exit 2; }

name=$(basename "$source_root")-$(printf '%s' "$source_root" | sha256sum | cut -c1-8)
copy="$HOME/hexide-linux/$name"
mkdir -p "$copy"

# --delete removes what the checkout no longer has; the excluded directories are left alone on both
# sides, so the copy's own build output survives.
rsync -a --delete \
    --exclude=.git --exclude=bin/ --exclude=obj/ --exclude=/artifacts/ --exclude=.vs/ --exclude=node_modules/ \
    "$source_root/" "$copy/"

echo "test-linux.sh: $source_root -> $copy" >&2
cd "$copy/$project"
exec dotnet test "${test_args[@]}"
