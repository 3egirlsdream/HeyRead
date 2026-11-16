#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p native/build/AppIcon.iconset
xcrun swift native/AppIcon.swift native/build/icon-1024.png
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" native/build/icon-1024.png --out "native/build/AppIcon.iconset/icon_${size}x${size}.png" >/dev/null
  retina=$((size * 2))
  sips -z "$retina" "$retina" native/build/icon-1024.png --out "native/build/AppIcon.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns native/build/AppIcon.iconset -o native/build/AppIcon.icns
