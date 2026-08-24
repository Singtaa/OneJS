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
