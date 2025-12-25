# OneJS WebGL Plugin

Browser JavaScript bridge for WebGL builds.

## Architecture

In WebGL builds, JavaScript runs directly in the browser's JS engine (V8/SpiderMonkey) with JIT optimization, rather than in QuickJS compiled to WASM. This provides significant performance benefits.

```
┌─────────────────────────────────────────────────────────────┐
│                      Browser Environment                     │
├─────────────────────────────────────────────────────────────┤
│  Unity WASM Module                                           │
│      ↓                                                       │
│  C# Code (IL2CPP → WASM)                                    │
│      ↓ [DllImport("__Internal")]                            │
│  OneJSWebGL.jslib (mergeInto LibraryManager.library)        │
│      ↓                                                       │
│  Browser JavaScript (with JIT!)                              │
│      ↓                                                       │
│  QuickJSBootstrap.js (runs in browser context)              │
└─────────────────────────────────────────────────────────────┘
```

## Files

| File | Purpose |
|------|---------|
| `OneJSWebGL.jslib` | Emscripten library implementing qjs_* functions |

## How It Works

### C# → JavaScript (eval)
1. C# calls `qjs_eval()` via `[DllImport("__Internal")]`
2. Emscripten routes to `OneJSWebGL.jslib`
3. jslib uses browser's `eval()` to execute code
4. Result marshaled back via shared WASM heap

### JavaScript → C# (invoke)
1. JS calls `__cs_invoke()` (set up by bootstrap)
2. jslib marshals arguments to WASM heap structs
3. `makeDynCall` invokes C# callback delegate
4. C# processes request via reflection (same as native QuickJS path)
5. Result marshaled back to JS

### Event Dispatch (optimized)
1. C# calls `qjs_dispatch_event()` directly (not eval)
2. jslib calls `__dispatchEvent()` with parsed JSON
3. Avoids eval overhead for high-frequency events

### Tick Loop (RAF)
In WebGL, the tick loop uses browser's native `requestAnimationFrame` instead of Unity's Update:
1. `__startWebGLTick()` called after script loads
2. Browser RAF drives `__webglTick()` at 60fps
3. Processes RAF callbacks, timeouts, intervals
4. Avoids PlayerLoop recursion (C# Update → JS → C# interop)

## Target Platform

- **Unity 6+** (Emscripten 3.1.38+)
- Uses `makeDynCall` (not deprecated `dynCall`)
- Uses `UTF8ToString` (not deprecated `Pointer_stringify`)

## Build Process

No special setup required:
- Plugin automatically included only in WebGL builds (via .meta settings)
- Editor/Play Mode continues using native QuickJS
- Just press Ctrl+B / Cmd+B to build

### StreamingAssets Loading
For WebGL, the app bundle is loaded from StreamingAssets using browser's native `fetch()`:
1. JSRunner defers loading to Update (browser needs to be ready)
2. Uses native `fetch()` instead of `UnityWebRequest` (more reliable in WebGL)
3. Script executed directly in JS via `eval()` to avoid buffer size limits
4. `__startWebGLTick()` called after successful execution

## Implementation Status

### Phase 1 - Basic Bridge ✅
- [x] `qjs_create` / `qjs_destroy`
- [x] `qjs_eval` - Execute JS in browser
- [x] `qjs_run_gc` - No-op (browser GC)
- [x] `qjs_execute_pending_jobs` - No-op (browser event loop)
- [x] Callback registration functions
- [x] `[MonoPInvokeCallback]` attributes for IL2CPP

### Phase 2 - Full Interop ✅
- [x] `__cs_invoke` - Full argument marshaling (JS→C#)
- [x] `marshalValue` - JS values to WASM heap structs
- [x] `unmarshalValue` - WASM heap structs to JS values
- [x] Support for all InteropType values (primitives, strings, handles, vectors)
- [x] Memory management (alloc/free for strings, args, results)

### Phase 3 - Production Ready ✅
- [x] `qjs_dispatch_event` - Fast event dispatch (avoids eval)
- [x] Native RAF tick loop (avoids PlayerLoop recursion)
- [x] Platform defines injection (UNITY_WEBGL, etc.)
- [x] StreamingAssets loading via native fetch

## Key Differences from Native QuickJS

| Aspect | Native QuickJS | WebGL |
|--------|---------------|-------|
| JS Engine | QuickJS (interpreter) | Browser V8/SpiderMonkey (JIT) |
| Tick Loop | Unity Update → `__tick()` | Browser RAF → `__webglTick()` |
| Microtasks | `ExecutePendingJobs()` | Browser handles natively |
| GC | QuickJS GC | Browser GC |
| Event Dispatch | `qjs_eval()` | `qjs_dispatch_event()` (fast path) |

## Gotchas

1. **Bootstrap timing**: The bootstrap runs before platform defines are set. Check for `__nativeRequestAnimationFrame` instead of `UNITY_WEBGL`.

2. **PlayerLoop recursion**: Never call back into C# from Unity's Update loop in a way that triggers more JS execution. Use native RAF instead.

3. **Large scripts**: Don't return large scripts through C# eval buffer. Execute directly in JS via `eval()`.

4. **performance.now()**: Don't override browser's `performance` object - Unity WebGL uses it.
