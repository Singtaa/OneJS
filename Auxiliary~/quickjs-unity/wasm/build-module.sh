#!/bin/bash
# Builds a native WebAssembly module library for OneJS from a .wasm.
#
#     wasm/build-module.sh <module.wasm> <name> <out dir> <platform>...
#
# platform is any of: macos windows linux android ios (or all). The libraries
# land in <out dir> laid out as OneJS's own Plugins/ folder is:
#
#     macOS/libonejs_<name>.dylib          universal, arm64 and x86_64
#     Windows/x64/onejs_<name>.dll
#     Linux/x64/libonejs_<name>.so         built in ubuntu:22.04, glibc 2.35 at most
#     Android/<abi>/libonejs_<name>.so     arm64-v8a, armeabi-v7a, x86_64
#     iOS/libonejs_<name>.a                arm64, one prelinked object
#
# Each exports one function, onejs_wasm_<name>(), returning the table C# hands
# to QuickJSNative.RegisterWasmModule. Nothing else is visible: on iOS, where
# every module is linked into one binary, each module's copy of the wasm2c
# runtime is made local by prelinking, so two modules never collide.
#
# Memory is bounds checked rather than guarded. wasm2c's guard pages need a
# SIGSEGV handler that takes every fault for a wasm trap, Mono's and IL2CPP's
# null-reference faults included, and reserve 8 GB of address space per
# instance, which iOS does not give. Checks cost code size (Rapier is 9.3 MB of
# arm64 code checked, 3.7 MB guarded), not correctness.
#
# wasm2c comes from WABT_DIR when set (a WABT 1.0.42 release folder), else the
# pinned release for this host is downloaded once, checked against its sha256,
# and kept in ~/.cache/onejs-wabt.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WABT_VERSION="1.0.42"
MACOS_MIN="11.0"
IOS_MIN="13.0"
ANDROID_API=21
DEFINES="-DWASM_RT_USE_MMAP=0 -DWASM_RT_MEMCHECK_GUARD_PAGES=0 -DWASM_RT_MEMCHECK_BOUNDS_CHECK=1"

if [ $# -lt 4 ]; then
    sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
fi

WASM="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
NAME="$2"
mkdir -p "$3"
OUT="$(cd "$3" && pwd)"
shift 3
PLATFORMS="$*"
[ "$PLATFORMS" = "all" ] && PLATFORMS="macos windows linux android ios"
for P in $PLATFORMS; do
    case "$P" in
        macos|windows|linux|android|ios) ;;
        *) echo "Unknown platform: $P (macos windows linux android ios, or all)"; exit 2 ;;
    esac
done
if ! [[ "$NAME" =~ ^[a-z][a-z0-9_]*$ ]]; then
    echo "The name becomes a C identifier and a library name: lowercase letters, digits and _ only ($NAME)"
    exit 2
fi

BUILD="$SCRIPT_DIR/build-$NAME"
GEN="$BUILD/gen"
rm -rf "$BUILD"
mkdir -p "$GEN"
trap 'rm -rf "$BUILD"' EXIT

# --- wasm2c ---

wabt_dir() {
    if [ -n "${WABT_DIR:-}" ]; then
        echo "$WABT_DIR"
        return
    fi
    local ASSET SHA
    case "$(uname -s)-$(uname -m)" in
        Darwin-arm64)  ASSET="macos-arm64"; SHA="3f654779b436c628db3ca4323c51538815f5fbc887a6d5f3bb1c18d36281e240" ;;
        Linux-x86_64)  ASSET="linux-x64";   SHA="84895407a6bbb80e918f33b16b2fb2206021c150b6bc9ff6f761263a745ab131" ;;
        Linux-aarch64) ASSET="linux-arm64"; SHA="a7632c1cc66f70886441842ecf5a2ffa91a98c79e026059458451234ad013ea8" ;;
        *) echo "No pinned WABT release for this host; set WABT_DIR to a WABT $WABT_VERSION folder" >&2; exit 1 ;;
    esac
    local CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/onejs-wabt/$WABT_VERSION-$ASSET"
    if [ ! -x "$CACHE/bin/wasm2c" ]; then
        local TGZ="$CACHE.tar.gz"
        mkdir -p "$(dirname "$CACHE")"
        curl -fsSL -o "$TGZ" "https://github.com/WebAssembly/wabt/releases/download/$WABT_VERSION/wabt-$WABT_VERSION-$ASSET.tar.gz"
        local GOT
        GOT=$(shasum -a 256 "$TGZ" | cut -d' ' -f1)
        if [ "$GOT" != "$SHA" ]; then
            rm -f "$TGZ"
            echo "WABT $WABT_VERSION $ASSET has sha256 $GOT, expected $SHA" >&2
            exit 1
        fi
        rm -rf "$CACHE" && mkdir -p "$CACHE"
        tar -xzf "$TGZ" -C "$CACHE" --strip-components 1
        rm -f "$TGZ"
    fi
    echo "$CACHE"
}

