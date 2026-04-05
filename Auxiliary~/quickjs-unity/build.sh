#!/bin/bash
set -e

# Build QuickJS native plugin for macOS
# Requires: Xcode command line tools (clang, make)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

BUILD_DIR="build-macos"

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR/quickjs"

# Copy QuickJS source to isolated build dir
# Note: "quickjs/." (not "quickjs/") ensures contents are copied on both BSD (macOS)
# and GNU (Linux) cp - without "/.", GNU cp nests the directory when dest exists.
cp -R quickjs/. "$BUILD_DIR/quickjs/"

echo "=== Building QuickJS static library for macOS ==="
make -C "$BUILD_DIR/quickjs" clean 2>/dev/null || true
make -C "$BUILD_DIR/quickjs" libquickjs.a

echo "=== Building libquickjs_unity.dylib ==="
clang -dynamiclib -O2 \
    -I"$BUILD_DIR/quickjs" \
    -o "$BUILD_DIR/libquickjs_unity.dylib" \
    src/quickjs_unity.c \
    "$BUILD_DIR/quickjs/libquickjs.a"

echo "=== Installing to Plugins/macOS ==="
cp "$BUILD_DIR/libquickjs_unity.dylib" ../../Plugins/macOS/

echo ""
echo "DONE. Generated libquickjs_unity.dylib at Plugins/macOS/"

# --- Cleanup ---
rm -rf "$BUILD_DIR"
