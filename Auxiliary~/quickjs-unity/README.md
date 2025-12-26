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
├── build.sh                # macOS build script
└── libquickjs_unity.dylib  # Compiled library (macOS)
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

## Building (macOS)

```bash
./build.sh
# Output: libquickjs_unity.dylib → copy to Assets/Singtaa/OneJS/Plugins/macOS/
```

## Build Flags

```bash
-DCONFIG_VERSION="2024-02-14"
-DCONFIG_BIGNUM        # BigInt support
-fPIC                  # Position independent code
-shared               # Dynamic library
```
