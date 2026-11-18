#!/usr/bin/env bash
# Linux-capable orchestration checks; these do not prove Apple's signature acceptance.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT
mkdir -p "$TEMP/tools" "$TEMP/stage/addins/com.hexide.probe" "$TEMP/stage/standalone"
printf 'main' > "$TEMP/stage/HexIDE"
printf 'runner' > "$TEMP/stage/standalone/HexIDE.Standalone"
printf 'addin' > "$TEMP/stage/addins/com.hexide.probe/probe.dll"
cat > "$TEMP/tools/codesign" <<'TOOL'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$SIGN_LOG"
if [ "$1" = --verify ]; then
  [ "${FAIL_VERIFY:-0}" = 0 ]
fi
TOOL
chmod +x "$TEMP/tools/codesign"
export PATH="$TEMP/tools:$PATH"
export SIGN_LOG="$TEMP/sign.log"
bash "$ROOT/scripts/macos-bundle.sh" "$TEMP/stage" "$TEMP/Probe.app" 1.2.3
[ -f "$TEMP/Probe.app/Contents/Resources/addins/com.hexide.probe/probe.dll" ]
[ ! -e "$TEMP/Probe.app/Contents/MacOS/addins" ]
[ -f "$TEMP/Probe.app/Contents/MacOS/standalone/HexIDE.Standalone" ]
[ -f "$TEMP/stage/addins/com.hexide.probe/probe.dll" ]
grep -q '<string>1.2.3</string>' "$TEMP/Probe.app/Contents/Info.plist"
[ "$(tail -2 "$SIGN_LOG" | head -1)" = "--force -s - $TEMP/Probe.app" ]
[ "$(tail -1 "$SIGN_LOG")" = "--verify --strict --deep $TEMP/Probe.app" ]
grep -Fq -- "--force -s - $TEMP/Probe.app/Contents/MacOS/standalone/HexIDE.Standalone" "$SIGN_LOG"
if FAIL_VERIFY=1 bash "$ROOT/scripts/macos-bundle.sh" "$TEMP/stage" "$TEMP/Rejected.app" 1.2.3; then
  echo 'FAIL: ignored verification failure' >&2
  exit 1
fi
echo 'PASS: bundle layout, signing order and verification failure propagation'
