#!/bin/bash
set -e

# Build QuickJS native plugin for macOS
# Requires: Xcode command line tools (clang, make)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

BUILD_DIR="build-macos"

# Universal, not host-only. Unity's own guidance for shipping a Mac game is to
# ship a Universal binary, and the plugin's .meta offers this library to
# OSXUniversal builds. A host-only build made that offer false: the x86_64 slice
# of a Universal player had no library to load and died with
# "DllNotFoundException: quickjs_unity" in a player's hands, while building
# clean and running fine on the Apple Silicon machine that produced it.
ARCHS="x86_64 arm64"

# Below every macOS that Unity 6 supports, so the floor here is never the thing
# that decides whether a build runs. Without it the SDK's own default applies,
# which tracks whatever Xcode is installed (15.5 at the time of writing) and
# would exclude most of the Intel machines this is universal for.
MACOS_MIN="11.0"

rm -rf "$BUILD_DIR"

for ARCH in $ARCHS; do
    echo "=== Building QuickJS static library for macOS ($ARCH) ==="
    mkdir -p "$BUILD_DIR/$ARCH/quickjs"

    # Copy QuickJS source to isolated build dir
    # Note: "quickjs/." (not "quickjs/") ensures contents are copied on both BSD (macOS)
    # and GNU (Linux) cp: without "/.", GNU cp nests the directory when dest exists.
    # Each arch gets its own copy: the Makefile keeps objects in the tree, so one
    # shared copy would link the second arch against the first one's .o files.
    cp -R quickjs/. "$BUILD_DIR/$ARCH/quickjs/"

    make -C "$BUILD_DIR/$ARCH/quickjs" clean 2>/dev/null || true
    make -C "$BUILD_DIR/$ARCH/quickjs" \
        CC="clang -arch $ARCH -mmacosx-version-min=$MACOS_MIN" \
        libquickjs.a

    echo "=== Building libquickjs_unity.dylib ($ARCH) ==="
    clang -dynamiclib -O2 -arch "$ARCH" -mmacosx-version-min="$MACOS_MIN" \
        -I"$BUILD_DIR/$ARCH/quickjs" \
        -o "$BUILD_DIR/libquickjs_unity.$ARCH.dylib" \
        src/quickjs_unity.c \
        "$BUILD_DIR/$ARCH/quickjs/libquickjs.a"
done

echo "=== Joining the slices ==="
lipo -create -output "$BUILD_DIR/libquickjs_unity.dylib" \
    "$BUILD_DIR/libquickjs_unity.x86_64.dylib" \
    "$BUILD_DIR/libquickjs_unity.arm64.dylib"
lipo -info "$BUILD_DIR/libquickjs_unity.dylib"

echo "=== Installing to Plugins/macOS ==="
cp "$BUILD_DIR/libquickjs_unity.dylib" ../../Plugins/macOS/

echo ""
echo "DONE. Generated a universal libquickjs_unity.dylib at Plugins/macOS/"
echo "Verify with: python3 check-plugin-deps.py && python3 load-plugin-smoke.py"

# --- Cleanup ---
rm -rf "$BUILD_DIR"
