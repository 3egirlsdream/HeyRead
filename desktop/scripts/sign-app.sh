#!/bin/bash
set -euo pipefail
app="$1"
identity="${XIAOHEIHE_SIGN_IDENTITY:--}"
sign=(--force --sign "$identity")
if [[ "$identity" != - ]]; then sign+=(--options runtime --timestamp); fi
if [[ -n "${XIAOHEIHE_SIGN_KEYCHAIN:-}" ]]; then sign+=(--keychain "$XIAOHEIHE_SIGN_KEYCHAIN"); fi
while IFS= read -r -d '' binary; do
    if [[ "$(file -b "$binary")" == *Mach-O* ]]; then
        codesign "${sign[@]}" "$binary"
    fi
done < <(find "$app/Contents" -type f ! -name xiaoheihe-community-formac -print0)
# The .NET runtime also puts managed files in Contents/MacOS, where codesign can treat them as nested code.
# Seal those files before giving the main executable its entitlements.
codesign "${sign[@]}" --deep "$app"
codesign "${sign[@]}" --entitlements "$(dirname "$0")/../native/Website.entitlements" "$app"
codesign --verify --deep --strict "$app"
