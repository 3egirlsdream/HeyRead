#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-osx-arm64}"
out="${2:-$PWD/artifacts}"
case "$rid" in osx-arm64) arch=arm64;; osx-x64) arch=x86_64;; *) echo 'Expected osx-arm64 or osx-x64' >&2; exit 1;; esac
version=$(python3 scripts/release/release.py version)
slug=xiaoheihe-community-formac
mkdir -p "$out"
out="$(cd "$out" && pwd)"
./scripts/build-native.sh "$arch"
./scripts/build-icon.sh
app="$out/$rid/$slug.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish src/HeyRead.Desktop -c Release -r "$rid" --self-contained true -p:Version="$version" -p:PublishSingleFile=false -p:RestoreLockedMode=true -o "$app/Contents/MacOS"
python3 - "$app/Contents/Info.plist" "$version" <<'PY'
import json,plistlib,subprocess,sys
meta=json.loads(subprocess.check_output(['python3','scripts/release/release.py','metadata'],text=True))
info={'CFBundleIconFile':'AppIcon','CFBundleExecutable':meta['PackageName'],'CFBundleIdentifier':meta['BundleId'],'CFBundleName':meta['DisplayName'],'CFBundleDisplayName':meta['DisplayName'],'CFBundleVersion':sys.argv[2],'CFBundleShortVersionString':sys.argv[2],'CFBundlePackageType':'APPL','LSMinimumSystemVersion':'15.0','NSHighResolutionCapable':True,'NSPrincipalClass':'NSApplication','LSApplicationCategoryType':'public.app-category.news','CFBundleLocalizations':['zh-Hans','zh-Hant','en','fr']}
with open(sys.argv[1],'wb') as file:plistlib.dump(info,file)
PY
cp native/build/AppIcon.icns "$app/Contents/Resources/"
cp native/PrivacyInfo.xcprivacy "$app/Contents/Resources/"
cp ../LICENSE "$app/Contents/Resources/HeyRead-LICENSE"
cp docs/THIRD-PARTY-NOTICES.txt "$app/Contents/Resources/"
./scripts/sign-app.sh "$app"
if [ "${XIAOHEIHE_NOTARY_PROFILE:-}" != "" ]; then
  ditto -c -k --keepParent "$app" "$out/$rid/notary.zip"
  xcrun notarytool submit "$out/$rid/notary.zip" --keychain-profile "$XIAOHEIHE_NOTARY_PROFILE" --wait
  xcrun stapler staple "$app"
fi
ditto -c -k --sequesterRsrc --keepParent "$app" "$out/$slug-$version-$rid.zip"
if [ "${HEYREAD_SKIP_DMG:-0}" != 1 ]; then
  stage="$(mktemp -d)"
  trap 'rm -rf "$stage"' EXIT
  ditto "$app" "$stage/$slug.app"
  ln -s /Applications "$stage/Applications"
  hdiutil create -volname '小黑盒Community For Mac' -srcfolder "$stage" -ov -format UDZO "$out/$slug-$version-$rid.dmg"
fi
lipo "$app/Contents/MacOS/$slug" -verify_arch "$arch"
codesign --verify --deep --strict "$app"
printf '%s\n' "$app"
