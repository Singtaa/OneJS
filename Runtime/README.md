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
| `JSPad.cs` | Inline TSX runner with no external working directory |
| `Janitor.cs` | Marker component for live reload cleanup of JS-created GameObjects |
| `Network.cs` | Fetch API implementation using UnityWebRequest |
| `SourceMapParser.cs` | Parses source maps for error stack trace translation |
| `UICartridge.cs` | Cartridge system for packaged UI modules |
| `CartridgeTypeGenerator.cs` | Generates TypeScript declarations for cartridge types |
| `GPU/GPUBridge.cs` | Compute shader API for JavaScript |
| `GPU/ComputeShaderProvider.cs` | MonoBehaviour for registering shaders via inspector |
| `Input/InputBridge.cs` | Input System bridge for keyboard, mouse, gamepad, touch |
| `Styling/UssCompiler.cs` | Runtime USS compilation from strings |
| `Styling/StyleSheetBuilderWrapper.cs` | Reflection wrapper for Unity's internal StyleSheetBuilder |
| `Controls/CodeField.cs` | TextField with syntax highlighting via per-glyph vertex coloring |

## Controls (`Controls/` folder)

Custom UI Toolkit controls for OneJS applications. See `Controls/README.md` for details.

| Control | Description |
|---------|-------------|
| `CodeField` | TextField with syntax highlighting. Uses `PostProcessTextVertices` to colorize glyphs without affecting cursor positioning. Includes built-in JS/TS highlighter. |

## JSRunner Features

The `JSRunner` MonoBehaviour is the primary way to run JavaScript apps in Unity.

### Scene-Based Path System

JSRunner uses a **zero-config, scene-based path system**. All paths are automatically derived from the scene location:

```
Assets/Scenes/Level1.unity          # Your scene
Assets/Scenes/Level1/               # Auto-created folder next to scene
├── MainUI_abc123/                  # Per-JSRunner folder (name + instanceId)
│   ├── MainUI~/                    # Working directory (~ makes Unity ignore it)
│   │   ├── package.json            # Scaffolded on first run
│   │   ├── index.tsx               # Source file
│   │   └── esbuild.config.mjs      # Outputs to ../app.js.txt
│   ├── PanelSettings.asset         # Auto-created UI panel settings
│   ├── app.js.txt                  # Built bundle (esbuild output + TextAsset)
│   └── app.js.txt.map              # Source map (optional)
```

**Key benefits:**
- No manual path configuration needed
- Each JSRunner gets its own isolated workspace
- Instance ID suffix prevents naming collisions
- `~` suffix makes Unity ignore the working directory (no .meta files)
- TextAssets are created automatically during build
- **Cleanup on removal**: When JSRunner is removed in Edit mode, prompts to delete instance folder

### Platform Behavior

| Context | JS Loading | Live Reload |
|---------|------------|-------------|
| Editor | From disk (working directory) | Yes (polling) |
| Standalone/Mobile | TextAsset (auto-created during build) | No |
| WebGL | TextAsset embedded in build | No |

### Zero-Config UI Setup
JSRunner automatically creates `UIDocument` at runtime and a `PanelSettings.asset` in the instance folder on first Play mode. No manual asset creation required.

**Setup options:**
1. **Zero-config**: Just add JSRunner to any GameObject and hit Play - PanelSettings asset is auto-created
2. **Custom PanelSettings**: Drag any PanelSettings asset to the Panel Settings field to override the default

The auto-created PanelSettings asset is stored at `{InstanceFolder}/PanelSettings.asset` and can be modified directly in the inspector. Changes persist across sessions.

### Auto-Scaffolding (Editor Only)
On first run, JSRunner creates missing files from its **Default Files** list. This is non-destructive - existing files are never overwritten.

Configure scaffolding in the inspector:
- **Default Files**: List of `path → TextAsset` pairs. Each path is relative to Working Dir.
- Files are created only if missing, preserving user modifications.

