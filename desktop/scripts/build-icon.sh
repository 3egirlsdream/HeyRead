#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p native/build/AppIcon.iconset
source=src/HeyRead.Desktop/Assets/app-icon.png
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$source" --out "native/build/AppIcon.iconset/icon_${size}x${size}.png" >/dev/null
  retina=$((size * 2))
  sips -z "$retina" "$retina" "$source" --out "native/build/AppIcon.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns native/build/AppIcon.iconset -o native/build/AppIcon.icns
