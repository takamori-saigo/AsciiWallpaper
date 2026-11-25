#!/usr/bin/env bash
# Собирает «ASCII Обои.app» в папку dist/ (macOS, Apple Silicon или Intel).
set -euo pipefail
cd "$(dirname "$0")"

ARCH="$(uname -m)"
if [ "$ARCH" = "arm64" ]; then RID="osx-arm64"; else RID="osx-x64"; fi

APP_NAME="ASCII Обои"
APP="dist/${APP_NAME}.app"

echo "▶ dotnet publish ($RID)…"
dotnet publish -c Release -r "$RID" --self-contained true -o "bin/publish/$RID" -p:UseAppHost=true

echo "▶ Собираю бандл…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "bin/publish/$RID/." "$APP/Contents/MacOS/"

# Иконка .icns из Assets/icon.png
if command -v iconutil >/dev/null 2>&1; then
  ICONSET="$(mktemp -d)/AppIcon.iconset"
  mkdir -p "$ICONSET"
  for s in 16 32 128 256 512; do
    sips -z $s $s Assets/icon.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    sips -z $((s*2)) $((s*2)) Assets/icon.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
fi

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>${APP_NAME}</string>
  <key>CFBundleDisplayName</key><string>${APP_NAME}</string>
  <key>CFBundleIdentifier</key><string>dev.asciiwallpaper.app</string>
  <key>CFBundleVersion</key><string>1.0</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>AsciiWallpaper</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

echo "▶ Подписываю (ad-hoc)…"
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true

echo "✓ Готово: $APP"
echo "  Запуск: open \"$APP\"   (можно перетащить в /Applications)"
