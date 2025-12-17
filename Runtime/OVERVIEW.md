# OneJS Runtime

Core C# runtime for QuickJS integration with Unity.

## Files

| File | Purpose |
|------|---------|
| `QuickJSContext.cs` | Managed wrapper for QuickJS context (Eval, ExecutePendingJobs, callbacks) |
| `QuickJSUIBridge.cs` | UI Toolkit integration, event delegation, scheduling (RAF, timers) |
| `JSRunner.cs` | MonoBehaviour entry point for running JS apps |
| `ScriptEngine.cs` | Placeholder/stub |

## QuickJSNative Partial Classes

| File | Purpose |
|------|---------|
| `QuickJSNative.cs` | DllImports, enums, structs, string helpers |
| `QuickJSNative.Handles.cs` | Handle table for C# object references |
| `QuickJSNative.Reflection.cs` | Type/method/property resolution and caching |
| `QuickJSNative.Structs.cs` | Struct serialization (Vector3, Color, etc.) |
| `QuickJSNative.Dispatch.cs` | JS→C# callback dispatch, value conversion |
| `QuickJSNative.FastPath.cs` | Zero-allocation fast path for hot properties/methods |

## Profiling (in `Profiling/` folder)

| File | Purpose |
|------|---------|
| `QuickJSProfilerMinimal.cs` | Minimal fast-path test |
| `QuickJSZeroAllocProfilerTest.cs` | Full zero-allocation verification |

## Key Patterns

### Event Delegation
Events captured at root via TrickleDown, dispatched to JS:
```csharp
_root.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
// → Eval("__dispatchEvent(handle, 'click', {...})")
```

### Scheduling
```javascript
requestAnimationFrame(cb)  // Called each Tick()
setTimeout(cb, ms)         // Timer queue
setImmediate(cb)           // Via queueMicrotask
```

### Fast Path
Pre-registered handlers for hot paths (Time.deltaTime, transform.position):
```csharp
FastPath.StaticProperty<Time, float>("deltaTime", () => Time.deltaTime);
FastPath.Property<Transform, Vector3>("position", t => t.position, (t,v) => t.position = v);
```
