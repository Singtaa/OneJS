#!/bin/bash
set -e

# Cross-compile QuickJS for Windows x64 from macOS/Linux using MinGW
# Requires: brew install mingw-w64 (macOS) or apt install mingw-w64 (Linux)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

CROSS_PREFIX="x86_64-w64-mingw32-"
CC="${CROSS_PREFIX}gcc"
AR="${CROSS_PREFIX}ar"

# Verify cross-compiler exists
if ! command -v "$CC" &> /dev/null; then
    echo "Error: MinGW cross-compiler not found ($CC)"
    echo "Install with: brew install mingw-w64 (macOS) or apt install mingw-w64 (Linux)"
    exit 1
fi

BUILD_DIR="build-windows"

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR/quickjs"

# Copy QuickJS source to isolated build dir
# Note: "quickjs/." (not "quickjs/") ensures contents are copied on both BSD (macOS)
# and GNU (Linux) cp: without "/.", GNU cp nests the directory when dest exists.
cp -R quickjs/. "$BUILD_DIR/quickjs/"

echo "=== Building QuickJS static library for Windows x64 ==="
make -C "$BUILD_DIR/quickjs" clean 2>/dev/null || true
make -C "$BUILD_DIR/quickjs" CONFIG_WIN32=y CC="$CC" AR="$AR" libquickjs.a

echo "=== Building quickjs_unity.dll ==="
# -static, not just -static-libgcc: quickjs.c uses pthread mutexes and condition
# variables (the class-id lock, Atomics.wait), and MinGW's posix thread model
# satisfies those from libwinpthread. Left dynamic, the DLL imports
# libwinpthread-1.dll, which no Windows machine has and the package does not
# ship, so Unity fails the load as "DllNotFoundException: quickjs_unity".
# The check-plugin-deps.py guard exists to catch exactly this.
$CC -shared -O2 \
    -I"$BUILD_DIR/quickjs" \
    -o "$BUILD_DIR/quickjs_unity.dll" \
    src/quickjs_unity.c \
    "$BUILD_DIR/quickjs/libquickjs.a" \
    -static

echo "=== Installing to Plugins/Windows/x64 ==="
PLUGIN_DIR="../../Plugins/Windows/x64"
mkdir -p "$PLUGIN_DIR"
cp "$BUILD_DIR/quickjs_unity.dll" "$PLUGIN_DIR/"

echo ""
echo "DONE. Generated quickjs_unity.dll at Plugins/Windows/x64/"
echo ""
echo "NOTE: You may need to create a .meta file for Unity to recognize the plugin."
echo "Set the plugin settings to: Windows x64, CPU x86_64"

# --- Cleanup ---
rm -rf "$BUILD_DIR"