WABT="$(wabt_dir)"
echo "=== Generating C for $NAME from $(basename "$WASM") ==="
python3 "$SCRIPT_DIR/wasm2onejs.py" "$WASM" "$NAME" "$GEN" --wasm2c "$WABT/bin/wasm2c"

# Everything a compile needs sits in gen/, so a container can build from it alone.
cp "$WABT"/include/wasm-rt*.h "$GEN/"
cp "$WABT"/share/wabt/wasm2c/wasm-rt-* "$GEN/"
cp "$SCRIPT_DIR/onejs_wasm.h" "$GEN/"
SOURCES="$NAME.c ${NAME}_onejs.c wasm-rt-impl.c wasm-rt-mem-impl.c wasm-rt-exceptions-impl.c"

# Compiles every source with $1 (a compiler command) into $2/, in parallel.
compile() {
    local CC="$1" DIR="$2"
    mkdir -p "$DIR"
    local PIDS=""
    for SRC in $SOURCES; do
        (cd "$GEN" && $CC -O2 -fPIC -fvisibility=hidden $DEFINES -I. -c "$SRC" -o "$DIR/${SRC%.c}.o") &
        PIDS="$PIDS $!"
    done
    for PID in $PIDS; do wait "$PID"; done
}

objects() {
    local DIR="$1"
    for SRC in $SOURCES; do printf '%s ' "$DIR/${SRC%.c}.o"; done
}

build_macos() {
    echo "=== macOS (arm64, x86_64) ==="
    local ARCH
    for ARCH in arm64 x86_64; do
        compile "clang -arch $ARCH -mmacosx-version-min=$MACOS_MIN" "$BUILD/macos-$ARCH" &
    done
    wait
    for ARCH in arm64 x86_64; do
        # shellcheck disable=SC2046
        clang -dynamiclib -arch "$ARCH" -mmacosx-version-min="$MACOS_MIN" \
            -install_name "@rpath/libonejs_$NAME.dylib" \
            -o "$BUILD/macos-$ARCH/libonejs_$NAME.dylib" $(objects "$BUILD/macos-$ARCH")
        strip -x "$BUILD/macos-$ARCH/libonejs_$NAME.dylib"
    done
    mkdir -p "$OUT/macOS"
    lipo -create -output "$OUT/macOS/libonejs_$NAME.dylib" \
        "$BUILD/macos-arm64/libonejs_$NAME.dylib" "$BUILD/macos-x86_64/libonejs_$NAME.dylib"
}

build_windows() {
    echo "=== Windows x64 (MinGW) ==="
    local CC="x86_64-w64-mingw32-gcc"
    command -v "$CC" > /dev/null || { echo "MinGW not found: brew install mingw-w64"; exit 1; }
    compile "$CC" "$BUILD/windows"
    mkdir -p "$OUT/Windows/x64"
    # shellcheck disable=SC2046
    $CC -shared -static -s -o "$OUT/Windows/x64/onejs_$NAME.dll" $(objects "$BUILD/windows")
}

