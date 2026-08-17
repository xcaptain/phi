#!/usr/bin/env bash
#
# Builds a macOS Phi.app bundle for the Avalonia desktop app.
#
# Why this exists: the Dock / Cmd-Tab icon on macOS comes from the .app
# bundle's Contents/Resources/AppIcon.icns (referenced by Info.plist), NOT
# from Avalonia's Window.Icon. A bare `dotnet run` / published binary has no
# bundle, so macOS shows a generic icon. This script assembles the bundle,
# generates the icns from the master PNG, and ad-hoc signs it.
#
# Usage:
#   scripts/package-macos.sh [version]     # version defaults to latest git tag / short sha
#
# Output:
#   dist/Phi.app
#   dist/Phi-<version>-darwin-arm64.zip
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-$(git -C "$REPO_ROOT" describe --tags --always 2>/dev/null || echo 0.1.0)}"
RID="osx-arm64"

BUILD_DIR="$REPO_ROOT/dist"
STAGE="$BUILD_DIR/.stage"
APP="$BUILD_DIR/Phi.app"
ICONSET="$STAGE/AppIcon.iconset"

echo "==> Publish PhiCoding.Avalonia.Desktop ($RID, self-contained)"
dotnet publish "$REPO_ROOT/PhiCoding.Avalonia.Desktop/PhiCoding.Avalonia.Desktop.csproj" \
  -c Release -r "$RID" --self-contained -o "$STAGE/phi-avalonia"

echo "==> Assemble $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$STAGE/phi-avalonia/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/phi-avalonia"

echo "==> Generate AppIcon.icns from Assets/phi.png"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
# iconutil expects exactly these ten sizes in the iconset directory.
# sips (macOS built-in) keeps the script dependency-free for CI runners,
# which don't ship ImageMagick.
while read -r name px; do
  sips -z "$px" "$px" "$REPO_ROOT/PhiCoding.Avalonia/Assets/phi.png" \
    --out "$ICONSET/$name" >/dev/null
done <<'ICONS'
icon_16x16.png 16
icon_16x16@2x.png 32
icon_32x32.png 32
icon_32x32@2x.png 64
icon_128x128.png 128
icon_128x128@2x.png 256
icon_256x256.png 256
icon_256x256@2x.png 512
icon_512x512.png 512
icon_512x512@2x.png 1024
ICONS
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

echo "==> Write Info.plist"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Phi</string>
  <key>CFBundleDisplayName</key><string>Phi</string>
  <key>CFBundleIdentifier</key><string>dev.xcaptain.phi</string>
  <key>CFBundleExecutable</key><string>phi-avalonia</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION#v}</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

echo "==> Ad-hoc sign (local use; real signing/notarization later)"
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || echo "warning: codesign failed"

echo "==> Package zip"
(cd "$BUILD_DIR" && zip -q -r "Phi-$VERSION-darwin-arm64.zip" Phi.app)

echo "==> Done:"
echo "    $APP"
echo "    $BUILD_DIR/Phi-$VERSION-darwin-arm64.zip"