Default template files (in `Assets/Singtaa/OneJS/Editor/Templates/`):
- `package.json.txt` - npm configuration with React and onejs-react dependencies
- `esbuild.config.mjs.txt` - Build configuration with CSS Modules and Tailwind support
- `tsconfig.json.txt` - TypeScript configuration
- `global.d.ts.txt` - TypeScript declarations for OneJS globals
- `index.tsx.txt` - Sample React application
- `main.uss.txt` - Sample USS stylesheet
- `gitignore.txt` - Git ignore for node_modules

### Inspector Fields

| Field | Purpose |
|-------|---------|
| **Project** (auto-computed) | |
| Scene Folder | `{SceneName}/` next to scene file |
| Working Dir | `{SceneName}/{Name}_{InstanceId}/{Name}~/` |
| Bundle File | `{InstanceFolder}/app.js.txt` (esbuild output) |
| **Build** | |
| Bundle Asset | TextAsset for built JS (auto-created during build) |
| Source Map Asset | TextAsset for source map (optional) |
| Include Source Map | Whether to include source maps in builds |
| **UI Panel** | |
| Panel Settings | PanelSettings asset (auto-created in instance folder on first Play, or assign custom) |
| Default Theme | ThemeStyleSheet applied to PanelSettings |
| *(embedded inspector)* | Full PanelSettings inspector shown inline for easy configuration |
| **Scaffolding** | |
| Default Files | `path → TextAsset` pairs for auto-scaffolding on first run |
| **Advanced** | |
| Stylesheets | List of USS StyleSheets applied on init/reload |
| Preloads | List of TextAssets eval'd before entry file |
| Globals | `key → Object` pairs injected as `globalThis[key]` |

### Live Reload (Editor Only)
- Polls the entry file for changes (Mono-compatible, no FileSystemWatcher)
- Configurable poll interval (default: 0.5s)
- Hard reload: disposes context, clears UI, recreates fresh
- **Janitor cleanup**: When enabled, destroys JS-created GameObjects on reload (see below)

### Janitor (Live Reload Cleanup)

The Janitor feature automatically cleans up GameObjects created by JavaScript during live reload.

**How it works:**
1. When Play mode starts (and Janitor is enabled), a `Janitor` GameObject is spawned
2. The Janitor serves as a marker in the scene hierarchy
3. On each live reload, all root GameObjects **after** the Janitor are destroyed
4. This allows JS code to instantiate GameObjects that get cleaned up automatically

**Configuration:**
- Enable via `Enable Janitor` toggle in Project tab (under Live Reload settings)
- Enabled by default

**Use case:**
```javascript
// JS code creates GameObjects at runtime
const cube = new CS.UnityEngine.GameObject("MyCube")
// On live reload, this cube is automatically destroyed
```

### Build Support
For standalone/mobile builds, JSRunner loads from a TextAsset:
- **Same file**: esbuild outputs directly to `app.js.txt` which is also the TextAsset
- **Bundle path**: `{SceneName}/{Name}_{InstanceId}/app.js.txt`
- **Source maps**: Optional `app.js.txt.map` for error stack translation
- **Pre-assigned**: If a bundle TextAsset is already assigned, build processor skips it

### Custom Inspector
The `JSRunnerEditor` provides:
- Status display (running/stopped, reload count, working directory path)
- **Refresh** button - Force reload in Play mode
- **Rebuild** button - Delete node_modules, reinstall, and rebuild
- **Open Folder** - Open working directory in file explorer
- **Open Terminal** - Open terminal at working directory
- **Open VSCode** - Open working directory in VS Code
- Watcher status (auto-starts on Play mode)

### Public API
```csharp
// Properties
bool IsRunning { get; }
bool IsLiveReloadEnabled { get; }
int ReloadCount { get; }
DateTime LastModifiedTime { get; }

// Path properties (Editor only, auto-computed from scene)
string SceneFolder { get; }           // {SceneName}/
string InstanceFolder { get; }        // {SceneFolder}/{Name}_{InstanceId}/
string WorkingDirFullPath { get; }    // {InstanceFolder}/{Name}~/
string EntryFileFullPath { get; }     // {InstanceFolder}/app.js.txt
string BundleAssetPath { get; }       // Same as EntryFileFullPath
string SourceMapAssetPath { get; }    // {InstanceFolder}/app.js.txt.map
string PanelSettingsAssetPath { get; } // {InstanceFolder}/PanelSettings.asset

// Methods
void ForceReload();  // Manually trigger reload (Editor only)
```

