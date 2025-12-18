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

## Target Platform

- **Unity 6+** (Emscripten 3.1.38+)
- Uses `makeDynCall` (not deprecated `dynCall`)
- Uses `UTF8ToString` (not deprecated `Pointer_stringify`)

## Build Process

No special setup required:
- Plugin automatically included only in WebGL builds (via .meta settings)
- Editor/Play Mode continues using native QuickJS
- Just press Ctrl+B / Cmd+B to build

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

### Phase 3 - Production Ready
- [ ] Error handling and recovery
- [ ] Performance optimization
- [ ] Testing across browsers
- [ ] `qjs_invoke_callback` (C#→JS callbacks, if needed)
