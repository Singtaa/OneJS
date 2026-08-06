# OneJS

JavaScript runtime for Unity UI Toolkit. Write UI with React and TypeScript, render natively through Unity's GPU-accelerated UI system.

## V3 vs V2

This is the V3 branch. Key changes from V2:

| | V2 | V3 |
|---|---|---|
| UI Framework | Preact | React 19 |
| JS Engine | PuerTS | QuickJS |
| Setup | Manual UIDocument/PanelSettings | One-click (Initialize Project) |
| Styling | USS only | USS + CSS Modules + Tailwind |

V3 requires Unity 6.3+. For older Unity versions, use the `main` branch (V2).

## Requirements

- Unity 6.3+
- Node.js 18+

## Installation

**Package Manager (recommended):**

1. Open Window > Package Manager
2. Click + > Add package from git URL
3. Enter: `https://github.com/Singtaa/OneJS.git`

**Manual:**

```bash
git clone https://github.com/Singtaa/OneJS.git Assets/OneJS
```

## Quick Start

1. Add the `JSRunner` component to a GameObject in a saved scene
2. Click **Initialize Project** in the inspector (or just enter Play mode - first-run setup happens automatically)

That's it. JSRunner creates PanelSettings, a UIDocument template, and a working directory next to your scene, scaffolds a starter React app, then runs `npm install` and `npm run build` for you. The starter UI renders in the Game view immediately, no Play mode needed (edit-mode preview).

The editor manages the esbuild watcher for you: during Play mode and edit-mode preview, saving a source file rebuilds the bundle and OneJS hot-reloads the UI. You can also run `npm run watch` in the working directory manually for terminal workflows.

## Project Layout

Created by Initialize Project, next to your scene:

```
Assets/Scenes/Level1.unity            # Your scene
Assets/Scenes/Level1/App/             # App = the GameObject's name
├── ~/                                # Working directory (~ = ignored by Unity)
│   ├── index.tsx                     # Entry point
│   ├── package.json, tsconfig.json, esbuild.config.mjs
│   └── styles/, types/
├── PanelSettings.asset               # Project marker, assigned to JSRunner
├── UIDocument.uxml
└── app.js.txt                        # Built bundle (esbuild output)
```

The folder containing the assigned PanelSettings asset is the project's identity: the bundle is always loaded from `{that folder}/app.js.txt`. Move the folder and everything moves with it.

## JSRunner Inspector

| Field | Purpose |
|-------|---------|
| Panel Settings | The project marker (required; created and assigned by Initialize Project) |
| Live Reload | Watch the built bundle and hot-reload on change (default on) |
| Default Files | Templates used for scaffolding |
| Stylesheets | USS applied on init/reload |
| Preloads | Scripts eval'd before the entry bundle |
| Globals | Objects exposed as `globalThis[key]` |
| Typing Assemblies | C# assemblies to generate TypeScript declarations for |

## Features

**Runtime**
- QuickJS engine (interpreter, works on iOS/consoles where JIT is prohibited)
- WebGL uses browser's native JS engine (V8/SpiderMonkey with JIT)
- Full C# interop via `CS` global proxy
- Async/await support (C# Tasks become JS Promises)

**Development**
- Live reload (watches the built bundle, hot-reloads the context)
- One-click setup (Initialize Project scaffolds everything)
- Edit-mode preview (UI renders without Play mode)
- TypeScript, JSX, CSS Modules, Tailwind CSS

**Web APIs**
- `fetch()` using UnityWebRequest
- `localStorage` / `sessionStorage` using PlayerPrefs
- `URL` / `URLSearchParams`
- `atob()` / `btoa()`
- `setTimeout`, `setInterval`, `requestAnimationFrame`

**Build Support**
- JS bundle embedded as a TextAsset automatically during builds (no StreamingAssets step)
- Optional source map TextAsset for error translation
- Works on Desktop, Mobile, WebGL

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
│   ├── GPU/                     # Compute shader bridge
│   └── Particles/               # 2D particle engine
├── Editor/
│   ├── JSRunnerEditor.cs        # Custom inspector
│   ├── JSPadEditor.cs           # JSPad inspector
│   ├── JSRunnerBuildProcessor.cs # Build automation
│   └── Templates/               # Scaffolding templates
├── Plugins/                     # Native QuickJS libs (Windows/macOS/Linux/Android/iOS) + WebGL jslib
├── Resources/
│   └── OneJS/QuickJSBootstrap.js.txt  # JS runtime (CS proxy, events, scheduling)
└── Tests/                       # Unity PlayMode tests
```

## Platform Support

| Platform | JS Engine | Notes |
|----------|-----------|-------|
| Editor | QuickJS | Live reload + edit-mode preview |
| Windows/Mac/Linux | QuickJS | Bundle embedded as TextAsset |
| iOS | QuickJS | Static linking |
| Android | QuickJS | Bundle embedded as TextAsset |
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

## Documentation

Full documentation: https://onejs.com/docs

**AI agents:** read [AGENTS.md](AGENTS.md) for a condensed, agent-oriented guide to working with OneJS.

## License

MIT