## JSRunner vs JSPad: Choosing the Right Tool

OneJS provides two MonoBehaviours for running JavaScript: **JSRunner** (production framework) and **JSPad** (rapid prototyping tool). They share the same underlying runtime (`QuickJSUIBridge`, `QuickJSContext`) but offer different developer experiences optimized for their respective use cases.

### Goals & Use Cases

| | JSRunner | JSPad |
|--|----------|-------|
| **Primary Goal** | Production-ready application framework | Quick experimentation & prototyping |
| **Developer Experience** | Full IDE workflow with external files | Zero-config with inline code editor |
| **Team Collaboration** | Version-controllable files | Self-contained in scene |
| **Iteration Speed** | Live reload on file save | Manual Build & Run |
| **Build Complexity** | Developer manages npm/build | Editor handles everything |

**Use JSRunner when:**
- Building complete React/JavaScript applications
- Working in a team (files are version-controllable)
- Needing live reload for rapid iteration
- Deploying to production (desktop, mobile, web, console)
- Managing multiple JS components across scenes
- Long-term maintenance is expected

**Use JSPad when:**
- Testing OneJS features quickly
- Prototyping UI ideas before committing to full project
- Learning OneJS by tinkering
- Creating simple single-component UIs
- Quick bug reproduction
- Teaching or demos
- Shipping prototype builds without npm in build pipeline

### Technical Comparison

| Aspect | JSRunner | JSPad |
|--------|----------|-------|
| Source | External files | Inline TextArea |
| Working Dir | User-facing `{Scene}/{Name}~/` | Hidden in `Temp/OneJSPad/` |
| Live Reload | Yes (automatic file polling) | No (manual Build & Run) |
| Build | Developer runs npm | Editor runs esbuild |
| Scaffolding | Full project (package.json, tsconfig, etc.) | Minimal essentials |
| npm Management | Developer responsibility | Automated by editor |
| Build Output | `app.js.txt` (same folder as source) | `@outputs/app.js` in temp |
| Standalone Build | Pre-built TextAsset | Serialized to component |

## JSPad (Inline TSX Runner)

`JSPad` is a simpler alternative to `JSRunner` for quick experimentation. Write TSX directly in the inspector with no external working directory.

### Usage
1. Add `JSPad` component to a GameObject with `UIDocument`
2. Write TSX code in the Source Code text area
3. Enter Play Mode
4. Click **Build & Run**

First build installs dependencies (~10s), subsequent builds are fast.

### Custom Inspector
- **Build & Run** - Build and execute immediately
- **Build Only** - Build without running
- **Run** - Execute previously built output
- **Stop** - Stop execution and clear UI
- **Open Temp Folder** - Reveal build directory
- **Clean** - Delete temp directory and node_modules

### Standalone Build Support

JSPad works in standalone builds without requiring npm/node at runtime:

1. **Automatic Bundle Serialization**: When entering Play mode, the built JS bundle and source map are automatically saved to serialized fields on the JSPad component
2. **Scene Auto-Save**: The scene is automatically saved to persist the bundle for builds
3. **Runtime Loading**: In standalone, JSPad loads from the serialized bundle instead of the temp file

**How it works**:
- `[InitializeOnLoad]` static handler builds all JSPad instances before entering Play mode
- Bundle is stored in `_builtBundle` serialized field (hidden in inspector)
- Source map stored in `_builtSourceMap` for error translation
- Scene is saved immediately after bundle serialization

**Error Messages**: Stack traces in standalone builds are translated using the embedded source map, showing original TypeScript line numbers instead of bundled JS locations.

