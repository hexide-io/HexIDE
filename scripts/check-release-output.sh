#!/usr/bin/env bash
# Check the built distribution, including framework references (which are not copied as DLLs).
set -euo pipefail
output="${1:?usage: check-release-output.sh <Desktop output directory>}"

for file in HexIDE.Desktop.dll HexIDE.Desktop.deps.json HexIDE.Desktop.runtimeconfig.json; do
    if [ ! -s "$output/$file" ]; then
        echo "Missing Release output: $output/$file" >&2
        exit 1
    fi
done

if grep -aEq 'HexIDE\.Desktop\.Server|Microsoft\.AspNetCore|ModelContextProtocol' \
    "$output/HexIDE.Desktop.dll" "$output/HexIDE.Desktop.deps.json" \
    "$output/HexIDE.Desktop.runtimeconfig.json"; then
    echo "Release output contains the automation server or its dependencies" >&2
    exit 1
fi

if find "$output" -type f \( -iname 'Microsoft.AspNetCore*.dll' -o -iname 'ModelContextProtocol*.dll' \) -print -quit | grep -q .; then
    echo "Release output carries automation dependencies" >&2
    exit 1
fi
echo "Release output excludes the automation server and its dependencies"
