# OneJS Native Plugins

Platform-specific native libraries and JavaScript bridges.

## Directory Structure

```
Plugins/
├── macOS/                    # macOS native library
│   └── libquickjs_unity.dylib
├── WebGL/                    # WebGL browser bridge (Unity 6+)
│   └── OneJSWebGL.jslib
└── (future: Windows/, Linux/, iOS/, Android/)
```

## Platform Routing

The `QuickJSNative.cs` file uses conditional compilation to select the appropriate library:

| Platform | LibName | Implementation |
|----------|---------|----------------|
| Editor (macOS) | `quickjs_unity` | `macOS/libquickjs_unity.dylib` |
| Standalone macOS | `quickjs_unity` | `macOS/libquickjs_unity.dylib` |
| iOS | `__Internal` | Static linking |
| WebGL | `__Internal` | `WebGL/OneJSWebGL.jslib` |

## Adding New Platforms

1. Create platform subfolder (e.g., `Windows/`)
2. Add native library with correct naming (`quickjs_unity.dll`)
3. Configure `.meta` file to enable only for that platform
4. Update `QuickJSNative.cs` if special handling needed
