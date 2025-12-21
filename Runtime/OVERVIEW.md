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

### Platform Behavior

| Context | JS Loading | Live Reload |
|---------|------------|-------------|
| Editor | From disk (`App/@outputs/esbuild/app.js`) | Yes (polling) |
| Standalone | StreamingAssets (auto-copied during build) | No |
| WebGL | StreamingAssets via fetch API | No |
| Android | StreamingAssets via UnityWebRequest | No |
| iOS | StreamingAssets | No |

### Auto-Scaffolding (Editor Only)
On first run, if the working directory is empty, JSRunner creates a complete project:
- `package.json` - npm configuration with React and onejs-react dependencies
- `esbuild.config.mjs` - Build configuration for bundling
- `tsconfig.json` - TypeScript configuration
- `global.d.ts` - TypeScript declarations for OneJS globals
- `index.tsx` - Sample React application
- `styles/main.uss` - Sample USS stylesheet
- `@outputs/esbuild/app.js` - Default entry file

### Live Reload (Editor Only)
- Polls the entry file for changes (Mono-compatible, no FileSystemWatcher)
- Configurable poll interval (default: 0.5s)
- Hard reload: disposes context, clears UI, recreates fresh

### Build Support
For standalone/mobile builds, JSRunner loads from StreamingAssets:
- **Auto-copy**: `JSRunnerBuildProcessor` copies entry file to StreamingAssets during build
- **Default path**: `StreamingAssets/onejs/app.js`
- **Override**: Assign a TextAsset to `Embedded Script` to skip StreamingAssets

### Custom Inspector
The `JSRunnerEditor` provides:
- Status display (running/stopped, reload count)
- **Reload Now** button - Force reload in Play mode
- **Build** button - Run `npm run build`
- **Open Folder** - Open working directory in file explorer
- **Open Terminal** - Open terminal at working directory
- Build info showing where bundle will be copied

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
void ForceReload();  // Manually trigger reload (Editor only)
```

## QuickJSNative Partial Classes

| File | Purpose |
|------|---------|
| `QuickJSNative.cs` | DllImports, enums, structs, string helpers (platform-conditional) |
| `QuickJSNative.Handles.cs` | Handle table for C# object references with monitoring |
| `QuickJSNative.Reflection.cs` | Type/method/property resolution and caching |
| `QuickJSNative.Structs.cs` | Struct serialization (Vector3, Color, etc.) |
| `QuickJSNative.Dispatch.cs` | JS→C# callback dispatch, value conversion, exception handling |
| `QuickJSNative.FastPath.cs` | Zero-allocation fast path for hot properties/methods |
| `QuickJSNative.Tasks.cs` | C# Task/Promise bridging with queue monitoring |

## Stability & Monitoring

The runtime includes built-in monitoring to detect potential issues early.

### Buffer Overflow Detection (QuickJSContext)
Eval output is limited by buffer size (default 16KB). If output fills the buffer, a warning is logged:
```
[QuickJSContext] Eval output may have been truncated at 16384 bytes.
```
**Solution**: Increase buffer size in constructor or avoid returning large values from eval.

### Handle Table Monitoring (QuickJSNative)
C# objects referenced from JS use integer handles. The handle table is monitored:

| Threshold | Level | Action |
|-----------|-------|--------|
| 10,000 | Warning | Logs potential memory leak warning |
| 100,000 | Critical | Logs critical error |

```csharp
// Monitoring API
int count = QuickJSNative.GetHandleCount();       // Current handle count
int peak = QuickJSNative.GetPeakHandleCount();    // Peak since last reset
QuickJSNative.ResetHandleMonitoring();            // Reset peak and warnings
QuickJSNative.ClearAllHandles();                  // Clear all (on context dispose)
```

### Task Queue Monitoring (QuickJSNative)
Async C# methods create pending task completions. The queue is monitored:

| Threshold | Action |
|-----------|--------|
| 100 pending | Logs warning about queue growth |
| 50 per tick | Max tasks processed per frame to prevent blocking |

```csharp
// Monitoring API
int pending = QuickJSNative.GetPendingTaskCount();    // Current queue size
int peak = QuickJSNative.GetPeakTaskQueueSize();      // Peak since last reset
QuickJSNative.ResetTaskQueueMonitoring();             // Reset peak and warnings
QuickJSNative.ClearPendingTasks();                    // Clear queue (on dispose)
```

### Recursion Guard (QuickJSUIBridge)
All JS execution is protected by a recursion guard (`_inEval` flag) to prevent:
- Event handlers triggering during Tick()
- Nested eval calls causing stack overflow
- WebGL-specific recursion issues

Events dispatched during active JS execution are silently dropped.

### WebGL Fetch State Machine (JSRunner)
WebGL script loading uses an explicit state machine to prevent race conditions:
```
NotStarted → Fetching → Ready → Executed
                    ↘ Error
```
Script execution only occurs after the `Ready` state confirms fetch completion.

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
