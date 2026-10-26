#!/usr/bin/env bash
set -euo pipefail
script="$(cd "$(dirname "$0")" && pwd)/check-release-output.sh"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT

clean() {
    rm -rf "$fixture"/*
    for file in HexIDE.Desktop.dll HexIDE.Desktop.deps.json HexIDE.Desktop.runtimeconfig.json; do
        printf '{}' > "$fixture/$file"
    done
}
reject() {
    if bash "$script" "$fixture" > /dev/null 2>&1; then
        echo "Release guard accepted $1" >&2
        exit 1
    fi
}
clean
bash "$script" "$fixture"
for entry in 'HexIDE.Desktop.dll:HexIDE.Desktop.Server' \
             'HexIDE.Desktop.deps.json:ModelContextProtocol/1.0' \
             'HexIDE.Desktop.runtimeconfig.json:Microsoft.AspNetCore.App'; do
    clean
    printf '%s' "${entry#*:}" >> "$fixture/${entry%%:*}"
    reject "$entry"
done
for dependency in Microsoft.AspNetCore.Hosting.dll ModelContextProtocol.Core.dll; do
    clean
    mkdir "$fixture/nested"
    touch "$fixture/nested/$dependency"
    reject "$dependency"
done
clean
rm "$fixture/HexIDE.Desktop.dll"
reject 'missing assembly'
echo 'Release guard self-test passed (six rejected fixtures)'
