# OneJS Native Plugins

Platform-specific native libraries and JavaScript bridges.

## Directory Structure

```
Plugins/
├── macOS/                           # macOS native library
│   └── libquickjs_unity.dylib
├── Windows/                         # Windows native library
│   └── x64/quickjs_unity.dll
├── Linux/                           # Linux native library
│   └── x64/libquickjs_unity.so
├── iOS/                             # iOS static library (linked into IL2CPP binary)
│   └── libquickjs_unity.a
├── Android/                         # Android shared libraries (per-ABI)
│   ├── arm64-v8a/libquickjs_unity.so
│   ├── armeabi-v7a/libquickjs_unity.so
│   └── x86_64/libquickjs_unity.so
└── WebGL/                           # WebGL browser bridge (Unity 6+)
    └── OneJSWebGL.jslib
```

## Platform Routing

The `QuickJSNative.cs` file uses conditional compilation to select the appropriate library:

| Platform | LibName | Implementation |
|----------|---------|----------------|
| Editor (macOS) | `quickjs_unity` | `macOS/libquickjs_unity.dylib` |
| Editor (Windows) | `quickjs_unity` | `Windows/x64/quickjs_unity.dll` |
| Standalone macOS | `quickjs_unity` | `macOS/libquickjs_unity.dylib` |
| Standalone Windows | `quickjs_unity` | `Windows/x64/quickjs_unity.dll` |
| Linux | `quickjs_unity` | `Linux/x64/libquickjs_unity.so` |
| iOS | `__Internal` | `iOS/libquickjs_unity.a` (static linking) |
| Android | `quickjs_unity` | `Android/{ABI}/libquickjs_unity.so` |
| WebGL | `__Internal` | `WebGL/OneJSWebGL.jslib` |

## Building Native Libraries

Build scripts are located in `Auxiliary~/quickjs-unity/`. Each script compiles and copies the output to this `Plugins/` folder.

```bash
# macOS
./build.sh

# Windows (cross-compile from macOS/Linux)
./build-windows.sh     # Requires: mingw-w64

# Windows (native MSVC)
build-windows-msvc.bat

# Linux (the shipped .so must come out of the pinned container, see below)
./build-linux.sh

# iOS
./build-ios.sh         # Requires: Xcode

# Android
./build-android.sh     # Requires: Android NDK (set NDK_ROOT)
```

See `Auxiliary~/quickjs-unity/README.md` for full details.

## macOS Universal Binary

The committed `macOS/libquickjs_unity.dylib` is a universal binary carrying both
`x86_64` and `arm64` slices, built with a deployment target of macOS 11.0.

Both facts are load-bearing and neither is the default. `build.sh` with no
`-arch` flags produces a host-only library, and the plugin's `.meta` offers this
file to `OSXUniversal` builds with `CPU: AnyCPU`, so a host-only build makes that
offer false: the x86_64 slice of a Universal player has nothing to load and dies
with `DllNotFoundException: quickjs_unity`, having built cleanly and run fine on
the Apple Silicon machine that produced it. Unity's own guidance for shipping a
Mac game is to ship a Universal binary, so this is the configuration users are
told to use. Without an explicit `-mmacosx-version-min` the SDK's default applies
and tracks whichever Xcode is installed, which would exclude most of the Intel
machines the x86_64 slice exists for.

Verify both slices actually load, not just that they are present:

```bash
arch -arm64  /usr/bin/python3 -c "import ctypes;print(ctypes.CDLL('Plugins/macOS/libquickjs_unity.dylib').qjs_abi_version())"
arch -x86_64 /usr/bin/python3 -c "import ctypes;print(ctypes.CDLL('Plugins/macOS/libquickjs_unity.dylib').qjs_abi_version())"
```

The second runs under Rosetta 2 and is a real x86_64 process doing a real dyld
load, so it tests the slice rather than inspecting it. Rosetta is removed for
general use in macOS 28, and Unity drops Intel entirely in 6.8, so both this
check and the x86_64 slice have a finite life.

## Dependency Check

Every file here is a committed build artifact, and only the Linux .so is rebuilt
by CI, so a dependency picked up from whichever machine produced a binary ships
unnoticed and fails at load time on a clean one. Verify after refreshing any of
them:

```bash
python3 Auxiliary~/quickjs-unity/check-plugin-deps.py
```

It reads each binary's dependency list out of the container format itself and
rejects anything an end user's machine would not already have. CI runs it on
every push.

## Linux glibc Baseline

The committed `Linux/x64/libquickjs_unity.so` is built inside an `ubuntu:22.04`
container so it links against glibc 2.35 at most (Unity 6's own Ubuntu 22.04
floor). glibc is backward compatible, so the binary runs on any newer distro;
building on a bare dev machine instead would silently raise that floor. Refresh
it whenever `src/quickjs_unity.c` or the vendored QuickJS submodule changes,
with the same command CI uses (add `--platform linux/amd64` on Apple Silicon):

```bash
cd <package root>
docker run --rm -v "$PWD":/repo -w /repo ubuntu:22.04 bash -c \
  "apt-get update -qq && apt-get install -y -qq gcc make > /dev/null && bash Auxiliary~/quickjs-unity/build-linux.sh"
```

CI rebuilds the .so from source every run (overwriting the committed one for
the test run), so the build script and source stay verified even if the
committed binary lags a source change.
