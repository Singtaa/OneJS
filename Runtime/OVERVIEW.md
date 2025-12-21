# OneJS Runtime

Core C# runtime for QuickJS integration with Unity.

## Platform Support

| Platform | JS Engine | Notes |
|----------|-----------|-------|
| Editor/Standalone | Native QuickJS | `quickjs_unity.dylib/.dll/.so` |
| iOS | Native QuickJS | Statically linked (`__Internal`) |
| WebGL | Browser JS | Via `OneJSWebGL.jslib` - runs with JIT! |

For WebGL details, see `../Plugins/WebGL/OVERVIEW.md`.

## Files

| File | Purpose |
|------|---------|
| `QuickJSContext.cs` | Managed wrapper for QuickJS context (Eval, ExecutePendingJobs, callbacks) |
| `QuickJSUIBridge.cs` | UI Toolkit integration, event delegation, scheduling (RAF, timers) |
| `JSRunner.cs` | MonoBehaviour entry point with auto-scaffolding and live reload |
| `ScriptEngine.cs` | Placeholder/stub |

## JSRunner Features

The `JSRunner` MonoBehaviour is the primary way to run JavaScript apps in Unity.

### Auto-Scaffolding
On first run, if the working directory is empty, JSRunner creates a complete project:
- `package.json` - npm configuration with React and onejs-react dependencies
- `esbuild.config.mjs` - Build configuration for bundling
- `tsconfig.json` - TypeScript configuration
- `global.d.ts` - TypeScript declarations for OneJS globals
- `index.tsx` - Sample React application
- `styles/main.uss` - Sample USS stylesheet
- `@outputs/esbuild/app.js` - Default entry file

### Live Reload
- Polls the entry file for changes (Mono-compatible, no FileSystemWatcher)
- Configurable poll interval (default: 0.5s)
- Hard reload: disposes context, clears UI, recreates fresh
- Only available in Editor/Standalone (not WebGL)

### Custom Inspector
The `JSRunnerEditor` provides:
- Status display (running/stopped, reload count)
- **Reload Now** button - Force reload in Play mode
- **Build** button - Run `npm run build`
- **Open Folder** - Open working directory in file explorer
- **Open Terminal** - Open terminal at working directory

### Public API
```csharp
// Properties
bool IsRunning { get; }
bool IsLiveReloadEnabled { get; }
int ReloadCount { get; }
DateTime LastModifiedTime { get; }
string WorkingDirFullPath { get; }
string EntryFileFullPath { get; }

// Methods
void ForceReload();  // Manually trigger reload
```

## QuickJSNative Partial Classes

| File | Purpose |
|------|---------|
| `QuickJSNative.cs` | DllImports, enums, structs, string helpers (platform-conditional) |
| `QuickJSNative.Handles.cs` | Handle table for C# object references |
| `QuickJSNative.Reflection.cs` | Type/method/property resolution and caching |
| `QuickJSNative.Structs.cs` | Struct serialization (Vector3, Color, etc.) |
| `QuickJSNative.Dispatch.cs` | JS→C# callback dispatch, value conversion |
| `QuickJSNative.FastPath.cs` | Zero-allocation fast path for hot properties/methods |
| `QuickJSNative.Tasks.cs` | C# Task/Promise bridging for async/await support |

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

## JS Interop Features

The JS runtime (via `QuickJSBootstrap.js.txt`) provides rich interop with C#.

### CS Proxy
Access any C# type via the `CS` global:
```javascript
CS.UnityEngine.Debug.Log("Hello")
var go = new CS.UnityEngine.GameObject("MyObject")
```

### Generic Types
Create generic types by calling the type as a function with type arguments:
```javascript
var IntList = CS.System.Collections.Generic.List(CS.System.Int32)
var list = new IntList()
list.Add(42)

var Dict = CS.System.Collections.Generic.Dictionary(CS.System.String, CS.System.Int32)
var dict = new Dict()
dict.Add("key", 100)
```

### Indexer Support
Access indexed collections using bracket notation:
```javascript
var list = new (CS.System.Collections.Generic.List(CS.System.Int32))()
list.Add(10)
list.Add(20)

// Get by index
var first = list[0]   // Returns 10

// Set by index
list[0] = 100         // Sets first element to 100

// Loop with indexer
for (var i = 0; i < list.Count; i++) {
    console.log(list[i])
}
```

**Supported types**: Any C# type with `get_Item(int)` / `set_Item(int, T)` methods:
- `List<T>`
- `T[]` (arrays)
- Custom indexed collections

**Note**: String indexers (e.g., `dict["key"]`) require explicit method calls:
```javascript
dict.get_Item("key")      // Get
dict.set_Item("key", 42)  // Set
```

### Property/Method Heuristics
The proxy uses naming conventions to determine access type:
- **Uppercase first letter** → Method call (e.g., `obj.GetComponent(...)`)
- **Lowercase first letter** → Property access (e.g., `obj.name`)
- **Known properties** → Explicit property access (`Count`, `Length`, etc.)
- **Numeric string** → Indexer access (`obj[0]`)
- **`get_`/`set_` prefix** → Method call (e.g., `obj.get_Item(0)`)

### Async/Await Support
C# `Task` and `Task<T>` are automatically converted to JS Promises:
```javascript
// Using .then()
CS.MyClass.GetDataAsync().then(function(result) {
    console.log("Got:", result)
}).catch(function(error) {
    console.error("Failed:", error.message)
})

// Using async/await
async function loadData() {
    try {
        var result = await CS.MyClass.GetDataAsync()
        console.log("Got:", result)
    } catch (error) {
        console.error("Failed:", error.message)
    }
}
```

**How it works**:
1. When a C# async method returns `Task`/`Task<T>`, it's registered with a unique ID
2. JS receives a Promise keyed to that ID
3. When the Task completes, `QuickJSUIBridge.Tick()` resolves/rejects the Promise
4. The Promise result is wrapped as a C# object proxy if needed

**Supported return types**:
- `Task` → Promise resolving to `null`
- `Task<T>` → Promise resolving to the result value
- Primitives (`int`, `string`, `bool`, etc.) are converted directly
- Reference types (`GameObject`, custom classes) become C# object proxies

**Error handling**:
- Faulted tasks reject the Promise with the exception message
- Canceled tasks reject with "Task was canceled"
