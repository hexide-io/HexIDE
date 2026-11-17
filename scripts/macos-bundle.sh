#!/usr/bin/env bash
# Assemble and seal the measured macOS layout (#4495, #4507).
set -euo pipefail
if [ "$#" -ne 3 ]; then
  echo "usage: macos-bundle.sh <staged dir> <app path> <version>" >&2
  exit 2
fi
SRC="$1"
APP="$2"
VERSION="$3"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$SRC/." "$APP/Contents/MacOS/"
if [ -d "$APP/Contents/MacOS/addins" ]; then
  mv "$APP/Contents/MacOS/addins" "$APP/Contents/Resources/addins"
fi
cp "$ROOT/IDE/HexIDE/Icons/AppIcon/hexide.icns" "$APP/Contents/Resources/hexide.icns"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>HexIDE</string>
  <key>CFBundleDisplayName</key><string>HexIDE</string>
  <key>CFBundleIdentifier</key><string>io.hexide.HexIDE</string>
  <key>CFBundleExecutable</key><string>HexIDE</string>
  <key>CFBundleIconFile</key><string>hexide</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# The main executable resolves to the bundle and is signed by the final step.
# Some data files cannot be signed; the strict bundle gate below is authoritative.
while IFS= read -r -d '' f; do
  codesign --force -s - "$f" || true
done < <(find "$APP/Contents/MacOS" -type f -print0)
codesign --force -s - "$APP"
codesign --verify --strict --deep "$APP"
