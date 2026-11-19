#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-osx-arm64}"
out="${2:-$PWD/artifacts}"
case "$rid" in osx-arm64) arch=arm64;; osx-x64) arch=x86_64;; *) echo 'Expected osx-arm64 or osx-x64' >&2; exit 1;; esac
version=$(python3 scripts/release/release.py version)
slug=xiaoheihe-community-formac
identity="${XIAOHEIHE_SIGN_IDENTITY:--}"
notary_profile="${XIAOHEIHE_NOTARY_PROFILE:-}"
if [[ -n "$notary_profile" && "$identity" == - ]]; then
  echo 'Apple notarization requires a Developer ID Application signing identity' >&2
  exit 1
fi
mkdir -p "$out"
out="$(cd "$out" && pwd)"
scratch=$(mktemp -d "$out/.package-$rid.XXXXXX")
trap 'rm -rf "$scratch"' EXIT

notarize() {
  local archive="$1" target="$2"
  local args=(--keychain-profile "$notary_profile" --wait)
  if [[ -n "${XIAOHEIHE_SIGN_KEYCHAIN:-}" ]]; then args+=(--keychain "$XIAOHEIHE_SIGN_KEYCHAIN"); fi
  xcrun notarytool submit "$archive" "${args[@]}"
  xcrun stapler staple "$target"
  xcrun stapler validate "$target"
}

./scripts/build-native.sh "$arch" "$scratch/libHeyReadNative.dylib"
./scripts/build-icon.sh
app="$scratch/$slug.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish src/HeyRead.Desktop -c Release -r "$rid" --self-contained true -p:Version="$version" -p:PublishSingleFile=false -p:RestoreLockedMode=true -o "$app/Contents/MacOS"
lipo "$scratch/libHeyReadNative.dylib" -verify_arch "$arch"
cp "$scratch/libHeyReadNative.dylib" "$app/Contents/MacOS/"
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
if [[ -n "$notary_profile" ]]; then
  ditto -c -k --keepParent "$app" "$scratch/notary.zip"
  notarize "$scratch/notary.zip" "$app"
fi
zip="$scratch/$slug-$version-$rid.zip"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
if [ "${HEYREAD_SKIP_DMG:-0}" != 1 ]; then
  stage="$scratch/dmg"
  mkdir "$stage"
  ditto "$app" "$stage/$slug.app"
  ln -s /Applications "$stage/Applications"
  dmg="$scratch/$slug-$version-$rid.dmg"
  hdiutil create -volname '小黑盒Community For Mac' -srcfolder "$stage" -ov -format UDZO "$dmg"
  if [[ "$identity" != - ]]; then
    sign=(--force --sign "$identity" --timestamp)
    if [[ -n "${XIAOHEIHE_SIGN_KEYCHAIN:-}" ]]; then sign+=(--keychain "$XIAOHEIHE_SIGN_KEYCHAIN"); fi
    codesign "${sign[@]}" "$dmg"
    codesign --verify --strict "$dmg"
  fi
  if [[ -n "$notary_profile" ]]; then notarize "$dmg" "$dmg"; fi
fi
lipo "$app/Contents/MacOS/$slug" -verify_arch "$arch"
codesign --verify --deep --strict "$app"

destination="$out/$rid/$slug.app"
mkdir -p "$out/$rid"
if [[ -d "$destination" ]]; then mv "$destination" "$scratch/previous.app"; fi
if ! mv "$app" "$destination"; then
  if [[ -d "$scratch/previous.app" ]]; then mv "$scratch/previous.app" "$destination"; fi
  exit 1
fi
mv "$zip" "$out/"
if [[ -n "${dmg:-}" ]]; then mv "$dmg" "$out/"; fi
printf '%s\n' "$destination"
