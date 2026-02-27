# quickjs-unity

Native QuickJS engine with Unity C# interop wrapper.

## Structure

```
quickjs-unity/
├── quickjs/                 # QuickJS source (Bellard's engine)
│   ├── quickjs.c/h         # Core JS engine
│   ├── libregexp.c         # Regex support
│   ├── libunicode.c        # Unicode support
│   └── Makefile
├── src/
│   └── quickjs_unity.c     # Unity wrapper: CS proxy, handles, callbacks
├── build.sh                # macOS build
├── build-linux.sh          # Linux x64 build
├── build-windows.sh        # Windows cross-compile (MinGW)
├── build-windows-msvc.bat  # Windows native (MSVC)
├── build-ios.sh            # iOS build (static .a)
├── build-android.sh        # Android build (shared .so)
└── CMakeLists.txt          # CMake build (Windows MSVC)
```

## Building

### macOS
```bash
./build.sh
# Output: Plugins/macOS/libquickjs_unity.dylib
```

### Linux
```bash
./build-linux.sh
# Output: Plugins/Linux/x64/libquickjs_unity.so
```

### Windows (cross-compile from macOS/Linux)
```bash
./build-windows.sh     # Requires: mingw-w64
# Output: Plugins/Windows/x64/quickjs_unity.dll
```

### Windows (native MSVC)
```batch
build-windows-msvc.bat
```

### iOS
```bash
./build-ios.sh
# Output: Plugins/iOS/libquickjs_unity.a (static, arm64)
# Requires: Xcode with command line tools
```
iOS uses static linking (`__Internal` DllImport) — the library is linked into the IL2CPP binary.

### Android
```bash
./build-android.sh
# Output: Plugins/Android/{arm64-v8a,armeabi-v7a,x86_64}/libquickjs_unity.so
# Requires: Android NDK (set NDK_ROOT, ANDROID_NDK_HOME, or ANDROID_NDK_ROOT)
```

## Key Native Functions

| Function | Purpose |
|----------|---------|
| `qjs_create()` | Create JS context |
| `qjs_destroy()` | Destroy context |
| `qjs_eval()` | Evaluate JS code |
| `qjs_execute_pending_jobs()` | Process Promise queue |
| `qjs_set_cs_invoke_callback()` | Register C# dispatch handler |
| `qjs_invoke_callback()` | Call JS callback from C# |

## Handle System

C# objects are tracked via integer handles:
- `qjs_register_object()` - Store C# object, get handle
- `qjs_get_object()` - Retrieve C# object by handle
- `qjs_release_handle()` - Release handle when JS object is GC'd
