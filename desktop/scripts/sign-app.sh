#!/bin/bash
set -euo pipefail
app="$1"
identity="${XIAOHEIHE_SIGN_IDENTITY:--}"
while IFS= read -r -d '' binary; do
    if [[ "$(file -b "$binary")" == *Mach-O* ]]; then
        args=(--force --sign "$identity")
        if [[ "$identity" != - ]]; then args+=(--options runtime --timestamp); fi
        codesign "${args[@]}" "$binary"
    fi
done < <(find "$app/Contents" -type f ! -name xiaoheihe-community-formac -print0)
args=(--force --deep --sign "$identity" --entitlements "$(dirname "$0")/../native/Website.entitlements")
if [[ "$identity" != - ]]; then args+=(--options runtime --timestamp); fi
codesign "${args[@]}" "$app"
codesign --verify --deep --strict "$app"
