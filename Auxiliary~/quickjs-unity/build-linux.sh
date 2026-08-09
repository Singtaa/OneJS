#!/bin/bash
set -e

# Build QuickJS native plugin for Linux x64
# Requires: gcc, make

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

BUILD_DIR="build-linux"

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR/quickjs"

# Copy QuickJS source to isolated build dir
# Note: "quickjs/." (not "quickjs/") ensures contents are copied on both BSD (macOS)
# and GNU (Linux) cp: without "/.", GNU cp nests the directory when dest exists.
cp -R quickjs/. "$BUILD_DIR/quickjs/"

echo "=== Building QuickJS static library for Linux x64 ==="
make -C "$BUILD_DIR/quickjs" clean 2>/dev/null || true
make -C "$BUILD_DIR/quickjs" libquickjs.a \
    CFLAGS_OPT="-O2 -fPIC -fwrapv -funsigned-char -D_GNU_SOURCE -DCONFIG_VERSION='\"$(cat quickjs/VERSION)\"'"

echo "=== Building libquickjs_unity.so ==="
gcc -shared -O2 -fPIC \
    -I"$BUILD_DIR/quickjs" \
    -o "$BUILD_DIR/libquickjs_unity.so" \
    src/quickjs_unity.c \
    "$BUILD_DIR/quickjs/libquickjs.a" \
    -lm

echo "=== Installing to Plugins/Linux/x64 ==="
PLUGIN_DIR="../../Plugins/Linux/x64"
mkdir -p "$PLUGIN_DIR"
cp "$BUILD_DIR/libquickjs_unity.so" "$PLUGIN_DIR/"

echo ""
echo "DONE. Generated libquickjs_unity.so at Plugins/Linux/x64/"

# --- Cleanup ---
rm -rf "$BUILD_DIR"
