#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-osx-arm64}"
out="${2:-$PWD/artifacts}"
case "$rid" in osx-arm64) arch=arm64;; osx-x64) arch=x86_64;; *) echo 'Expected osx-arm64 or osx-x64' >&2; exit 1;; esac
mkdir -p "$out"
out="$(cd "$out" && pwd)"
./scripts/build-native.sh "$arch"
./scripts/build-icon.sh
app="$out/$rid/HeyRead.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish src/HeyRead.Desktop -c Release -r "$rid" --self-contained true -p:PublishSingleFile=false -p:RestoreLockedMode=true -o "$app/Contents/MacOS"
cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?><!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd"><plist version="1.0"><dict>
<key>CFBundleIconFile</key><string>AppIcon</string><key>CFBundleExecutable</key><string>HeyRead</string><key>CFBundleIdentifier</key><string>cn.heyread.desktop</string><key>CFBundleName</key><string>HeyRead</string><key>CFBundleDisplayName</key><string>盒阅 · HeyRead</string><key>CFBundleVersion</key><string>1</string><key>CFBundleShortVersionString</key><string>0.1.0</string><key>CFBundlePackageType</key><string>APPL</string><key>LSMinimumSystemVersion</key><string>15.0</string><key>NSHighResolutionCapable</key><true/><key>NSPrincipalClass</key><string>NSApplication</string><key>LSApplicationCategoryType</key><string>public.app-category.news</string><key>CFBundleLocalizations</key><array><string>zh-Hans</string><string>zh-Hant</string><string>en</string><string>fr</string></array></dict></plist>
PLIST
cp native/build/AppIcon.icns "$app/Contents/Resources/"
cp native/PrivacyInfo.xcprivacy "$app/Contents/Resources/"
cp ../LICENSE "$app/Contents/Resources/HeyRead-LICENSE"
cp docs/THIRD-PARTY-NOTICES.txt "$app/Contents/Resources/"
codesign --force --deep --sign "${HEYREAD_SIGN_IDENTITY:--}" "$app"
codesign --verify --deep --strict "$app"
if [ "${HEYREAD_NOTARY_PROFILE:-}" != "" ]; then
  ditto -c -k --keepParent "$app" "$out/$rid/HeyRead-notary.zip"
  xcrun notarytool submit "$out/$rid/HeyRead-notary.zip" --keychain-profile "$HEYREAD_NOTARY_PROFILE" --wait
  xcrun stapler staple "$app"
fi
if [ "${HEYREAD_SKIP_DMG:-0}" != 1 ]; then
  stage="$(mktemp -d)"
  trap 'rm -rf "$stage"' EXIT
  ditto "$app" "$stage/HeyRead.app"
  ln -s /Applications "$stage/Applications"
  hdiutil create -volname HeyRead -srcfolder "$stage" -ov -format UDZO "$out/HeyRead-0.1.0-$rid.dmg"
fi
printf '%s\n' "$app"
