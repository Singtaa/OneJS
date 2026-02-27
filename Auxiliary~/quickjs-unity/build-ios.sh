#!/bin/bash
set -e

# Build QuickJS native plugin for iOS (static library)
# iOS uses __Internal DllImport, so we produce a static .a that gets linked into the IL2CPP binary.
# Requires: Xcode with command line tools

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

MIN_IOS="13.0"
BUILD_DIR="build-ios"

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

# --- Helper ---
build_arch() {
    local ARCH=$1  # arm64 or x86_64
    local SDK=$2   # iphoneos or iphonesimulator
    local LABEL=$3 # display label

    echo "=== Building QuickJS static library ($LABEL) ==="

    local ARCH_DIR="$BUILD_DIR/$LABEL"
    local QJS_BUILD="$ARCH_DIR/quickjs"
    mkdir -p "$QJS_BUILD"

    # Copy QuickJS source to isolated build dir (Makefile builds in-tree)
    cp -R quickjs/ "$QJS_BUILD/"

    local SDK_PATH
    SDK_PATH=$(xcrun --sdk "$SDK" --show-sdk-path)
    local CC="xcrun -sdk $SDK clang -arch $ARCH -isysroot $SDK_PATH -miphoneos-version-min=$MIN_IOS"
    local AR="xcrun -sdk $SDK ar"

    # Build libquickjs.a
    make -C "$QJS_BUILD" clean 2>/dev/null || true
    make -C "$QJS_BUILD" libquickjs.a \
        CONFIG_CLANG=y \
        CC="$CC" \
        AR="$AR" \
        CONFIG_DEFAULT_AR=y

    echo "=== Compiling quickjs_unity.c ($LABEL) ==="
    $CC -O2 -fPIC \
        -I"$QJS_BUILD" \
        -c src/quickjs_unity.c \
        -o "$ARCH_DIR/quickjs_unity.o"

    echo "=== Creating static library ($LABEL) ==="
    # Extract QuickJS objects and repack everything into one .a
    mkdir -p "$ARCH_DIR/objs"
    cd "$ARCH_DIR/objs"
    $AR x "$SCRIPT_DIR/$QJS_BUILD/libquickjs.a"
    cd "$SCRIPT_DIR"

    $AR rcs "$ARCH_DIR/libquickjs_unity.a" \
        "$ARCH_DIR/quickjs_unity.o" \
        "$ARCH_DIR"/objs/*.o

    echo "  -> $ARCH_DIR/libquickjs_unity.a"
}

# --- Build device (arm64) ---
build_arch arm64 iphoneos "device-arm64"

# --- Install ---
echo "=== Installing to Plugins/iOS ==="
PLUGIN_DIR="../../Plugins/iOS"
mkdir -p "$PLUGIN_DIR"
cp "$BUILD_DIR/device-arm64/libquickjs_unity.a" "$PLUGIN_DIR/"

echo ""
echo "DONE."
echo "  Installed: Plugins/iOS/libquickjs_unity.a (arm64)"
echo ""
echo "NOTE: Set the plugin .meta to: Platform: iOS, CPU: ARM64"

# --- Cleanup ---
rm -rf "$BUILD_DIR"