### Temp Directory Structure
```
Temp/OneJSPad/{instanceId}/
├── package.json        # Auto-generated
├── tsconfig.json       # Auto-generated
├── esbuild.config.mjs  # Auto-generated
├── global.d.ts         # TypeScript declarations
├── index.tsx           # Written from Source Code
├── node_modules/       # npm install (cached)
└── @outputs/app.js     # Build output (JSPad uses temp folder)
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
| `QuickJSNative.ZeroAlloc.cs` | Zero-allocation interop for GPU and hot-path operations |

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

### Event Handler Cleanup (QuickJSBootstrap.js)
When C# objects are released (either manually via `releaseObject()` or automatically via garbage collection), event handlers registered for that element are automatically cleaned up:

```javascript
// Event handlers are stored per-handle
__eventHandlers.get(handle)  // Map of eventType -> Set<callback>

// Automatic cleanup when handle is released
__cleanupHandle(handle);     // Called before releasing to C#
```

This prevents memory leaks where stale event handlers could accumulate for destroyed UI elements.

### Double-Free Prevention (QuickJSBootstrap.js)
The `FinalizationRegistry` callback can race with manual `releaseObject()` calls. A tracking Set prevents double-free:

```javascript
// Track manually released handles
const __releasedHandles = new Set();

// Safe manual release
function releaseObject(obj) {
    if (!__releasedHandles.has(handle)) {
        __releasedHandles.add(handle);
        __cleanupHandle(handle);
        __releaseHandle(handle);
    }
}

// FinalizationRegistry skips already-released handles
const __handleRegistry = new FinalizationRegistry((handle) => {
    if (__releasedHandles.has(handle)) {
        __releasedHandles.delete(handle);  // Clean up tracking
        return;
    }
    // ... release handle
});
```

### Recursion Guard (QuickJSUIBridge)
All JS execution is protected by a recursion guard (`_inEval` flag) to prevent:
- Event handlers triggering during Tick()
- Nested eval calls causing stack overflow
- WebGL-specific recursion issues

Events dispatched during active JS execution are silently dropped.

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

## Zero-Allocation Interop (QuickJSNative.ZeroAlloc.cs)

For truly zero-allocation per-frame operations (e.g., GPU compute shader calls), the runtime provides specialized binding methods that bypass C# generics entirely.

### Why Specialized Bindings?

The generic `Bind<T>()` API causes boxing due to how C# generics work with value types:

```csharp
// Generic GetArg<T> boxes:
static T GetArg<T>(InteropValue* v) {
    return (T)(object)GetInt(v);  // ← Boxes int to object, then unboxes to T
}

// Generic SetResult<T> boxes:
switch (value) {  // ← Pattern matching boxes value type to object
    case int i: ...
}
```

The specialized `BindGpu*` methods use direct primitive types - no generics, no boxing:

```csharp
// Truly zero-alloc:
QuickJSNative.BindGpuSetFloatById((h, id, v) => GPUBridge.SetFloatById(h, id, v));
// Internally uses: int handle = GetInt(&args[0]); // No boxing!
```

### Available Specialized Bindings

| Method | Signature | Purpose |
|--------|-----------|---------|
| `BindGpuSetFloatById` | `(int, int, float) → void` | Set shader float by property ID |
| `BindGpuSetIntById` | `(int, int, int) → void` | Set shader int by property ID |
| `BindGpuSetVectorById` | `(int, int, float, float, float, float) → void` | Set shader vector by property ID |
| `BindGpuSetTextureById` | `(int, int, int, int) → void` | Set shader texture by property ID |
| `BindGpuDispatch` | `(int, int, int, int, int) → void` | Dispatch compute shader |
| `BindGpuGetScreenWidth` | `() → int` | Get screen width |
| `BindGpuGetScreenHeight` | `() → int` | Get screen height |

### Property ID Caching

To avoid string allocations, use Unity's `Shader.PropertyToID()` pattern:

```typescript
// JavaScript side - cache property IDs once at init
const _propertyIdCache = new Map<string, number>()

function getPropertyId(name: string): number {
    let id = _propertyIdCache.get(name)
    if (id === undefined) {
        id = CS.OneJS.GPU.GPUBridge.PropertyToID(name)  // One-time allocation
        _propertyIdCache.set(name, id)
    }
    return id
}

