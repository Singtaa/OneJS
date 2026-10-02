# quickjs-unity

Native QuickJS engine with Unity C# interop wrapper.

## Structure

```
quickjs-unity/
├── quickjs/                 # Submodule: Bellard's QuickJS, used by every .sh build
├── quickjs-ng/              # Submodule: quickjs-ng, used only by the MSVC/CMake path
├── src/
│   └── quickjs_unity.c     # Unity wrapper: interop marshaling, callback table, exports
├── build.sh                # macOS build
├── build-linux.sh          # Linux x64 build
├── build-windows.sh        # Windows cross-compile (MinGW), the shipped DLL
├── build-windows-msvc.bat  # Windows native (MSVC, quickjs-ng)
├── build-windows.bat       # Windows native (CMake, quickjs-ng)
├── build-ios.sh            # iOS build (static .a)
├── build-android.sh        # Android build (shared .so)
├── check-plugin-deps.py    # Verifies the shipped binaries' dynamic dependencies
├── load-plugin-smoke.py    # Loads this platform's shipped binary and checks qjs_abi_version against the C#
├── bench-dispatch.py       # Benchmarks __dispatchEvent on the shipped native engine
└── CMakeLists.txt          # CMake build (Windows MSVC)
```

Run `git submodule update --init` in the OneJS repo before building: both engine folders are submodules.

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

The shipped .so is built inside an `ubuntu:22.04` container to pin the glibc
baseline; see `Plugins/README.md` for the exact command before refreshing the
committed binary.

### Windows (cross-compile from macOS/Linux)
```bash
./build-windows.sh     # Requires: mingw-w64
# Output: Plugins/Windows/x64/quickjs_unity.dll
```

This is the shipped Windows binary. It links with `-static` rather than
`-static-libgcc`, because `quickjs.c` uses pthread mutexes and condition
variables and MinGW's posix thread model satisfies those from libwinpthread. A
dynamic link leaves `libwinpthread-1.dll` in the DLL's import table, which no
Windows machine has and the package does not ship, so Unity reports the load
failure as `DllNotFoundException: quickjs_unity`. That shipped in v3.2.0 and
v3.2.1; `check-plugin-deps.py` now catches it.

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
iOS uses static linking (`__Internal` DllImport): the library is linked into the IL2CPP binary.

### Android
```bash
./build-android.sh
# Output: Plugins/Android/{arm64-v8a,armeabi-v7a,x86_64}/libquickjs_unity.so
# Requires: Android NDK (set NDK_ROOT, ANDROID_NDK_HOME, or ANDROID_NDK_ROOT)
```

## Verifying a rebuilt binary

```bash
python3 check-plugin-deps.py
```

Reads the dependency list straight out of each committed binary (PE imports,
ELF DT_NEEDED, Mach-O LC_LOAD_DYLIB) and fails on anything a clean end-user
machine would not already have. No toolchain and no third-party module, so it
checks every platform's binary from any host. CI runs it on each push; run it
yourself after refreshing any binary here, since a dependency picked up from
the build machine only surfaces at load time on someone else's machine.

## Key Native Functions

| Function | Purpose |
|----------|---------|
| `qjs_abi_version()` | ABI version the C# runtime checks at startup |
| `qjs_create()` / `qjs_destroy()` | Create or destroy a JS context |
| `qjs_eval()` | Evaluate JS code |
| `qjs_execute_pending_jobs()` | Process Promise queue |
| `qjs_run_gc()` | Run the QuickJS GC |
| `qjs_invoke_callback()` | Call a registered JS callback from C# |
| `qjs_take_last_error()` | Take the error a callback threw (message, then its frames); C# throws or logs it as a `JSException` |
| `qjs_set_cs_invoke_callback()` | Register the C# dispatch handler (also `_log_`, `_release_handle_`, `_zeroalloc_`, `_free_` variants) |
| `qjs_free()` | Free memory the native side allocated for C# |

## Handle System

The handle table lives in C# (`Runtime/QuickJSNative.Handles.cs`: `RegisterObject`, `GetObjectByHandle`). JS holds the integer handle; when the bootstrap's `FinalizationRegistry` collects a proxy it calls the native `__releaseHandle`, which forwards to the callback registered with `qjs_set_cs_release_handle_callback` so C# drops the entry.
