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

### JS → C# Delegate Callbacks (events, delegate props)
When JS passes a function to C# (event subscription via `add_X`, delegate-typed
argument, or delegate property assignment):
1. Bootstrap's `__resolveValue` calls the page-global `__registerCallback(fn)`
   (installed by `OneJS.init()`), which stores the function in
   `OneJS.callbackRegistry` and returns an integer handle
2. The `{ __csCallbackHandle: N }` marker crosses the bridge as JSON;
   C# `ConvertToTargetType` wraps it in a cached delegate (`CreateDelegateWrapper`)
3. When C# raises the event, the delegate calls `qjs_invoke_callback`, which looks
   up the registry, unmarshals args, and invokes the JS function
4. `__unregisterCallback(id)` frees the entry (bootstrap's `add_`/`remove_` and
   delegate-reassignment bookkeeping call it automatically)

Contract notes:
- Argument memory is owned by the C# caller (`InvokeCallback`/`NoAlloc` allocate
  and free): the jslib must not free it
- `outResultPtr` is null for the zero-alloc invoke family
- Handler exceptions and stale handles log `console.error` but return success,
  so one broken JS handler can't abort remaining C# listeners on the same event

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

### Phase 1: Basic Bridge ✅
- [x] `qjs_create` / `qjs_destroy`
- [x] `qjs_eval`: Execute JS in browser
- [x] `qjs_run_gc`: No-op (browser GC)
- [x] `qjs_execute_pending_jobs`: No-op (browser event loop)
- [x] Callback registration functions
- [x] `[MonoPInvokeCallback]` attributes for IL2CPP

### Phase 2: Full Interop ✅
- [x] `__cs_invoke`: Full argument marshaling (JS→C#)
- [x] `marshalValue`: JS values to WASM heap structs
- [x] `unmarshalValue`: WASM heap structs to JS values
- [x] Support for all InteropType values (primitives, strings, handles, vectors)
- [x] Memory management (alloc/free for strings, args, results)
- [x] Delegate/callback marshaling (`__registerCallback` registry + `qjs_invoke_callback`): JS functions as C# event handlers, delegate args, and delegate props; also powers `onPlay`/`onStop`

### Phase 3: Production Ready ✅
- [x] `qjs_dispatch_event`: Fast event dispatch (avoids eval)
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
| Delegate Callbacks | Native callback table (4096 slots) | Page-global `Map` registry (unbounded) |

## Gotchas

1. **Bootstrap timing**: The bootstrap runs before platform defines are set. Check for `__nativeRequestAnimationFrame` instead of `UNITY_WEBGL`.

2. **PlayerLoop recursion**: Never call back into C# from Unity's Update loop in a way that triggers more JS execution. Use native RAF instead.

3. **Large scripts**: Don't return large scripts through C# eval buffer. Execute directly in JS via `eval()`.

4. **performance.now()**: Don't override browser's `performance` object. Unity WebGL uses it.

5. **Shared global scope**: `qjs_eval` uses indirect eval, so the bootstrap and app code run in the **embedding page's** global scope, there is no isolation from the host website. Two rules follow:
   - All bootstrap polyfills (`URL`, `URLSearchParams`, `localStorage`, `sessionStorage`, `btoa`, `atob`, `queueMicrotask`, `performance`, `fetch`, `WebSocket`) are install-if-missing: on WebGL the browser natives win, the polyfills only exist for QuickJS. Never assign a polyfill to `globalThis` unconditionally, it would clobber the native for every script on the host page (e.g. a non-iterable `URLSearchParams` breaks Next.js routing).
   - The whole bootstrap body lives inside an IIFE, because in sloppy-mode indirect eval **top-level `function`/`var` declarations also become own properties of the host page's `window`** (a top-level `function addEventListener(element, ...)` shadows `EventTarget.prototype.addEventListener` for the entire page). Keep every new declaration inside the IIFE; anything needed by the user bundle, C#, or the jslib must be exported explicitly via `globalThis.*`.

6. **Timer overrides capture the host page** (known limitation, mitigated by teardown): `setTimeout`/`setInterval`/`requestAnimationFrame` are intentionally replaced with tick-queue versions (React must run on OneJS's tick), but because the global scope is shared, everything registered after boot is rerouted, **including Unity's own main loop**: Emscripten's `MainLoop.requestAnimationFrame` resolves the bare global at call time, so every Unity frame runs from inside `__webglTick`. While the app runs this is invisible; the sharp edge is context destruction. `qjs_destroy` therefore calls the bootstrap's `__teardownTimers()`, which stops the RAF tick, migrates still-pending queue entries onto native timers, restores the native functions, and leaves thin `clear*`/`cancel*` wrappers that route old override ids (always >= `1 << 30`, so they never collide with the browser's small sequential ids) to the migrated native timers. Additionally, `invokeCs`, the zero-alloc invoke, and `releaseHandle` refuse calls while `OneJS.contextPtr === 0` (one-time console warning): page-scope JS outlives the C# context, and after `Application.Quit` / `unityInstance.Quit()` a surviving module-level interval would otherwise dynCall into a shut-down IL2CPP runtime. A real fix for the capture itself still requires isolating OneJS execution in its own realm (iframe/ShadowRealm) or scoping the overrides to OneJS code only.

7. **`marshalValue` ordering and duck-typing**: `marshalValue` re-implements the native C marshaler's JS→C# conversion, so any classification difference is a platform-specific bug. Two hard rules: (a) objects carrying `__type` (JSON-round-tripped C# structs, e.g. `Translate` returned by a data-only-struct ctor) must cross as `TYPE_JSON_OBJECT` **before** any vector duck-typing, their `x`/`y`/`z` members can be nested structs, and `HEAPF32` writes would mangle them into NaN; (b) the Vector3/Vector4/Color duck-checks require `typeof === "number"` members, never just `!== undefined`. C# rebuilds `__type` payloads via `DeserializeFromDict`.
