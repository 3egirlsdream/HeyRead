#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p native/build
arch="${1:-$(uname -m)}"
xcrun swiftc native/HeyReadNative.swift -emit-library -o "native/build/libHeyReadNative.dylib" -module-name HeyReadNative -target "${arch}-apple-macos15.0" -framework AppKit -framework AVKit -framework SwiftUI -framework Translation -framework CoreText -framework WebKit -Xlinker -install_name -Xlinker @rpath/libHeyReadNative.dylib
