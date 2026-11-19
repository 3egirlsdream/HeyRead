#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
arch="${1:-$(uname -m)}"
output="${2:-native/build/libHeyReadNative.dylib}"
mkdir -p "$(dirname "$output")"
xcrun swiftc native/HeyReadNative.swift -emit-library -o "$output" -module-name HeyReadNative -target "${arch}-apple-macos15.0" -framework AppKit -framework AVKit -framework SwiftUI -framework Translation -framework CoreText -framework WebKit -Xlinker -install_name -Xlinker @rpath/libHeyReadNative.dylib
