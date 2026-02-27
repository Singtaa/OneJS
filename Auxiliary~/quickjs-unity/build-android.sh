#!/bin/bash
set -e

# Build QuickJS native plugin for Android (shared library)
# Produces .so files for arm64-v8a, armeabi-v7a, and x86_64.
# Requires: Android NDK (set NDK_ROOT, ANDROID_NDK_HOME, or ANDROID_NDK_ROOT)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

API_LEVEL=21
BUILD_DIR="build-android"

# --- Locate NDK ---
NDK="${NDK_ROOT:-${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}}"

# Try Unity's bundled NDK as fallback
if [ -z "$NDK" ]; then
    for CANDIDATE in \
        "/Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/NDK" \
        "$HOME/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/NDK"; do
        FOUND=$(ls -d $CANDIDATE 2>/dev/null | tail -1)
        if [ -n "$FOUND" ]; then
            NDK="$FOUND"
            break
        fi
    done
fi

if [ -z "$NDK" ] || [ ! -d "$NDK" ]; then
    echo "Error: Android NDK not found."
    echo "Set one of: NDK_ROOT, ANDROID_NDK_HOME, or ANDROID_NDK_ROOT"
    echo "Or install via Android Studio / Unity Hub."
    exit 1
fi
echo "Using NDK: $NDK"

# Detect host platform
case "$(uname -s)" in
    Darwin) HOST_TAG="darwin-x86_64" ;;
    Linux)  HOST_TAG="linux-x86_64" ;;
    *)      echo "Error: Unsupported host OS"; exit 1 ;;
esac

TOOLCHAIN="$NDK/toolchains/llvm/prebuilt/$HOST_TAG"
if [ ! -d "$TOOLCHAIN" ]; then
    echo "Error: NDK toolchain not found at $TOOLCHAIN"
    exit 1
fi

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

# --- Helper ---
build_abi() {
    local ABI=$1
    local TRIPLE=$2

    echo ""
    echo "=== Building for $ABI ==="

    local ABI_DIR="$BUILD_DIR/$ABI"
    local QJS_BUILD="$ABI_DIR/quickjs"
    mkdir -p "$QJS_BUILD"

    # Copy QuickJS source to isolated build dir
    cp -R quickjs/ "$QJS_BUILD/"

    local CC="$TOOLCHAIN/bin/${TRIPLE}${API_LEVEL}-clang"
    local AR="$TOOLCHAIN/bin/llvm-ar"

    if [ ! -f "$CC" ]; then
        echo "Error: Compiler not found: $CC"
        exit 1
    fi

    # Build libquickjs.a (with -fPIC so objects can be linked into a shared library)
    make -C "$QJS_BUILD" clean 2>/dev/null || true
    make -C "$QJS_BUILD" libquickjs.a \
        CONFIG_CLANG=y \
        CC="$CC" \
        AR="$AR" \
        CONFIG_DEFAULT_AR=y \
        CFLAGS_OPT="-O2 -fPIC -fwrapv -D_GNU_SOURCE -DCONFIG_VERSION=\\\"2025-09-13\\\""

    echo "  Building libquickjs_unity.so..."
    $CC -shared -O2 -fPIC \
        -Wl,-z,max-page-size=16384 \
        -I"$QJS_BUILD" \
        -o "$ABI_DIR/libquickjs_unity.so" \
        src/quickjs_unity.c \
        "$QJS_BUILD/libquickjs.a" \
        -lm

    # Strip debug symbols for smaller binary
    "$TOOLCHAIN/bin/llvm-strip" "$ABI_DIR/libquickjs_unity.so"

    echo "  -> $ABI_DIR/libquickjs_unity.so ($(du -h "$ABI_DIR/libquickjs_unity.so" | cut -f1))"
}

# --- Build all ABIs ---
build_abi arm64-v8a   aarch64-linux-android
build_abi armeabi-v7a armv7a-linux-androideabi
build_abi x86_64      x86_64-linux-android

# --- Install ---
echo ""
echo "=== Installing to Plugins/Android ==="
PLUGIN_DIR="../../Plugins/Android"

for ABI in arm64-v8a armeabi-v7a x86_64; do
    mkdir -p "$PLUGIN_DIR/$ABI"
    cp "$BUILD_DIR/$ABI/libquickjs_unity.so" "$PLUGIN_DIR/$ABI/"
    echo "  $PLUGIN_DIR/$ABI/libquickjs_unity.so"
done

echo ""
echo "DONE."
echo ""
echo "NOTE: Each .so needs a .meta file with the correct Unity plugin settings:"
echo "  arm64-v8a:    Platform: Android, CPU: ARM64"
echo "  armeabi-v7a:  Platform: Android, CPU: ARMv7"
echo "  x86_64:       Platform: Android, CPU: x86_64"

# --- Cleanup ---
rm -rf "$BUILD_DIR"
