#!/usr/bin/env bash
#
# Build QuickJS-backed puerts.dll for Windows x64 with a custom JS stack size.
#
# Usage:
#   ./build_quickjs_win.sh [stack_size_in_bytes]
#
# Examples:
#   ./build_quickjs_win.sh            # 2MB (default)
#   ./build_quickjs_win.sh 4194304    # 4MB
#   ./build_quickjs_win.sh 1048576    # 1MB
#
# Prerequisites:
#   - Node.js (v16+)
#   - CMake (v3.15+)
#   - Visual Studio 2019 or 2022 with C++ Desktop workload
#
# The script clones PuerTS, patches the QuickJS default max stack size,
# builds for Windows x64, and copies the resulting puerts.dll into
# Puerts/Plugins/x86_64/.
#
set -euo pipefail

PUERTS_TAG="Unity_v2.2.2_16kb"
STACK_SIZE="${1:-2097152}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ONEJS_DIR="$(dirname "$SCRIPT_DIR")"
PLUGINS_DIR="$ONEJS_DIR/Puerts/Plugins/x86_64"
BUILD_DIR="$SCRIPT_DIR/_build_tmp"

# Add VS-bundled CMake to PATH if cmake is not already available
if ! command -v cmake &>/dev/null; then
    VS_CMAKE=""
    for edition in Community Professional Enterprise BuildTools; do
        for year in 2022 2019; do
            candidate="/c/Program Files/Microsoft Visual Studio/$year/$edition/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin"
            if [ -f "$candidate/cmake.exe" ]; then
                VS_CMAKE="$candidate"
                break 2
            fi
        done
    done
    if [ -n "$VS_CMAKE" ]; then
        echo "Found CMake at: $VS_CMAKE"
        export PATH="$VS_CMAKE:$PATH"
    else
        echo "ERROR: CMake not found. Install CMake or Visual Studio with C++ workload." >&2
        exit 1
    fi
fi

echo "=== Building puerts.dll (QuickJS, Windows x64) ==="
echo "    PuerTS tag:   $PUERTS_TAG"
echo "    Stack size:   $STACK_SIZE bytes ($(( STACK_SIZE / 1024 / 1024 ))MB)"
echo ""

# Clean previous build
if [ -d "$BUILD_DIR" ]; then
    echo "Removing previous build directory..."
    rm -rf "$BUILD_DIR"
fi
mkdir -p "$BUILD_DIR"

# Clone
echo "Cloning PuerTS ($PUERTS_TAG)..."
git clone --branch "$PUERTS_TAG" --depth 1 \
    https://github.com/Tencent/puerts.git "$BUILD_DIR/puerts"

# Patch stack size
QJS_HEADER="$BUILD_DIR/puerts/unity/native_src/backend-quickjs/quickjs/quickjs.h"
echo "Patching JS_DEFAULT_STACK_SIZE to $STACK_SIZE..."
sed -i "s/#define JS_DEFAULT_STACK_SIZE (256 \* 1024)/#define JS_DEFAULT_STACK_SIZE ($STACK_SIZE)/" \
    "$QJS_HEADER"

# Verify patch applied
if grep -q "JS_DEFAULT_STACK_SIZE ($STACK_SIZE)" "$QJS_HEADER"; then
    echo "Patch applied successfully."
else
    echo "ERROR: Failed to patch quickjs.h" >&2
    exit 1
fi

# Build
echo "Building (this may take a few minutes)..."
cd "$BUILD_DIR/puerts/unity/native_src"
node ../cli make --platform win --arch x64 --backend quickjs --config Release --websocket 0

# Copy result
DLL_PATH="$BUILD_DIR/puerts/unity/native_src/build_win_x64_quickjs/Release/puerts.dll"
if [ ! -f "$DLL_PATH" ]; then
    echo "ERROR: Build succeeded but puerts.dll not found at expected path" >&2
    echo "Check $BUILD_DIR/puerts/unity/native_src/build_win_x64_quickjs/" >&2
    exit 1
fi

echo "Copying puerts.dll to $PLUGINS_DIR/"
cp "$DLL_PATH" "$PLUGINS_DIR/puerts.dll"

# Cleanup
echo "Cleaning up build directory..."
rm -rf "$BUILD_DIR"

echo ""
echo "=== Done! ==="
echo "Updated: $PLUGINS_DIR/puerts.dll"
echo "QuickJS max stack size: $STACK_SIZE bytes"