// Per-frame calls use cached ID - zero allocations
const timeId = getPropertyId("_Time")
setFloatById(shaderHandle, timeId, performance.now() / 1000)
```

### Two API Tiers

| API | Allocation | Use Case |
|-----|------------|----------|
| Generic `Bind<T>()` | ~80B per call (boxing) | Prototyping, non-hot-path |
| Specialized `BindGpu*()` | 0B | Per-frame GPU operations |

The generic API remains for convenience. Use specialized bindings for hot paths.

### Adding New Specialized Bindings

To add a new zero-alloc binding:

```csharp
// 1. Add to QuickJSNative.ZeroAlloc.cs
public static unsafe int BindMyMethod(Action<int, float> action) {
    return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
        int arg0 = GetInt(&args[0]);      // Direct int, no boxing
        float arg1 = GetFloat(&args[1]);  // Direct float, no boxing
        action(arg0, arg1);
    });
}

// 2. Register in your initialization code
int bindingId = QuickJSNative.BindMyMethod((a, b) => MyClass.MyMethod(a, b));

// 3. Use from JavaScript via __zaInvoke2
__zaInvoke2(bindingId, intValue, floatValue);
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

### Fetch API
The runtime provides a web-compatible `fetch()` API for making HTTP requests:
```javascript
// Simple GET request
const response = await fetch("https://api.example.com/data");
const data = await response.json();

// POST with JSON body
const response = await fetch("https://api.example.com/data", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name: "test" })
});

// Check response status
if (response.ok) {
    const text = await response.text();
}
```

**Supported features**:
- `fetch(url, options)` - Returns Promise<Response>
- Options: `method`, `headers`, `body`
- Response properties: `ok`, `status`, `statusText`, `url`, `headers`
- Response methods: `text()`, `json()`, `clone()`
- Headers class: `get()`, `set()`, `has()`, `append()`, `delete()`, `keys()`, `values()`, `entries()`, `forEach()`

**Implementation details**:
- Uses `UnityWebRequest` under the hood (works on all platforms)
- Supports GET, POST, PUT, DELETE, HEAD methods
- Auto-stringifies object bodies and sets Content-Type header
- Response body is fetched as text; use `json()` to parse

### Storage API (localStorage/sessionStorage)
Web-compatible storage API using Unity's PlayerPrefs:
```javascript
// Store and retrieve values
localStorage.setItem("theme", "dark");
const theme = localStorage.getItem("theme"); // "dark"

// Store objects as JSON
localStorage.setItem("user", JSON.stringify({ name: "Alice", level: 5 }));
const user = JSON.parse(localStorage.getItem("user"));

// Remove items
localStorage.removeItem("theme");
localStorage.clear(); // WARNING: Clears ALL PlayerPrefs
```

**Supported methods**:
- `getItem(key)` - Returns value or null
- `setItem(key, value)` - Stores value (converted to string)
- `removeItem(key)` - Removes item
- `clear()` - Clears all PlayerPrefs (use with caution)

**Limitations** (due to PlayerPrefs):
- `key(index)` - Always returns null (enumeration not supported)
- `length` - Always returns 0 (counting not supported)

**sessionStorage**: Alias to localStorage. Unlike web browsers, data persists across app restarts since Unity has no session concept.

**Implementation details**:
- Uses `PlayerPrefs` under the hood (cross-platform)
- Synchronous API (matches web localStorage)
- Values are automatically converted to strings
- `Save()` is called after each write for reliability

### URL API (URL/URLSearchParams)
Web-compatible URL parsing and query string manipulation:
```javascript
// Parse URLs
const url = new URL("https://example.com:8080/path?query=1#hash");
url.hostname;  // "example.com"
url.port;      // "8080"
url.pathname;  // "/path"
url.search;    // "?query=1"
url.hash;      // "#hash"
url.origin;    // "https://example.com:8080"

// Resolve relative URLs
const abs = new URL("/api/data", "https://example.com");
abs.href;  // "https://example.com/api/data"

// Work with query parameters
url.searchParams.get("query");     // "1"
url.searchParams.set("query", "2");
url.searchParams.append("new", "value");

// Build query strings
const params = new URLSearchParams({ foo: "1", bar: "2" });
params.toString();  // "foo=1&bar=2"
```

