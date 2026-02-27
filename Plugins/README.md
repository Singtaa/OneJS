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

# Linux
./build-linux.sh

# iOS
./build-ios.sh         # Requires: Xcode

# Android
./build-android.sh     # Requires: Android NDK (set NDK_ROOT)
```

See `Auxiliary~/quickjs-unity/README.md` for full details.
