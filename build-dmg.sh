#!/usr/bin/env bash
# Собирает установщик dist/ASCII-Oboi.dmg с оформленным окном:
# фон с подсказкой «Перетащите приложение в папку «Программы»», слева приложение, справа ярлык «Программы».
set -euo pipefail
cd "$(dirname "$0")"

APP_NAME="ASCII Обои"
VOL_NAME="ASCII Обои"
DMG="dist/ASCII-Oboi.dmg"
TMP_DMG="dist/ASCII-Oboi-rw.dmg"

# Окно установки: размер и где стоят иконки (в точках, как на фоне Assets/dmg/background.png)
WIN_W=660; WIN_H=420
APP_X=170; APP_Y=200
LINK_X=490; LINK_Y=200
ICON_SIZE=112

# 1. Само приложение (.app)
./build-app.sh

# 2. Папка для образа: приложение, ярлык на /Applications, фон
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp -R "dist/${APP_NAME}.app" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
mkdir "$STAGE/.background"
# фон для обычных и Retina-экранов в одном файле
tiffutil -cathidpicheck Assets/dmg/background.png Assets/dmg/background@2x.png \
  -out "$STAGE/.background/background.tiff" >/dev/null 2>&1 \
  || cp Assets/dmg/background.png "$STAGE/.background/background.tiff"

# 3. Записываемый образ, чтобы настроить окно
echo "▶ Создаю образ…"
rm -f "$DMG" "$TMP_DMG"
SIZE_MB=$(( $(du -sm "$STAGE" | cut -f1) + 40 ))
hdiutil create -srcfolder "$STAGE" -volname "$VOL_NAME" -fs HFS+ \
  -format UDRW -size "${SIZE_MB}m" -ov "$TMP_DMG" >/dev/null

# если такой том уже смонтирован (от прошлой сборки) — отключаем
if [ -d "/Volumes/${VOL_NAME}" ]; then hdiutil detach "/Volumes/${VOL_NAME}" -force >/dev/null || true; fi

DEVICE=$(hdiutil attach -readwrite -noverify -noautoopen "$TMP_DMG" | grep -E '^/dev/' | head -n 1 | awk '{print $1}')
sleep 2

# 4. Оформление окна через Finder (при первом запуске macOS спросит разрешение
#    «Терминал хочет управлять Finder» — нажми «Разрешить»)
echo "▶ Оформляю окно установки…"
osascript <<APPLESCRIPT || echo "  (не получилось оформить окно — образ всё равно рабочий)"
tell application "Finder"
  tell disk "${VOL_NAME}"
    open
    set current view of container window to icon view
    set toolbar visible of container window to false
    set statusbar visible of container window to false
    set the bounds of container window to {200, 120, $((200 + WIN_W)), $((120 + WIN_H))}
    set viewOptions to the icon view options of container window
    set arrangement of viewOptions to not arranged
    set icon size of viewOptions to ${ICON_SIZE}
    set text size of viewOptions to 13
    set background picture of viewOptions to file ".background:background.tiff"
    set position of item "${APP_NAME}.app" of container window to {${APP_X}, ${APP_Y}}
    set position of item "Applications" of container window to {${LINK_X}, ${LINK_Y}}
    close
    open
    update without registering applications
    delay 2
    close
  end tell
end tell
APPLESCRIPT

chmod -Rf go-w "/Volumes/${VOL_NAME}" || true
sync
hdiutil detach "$DEVICE" >/dev/null || hdiutil detach "$DEVICE" -force >/dev/null

# 5. Сжатый финальный образ
echo "▶ Сжимаю…"
hdiutil convert "$TMP_DMG" -format UDZO -imagekey zlib-level=9 -o "$DMG" >/dev/null
rm -f "$TMP_DMG"

echo "✓ Готово: $DMG"
echo "  Открой его и перетащи «${APP_NAME}» на папку «Программы»."
open "$DMG" 2>/dev/null || true