**URL properties**: `href`, `protocol`, `hostname`, `port`, `host`, `pathname`, `search`, `searchParams`, `hash`, `origin`, `username`, `password`

**URLSearchParams methods**: `get()`, `getAll()`, `set()`, `append()`, `delete()`, `has()`, `toString()`, `keys()`, `values()`, `entries()`, `forEach()`, `sort()`, `size`

**Implementation details**:
- Pure JavaScript implementation (~250 lines)
- WHATWG URL Standard compliant (common cases)
- Supports relative URL resolution with base URL
- Automatic encoding/decoding of special characters

### Base64 Encoding (atob/btoa)
Web-compatible Base64 encoding and decoding:
```javascript
// Encode string to Base64
btoa("Hello, World!")  // "SGVsbG8sIFdvcmxkIQ=="

// Decode Base64 to string
atob("SGVsbG8sIFdvcmxkIQ==")  // "Hello, World!"

// Common use cases
const encoded = btoa(JSON.stringify({ user: "alice", id: 123 }));
const decoded = JSON.parse(atob(encoded));

// JWT-like payload handling
const payload = atob(token.split('.')[1]);
```

**Limitations**:
- Only supports Latin1 characters (0-255). For Unicode, encode to UTF-8 first.
- `btoa()` throws if string contains characters outside Latin1 range.

**Implementation details**:
- Pure JavaScript implementation
- Handles padding correctly (=, ==)
- Validates input characters

### Array Marshaling
JS arrays and TypedArrays are automatically marshaled to C# arrays when calling C# methods:

```javascript
// TypedArrays → typed C# arrays
const floats = new Float32Array([1.5, 2.5, 3.5])
CS.MyClass.TakeFloatArray(floats)  // float[]

const ints = new Int32Array([1, 2, 3])
CS.MyClass.TakeIntArray(ints)  // int[]

// JS arrays of objects → Unity struct arrays
const vectors = [
    { x: 1, y: 2, z: 3 },
    { x: 4, y: 5, z: 6 }
]
CS.MyClass.TakeVector3Array(vectors)  // Vector3[]

// Tuple-style arrays also work
const colors = [
    [1, 0, 0, 1],  // Red (r, g, b, a)
    [0, 1, 0, 1]   // Green
]
CS.MyClass.TakeColorArray(colors)  // Color[]

// Direct mesh creation example
const mesh = new CS.UnityEngine.Mesh()
mesh.vertices = [{ x: 0, y: 0, z: 0 }, { x: 1, y: 0, z: 0 }, ...]  // Vector3[]
mesh.normals = [{ x: 0, y: 1, z: 0 }, ...]  // Vector3[]
mesh.uv = [{ x: 0, y: 0 }, { x: 1, y: 0 }, ...]  // Vector2[]
mesh.triangles = new Int32Array([0, 1, 2, ...])  // int[]
```

**Supported TypedArray mappings**:
| TypedArray | C# Type |
|------------|---------|
| `Float32Array` | `float[]` |
| `Float64Array` | `double[]` |
| `Int32Array` | `int[]` |
| `Int16Array` | `short[]` |
| `Int8Array` | `sbyte[]` |
| `Uint32Array` | `uint[]` |
| `Uint16Array` | `ushort[]` |
| `Uint8Array` | `byte[]` |

**Supported object array mappings**:
| JS Object Shape | C# Type |
|-----------------|---------|
| `{ x, y }` | `Vector2[]` |
| `{ x, y, z }` | `Vector3[]` |
| `{ x, y, z, w }` | `Vector4[]` |
| `{ r, g, b, a }` | `Color[]` |
| `[x, y]` (tuple) | `Vector2[]` |
| `[x, y, z]` (tuple) | `Vector3[]` |
| `[x, y, z, w]` (tuple) | `Vector4[]` / `Color[]` |

**Implementation details**:
- Arrays are serialized as JSON with a `__csArray` marker and type hint
- Type inference runs on the first element for JS arrays
- Empty typed arrays (e.g., `new Int32Array(0)`) preserve their type
- Empty JS arrays (`[]`) default to `object[]`
