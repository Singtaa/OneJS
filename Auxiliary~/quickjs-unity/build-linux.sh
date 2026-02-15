#!/bin/bash
set -e

# Build QuickJS native plugin for Linux x64
# Requires: gcc, make

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Building QuickJS static library for Linux x64 ==="
cd quickjs
make clean 2>/dev/null || true
make libquickjs.a
cd ..

echo "=== Building libquickjs_unity.so ==="
gcc -shared -O2 -fPIC \
    -I./quickjs \
    -o libquickjs_unity.so \
    src/quickjs_unity.c \
    quickjs/libquickjs.a

echo "=== Installing to Plugins/Linux/x64 ==="
PLUGIN_DIR="../../Plugins/Linux/x64"
mkdir -p "$PLUGIN_DIR"
cp libquickjs_unity.so "$PLUGIN_DIR/"

echo ""
echo "DONE. Generated libquickjs_unity.so at Plugins/Linux/x64/"
