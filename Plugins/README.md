# OneJS Native Plugins

Platform-specific native libraries and JavaScript bridges.

## Directory Structure

```
Plugins/
├── macOS/                    # macOS native library
│   └── libquickjs_unity.dylib
├── Windows/                  # Windows native library
│   └── x64/quickjs_unity.dll
├── WebGL/                    # WebGL browser bridge (Unity 6+)
│   └── OneJSWebGL.jslib
└── (future: Linux/, iOS/, Android/)
```

## Platform Routing

The `QuickJSNative.cs` file uses conditional compilation to select the appropriate library:

| Platform | LibName | Implementation |
|----------|---------|----------------|
| Editor (macOS) | `quickjs_unity` | `macOS/libquickjs_unity.dylib` |
| Editor (Windows) | `quickjs_unity` | `Windows/x64/quickjs_unity.dll` |
| Standalone macOS | `quickjs_unity` | `macOS/libquickjs_unity.dylib` |
| Standalone Windows | `quickjs_unity` | `Windows/x64/quickjs_unity.dll` |
| iOS | `__Internal` | Static linking |
| WebGL | `__Internal` | `WebGL/OneJSWebGL.jslib` |

## Building Native Libraries

Build scripts are located in `Auxiliary~/quickjs-unity/`:

```bash
# macOS (run on macOS)
./build.sh

# Windows (cross-compile from macOS/Linux using MinGW)
# Requires: brew install mingw-w64 (macOS) or apt install mingw-w64 (Linux)
./build-windows.sh
```

## Adding New Platforms

1. Create platform subfolder (e.g., `Linux/`)
2. Add native library with correct naming (`libquickjs_unity.so`)
3. Configure `.meta` file to enable only for that platform
4. Update `QuickJSNative.cs` if special handling needed
