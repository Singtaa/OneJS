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

echo "=== Building QuickJS static library for Windows x64 ==="
cd quickjs
make clean 2>/dev/null || true
make CONFIG_WIN32=y CC="$CC" AR="$AR" libquickjs.a
cd ..

echo "=== Building quickjs_unity.dll ==="
$CC -shared -O2 \
    -I./quickjs \
    -o quickjs_unity.dll \
    src/quickjs_unity.c \
    quickjs/libquickjs.a \
    -static-libgcc

echo "=== Installing to Plugins/Windows/x64 ==="
PLUGIN_DIR="../../Plugins/Windows/x64"
mkdir -p "$PLUGIN_DIR"
cp quickjs_unity.dll "$PLUGIN_DIR/"

echo ""
echo "DONE. Generated quickjs_unity.dll at Plugins/Windows/x64/"
echo ""
echo "NOTE: You may need to create a .meta file for Unity to recognize the plugin."
echo "Set the plugin settings to: Windows x64, CPU x86_64"