build_linux() {
    echo "=== Linux x64 (ubuntu:22.04) ==="
    if [ "$(uname -s)" = "Linux" ] && [ "${ONEJS_IN_CONTAINER:-}" = "1" ]; then
        compile "gcc" "$BUILD/linux"
        mkdir -p "$OUT/Linux/x64"
        # shellcheck disable=SC2046
        gcc -shared -s -o "$OUT/Linux/x64/libonejs_$NAME.so" $(objects "$BUILD/linux") -lm
        return
    fi
    # The same image the engine's .so is built in, so the glibc floor matches.
    docker run --rm --platform linux/amd64 -e ONEJS_IN_CONTAINER=1 \
        -v "$GEN":/gen -v "$OUT":/out public.ecr.aws/docker/library/ubuntu:22.04 bash -c "
            set -e
            apt-get update -qq && apt-get install -y -qq gcc > /dev/null
            cd /gen && for SRC in $SOURCES; do gcc -O2 -fPIC -fvisibility=hidden $DEFINES -I. -c \$SRC -o \${SRC%.c}.o & done; wait
            mkdir -p /out/Linux/x64
            gcc -shared -s -o /out/Linux/x64/libonejs_$NAME.so $(for S in $SOURCES; do printf '%s ' "${S%.c}.o"; done) -lm"
}

android_ndk() {
    local NDK="${NDK_ROOT:-${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}}"
    if [ -z "$NDK" ]; then
        NDK=$(ls -d /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/NDK \
            "$HOME"/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/NDK 2>/dev/null | tail -1 || true)
    fi
    [ -n "$NDK" ] && [ -d "$NDK" ] || { echo "Android NDK not found: set NDK_ROOT" >&2; exit 1; }
    case "$(uname -s)" in
        Darwin) echo "$NDK/toolchains/llvm/prebuilt/darwin-x86_64" ;;
        *)      echo "$NDK/toolchains/llvm/prebuilt/linux-x86_64" ;;
    esac
}

build_android() {
    echo "=== Android (arm64-v8a, armeabi-v7a, x86_64) ==="
    local TOOLCHAIN ABI TRIPLE
    TOOLCHAIN="$(android_ndk)"
    for PAIR in arm64-v8a:aarch64-linux-android armeabi-v7a:armv7a-linux-androideabi x86_64:x86_64-linux-android; do
        ABI="${PAIR%%:*}"
        TRIPLE="${PAIR#*:}"
        compile "$TOOLCHAIN/bin/${TRIPLE}${ANDROID_API}-clang" "$BUILD/android-$ABI" &
    done
    wait
    for PAIR in arm64-v8a:aarch64-linux-android armeabi-v7a:armv7a-linux-androideabi x86_64:x86_64-linux-android; do
        ABI="${PAIR%%:*}"
        TRIPLE="${PAIR#*:}"
        mkdir -p "$OUT/Android/$ABI"
        # shellcheck disable=SC2046
        "$TOOLCHAIN/bin/${TRIPLE}${ANDROID_API}-clang" -shared \
            -Wl,-z,max-page-size=16384 -Wl,-z,relro,-z,now \
            -o "$OUT/Android/$ABI/libonejs_$NAME.so" $(objects "$BUILD/android-$ABI") -lm
        "$TOOLCHAIN/bin/llvm-strip" "$OUT/Android/$ABI/libonejs_$NAME.so"
    done
}

build_ios() {
    echo "=== iOS arm64 ==="
    local SDK_PATH
    SDK_PATH=$(xcrun --sdk iphoneos --show-sdk-path)
    compile "xcrun -sdk iphoneos clang -arch arm64 -isysroot $SDK_PATH -miphoneos-version-min=$IOS_MIN" "$BUILD/ios"
    # One object, in which every hidden symbol becomes local: two modules
    # linked into one app each keep their own wasm2c runtime.
    # shellcheck disable=SC2046
    xcrun -sdk iphoneos ld -r -arch arm64 -o "$BUILD/ios/onejs_$NAME.o" $(objects "$BUILD/ios")
    mkdir -p "$OUT/iOS"
    rm -f "$OUT/iOS/libonejs_$NAME.a"
    xcrun -sdk iphoneos ar rcs "$OUT/iOS/libonejs_$NAME.a" "$BUILD/ios/onejs_$NAME.o"
}

for P in $PLATFORMS; do
    "build_$P"
done

echo ""
echo "DONE. $NAME, sha256 $(shasum -a 256 "$WASM" | cut -d' ' -f1):"
(cd "$OUT" && find . -name "*onejs_$NAME*" -type f | sort | while read -r F; do echo "  $F ($(du -h "$F" | cut -f1))"; done)
