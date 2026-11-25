#!/usr/bin/env bash
# Собирает установщик dist/ASCII-Oboi.dmg: внутри приложение и ярлык «Программы»,
# установка — перетащить приложение на ярлык.
set -euo pipefail
cd "$(dirname "$0")"

APP_NAME="ASCII Обои"
DMG="dist/ASCII-Oboi.dmg"

# 1. Сначала само приложение (.app)
./build-app.sh

# 2. Папка для образа: приложение + ярлык на /Applications
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp -R "dist/${APP_NAME}.app" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

# 3. Сжатый образ диска
echo "▶ Создаю .dmg…"
rm -f "$DMG"
hdiutil create \
  -volname "${APP_NAME}" \
  -srcfolder "$STAGE" \
  -fs HFS+ \
  -format UDZO \
  -ov "$DMG" >/dev/null

echo "✓ Готово: $DMG"
echo "  Открой его, перетащи «${APP_NAME}» в «Программы» и запускай из Launchpad."
open -R "$DMG" 2>/dev/null || true
