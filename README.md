# OneJS

JavaScript runtime for Unity UI Toolkit. Write UI with React and TypeScript, render natively through Unity's GPU-accelerated UI system.

## V3 vs V2

This is the V3 branch. Key changes from V2:

| | V2 | V3 |
|---|---|---|
| UI Framework | Preact | React 19 |
| JS Engine | PuerTS | QuickJS |
| Setup | Manual UIDocument/PanelSettings | Zero-config (auto-created) |
| Styling | USS only | USS + CSS Modules + Tailwind |

V3 requires Unity 6.3+. For older Unity versions, use the `main` branch (V2).

## Requirements

- Unity 6.3+
- Node.js 18+

## Installation

**Package Manager (recommended):**

1. Open Window > Package Manager
2. Click + > Add package from git URL
3. Enter: `https://github.com/Singtaa/OneJS.git#onejs-v3`

**Manual:**

```bash
git clone -b onejs-v3 https://github.com/Singtaa/OneJS.git Assets/OneJS
```

## Quick Start

1. Add `JSRunner` component to any GameObject
2. Enter Play Mode

That's it. JSRunner creates UIDocument and PanelSettings automatically.

On first run, it scaffolds a starter project in the auto-created working directory (next to your scene). Run `npm install && npm run build` to compile.

## Project Structure

```
OneJS/
├── Runtime/
│   ├── JSRunner.cs              # Main entry point
│   ├── JSPad.cs                 # Inline TSX runner (no external files)
│   ├── QuickJSContext.cs        # Managed QuickJS wrapper
│   ├── QuickJSUIBridge.cs       # UI Toolkit integration
│   ├── QuickJSNative*.cs        # P/Invoke layer (partials)
│   ├── Network.cs               # Fetch API implementation
│   └── GPU/                     # Compute shader bridge
├── Editor/
│   ├── JSRunnerEditor.cs        # Custom inspector
│   ├── JSPadEditor.cs           # JSPad inspector
│   ├── JSRunnerBuildProcessor.cs # Build automation
│   └── Templates/               # Scaffolding templates
├── Plugins/
│   ├── macOS/                   # Native QuickJS library
│   └── WebGL/                   # Browser JS bridge
├── Resources/
│   └── OneJS/QuickJSBootstrap.js.txt  # JS runtime (CS proxy, events, scheduling)
└── Tests/                       # PlayMode tests
```

## Features

**Runtime**
- QuickJS engine (interpreter, works on iOS/consoles where JIT is prohibited)
- WebGL uses browser's native JS engine (V8/SpiderMonkey with JIT)
- Full C# interop via `CS` global proxy
- Async/await support (C# Tasks become JS Promises)

**Development**
- Live reload (polls for file changes, hot-reloads context)
- Zero-config setup (auto-creates UIDocument, PanelSettings, project files)
- TypeScript, JSX, CSS Modules, Tailwind CSS

**Web APIs**
- `fetch()` using UnityWebRequest
- `localStorage` / `sessionStorage` using PlayerPrefs
- `URL` / `URLSearchParams`
- `atob()` / `btoa()`
- `setTimeout`, `setInterval`, `requestAnimationFrame`

**Build Support**
- Auto-creates TextAsset bundles during build
- Optional source map TextAsset for error translation
- Works on Desktop, Mobile, WebGL

## JSRunner Inspector

JSRunner uses **scene-based auto paths** - no manual configuration needed:

```
Assets/Scenes/Level1.unity          # Your scene
Assets/Scenes/Level1_JSRunner/      # Auto-created next to scene
├── MyUI_abc123/                    # Per-JSRunner folder
│   ├── MyUI~/                      # Working directory (~ = Unity ignores)
│   │   └── index.tsx               # Source file
│   └── app.js.txt                  # Built bundle (esbuild output)
```

| Field | Purpose |
|-------|---------|
| Panel Settings | Optional custom PanelSettings asset |
| Bundle Asset | TextAsset for builds (auto-created) |
| Include Source Map | Include source maps for error translation |
| Default Files | Templates for scaffolding |
| Stylesheets | USS applied on init/reload |
| Preloads | Scripts eval'd before entry file |
| Globals | Objects exposed as `globalThis[key]` |

## Platform Support

| Platform | JS Engine | Notes |
|----------|-----------|-------|
| Editor | QuickJS | Live reload enabled |
| Windows/Mac/Linux | QuickJS | StreamingAssets |
| iOS | QuickJS | Static linking |
| Android | QuickJS | UnityWebRequest loading |
| WebGL | Browser JS | Full JIT, native performance |

## C# Interop

```javascript
// Access any C# type
CS.UnityEngine.Debug.Log("Hello from JS")

// Create instances
var go = new CS.UnityEngine.GameObject("MyObject")

// Generics
var List = CS.System.Collections.Generic.List(CS.System.Int32)
var list = new List()
list.Add(42)

// Async
var result = await CS.MyClass.GetDataAsync()
```

## License

MIT
