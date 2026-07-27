# OneJS v3 - Agent Guide

Condensed facts for AI agents working with OneJS. Full documentation: https://onejs.com/docs

OneJS runs React 19 + TypeScript UIs inside Unity. TSX compiles with esbuild into a single bundle that the `JSRunner` component executes through QuickJS (or the browser's own JS engine on WebGL), rendering through UI Toolkit. No webview, no DOM.

## Requirements

- Unity 6000.3+ (Unity 6.3)
- Node.js 18+ on the dev machine

## Install

Package Manager: `+` > Add package from git URL:

```
https://github.com/Singtaa/OneJS.git#onejs-v3
```

Or clone into Assets: `git clone -b onejs-v3 https://github.com/Singtaa/OneJS.git`. Package id: `com.singtaa.onejs`. No other Unity packages or scoped registries required.

## Project setup

1. Add the `JSRunner` component to a GameObject in a saved scene.
2. Click **Initialize Project** in its inspector. This creates, next to the scene:

```
{SceneDir}/{SceneName}/{GameObjectName}/
├── ~/                      # TS/TSX source (the ~ suffix keeps Unity from importing it)
│   ├── index.tsx           # entry point
│   ├── package.json, tsconfig.json, esbuild.config.mjs
│   └── styles/main.uss, types/global.d.ts, AGENTS.md, .gitignore
├── PanelSettings.asset     # project marker - the one mandatory JSRunner field
├── UIDocument.uxml
├── app.js.txt              # built bundle (esbuild writes ../app.js.txt)
└── app.js.map.txt
```

then runs `npm install` and `npm run build` automatically.

Alternative: skip the button and just enter Play mode. On first play, `JSRunnerAutoWatch` auto-creates and assigns PanelSettings, scaffolds missing files, runs `npm install` + `npm run build` in the background, and starts the watcher. The first run takes a moment while packages install.

Key model:

- **Panel Settings is the project marker.** The folder containing the assigned PanelSettings asset defines the project; the bundle is always loaded from `{that folder}/app.js.txt`. A folder is considered valid when it contains `~/` or `app.js.txt`.
- **Do not add a UIDocument.** JSRunner adds and wires one at runtime (and an EventSystem if missing). On Unity 6.5+, do **not** substitute the new `PanelRenderer` either: it never attaches a panel outside Play mode, so edit-mode preview would silently render nothing. OneJS stays on `UIDocument`, which is not `[Obsolete]`. See `Runtime/README.md`.
- **Edit-mode preview**: JSRunner renders the UI in the Game view without Play mode whenever Panel Settings is valid, `app.js.txt` exists on disk, and the UIDocument root is ready. Ticks at 30 Hz.
- Scaffolding never overwrites existing files.

Headless (no inspector click): the three methods the button calls are public on `JSRunner` - `PopulateDefaultFiles()`, `EnsureProjectFolderAndAssets(true)`, `EnsureProjectSetup()` - callable from a small editor script (which `-executeMethod` can invoke); then run `npm install && npm run build` in `~/`.

## Build and live reload

All commands run inside `~/`:

- `npm run build` - one-shot bundle
- `npm run watch` - rebuild on save
- `npm run typecheck` - `tsc --noEmit`

Inside the editor, the esbuild watcher is **managed automatically** (`JSRunnerAutoWatch` / `NodeWatcherManager`): it starts when edit-mode preview begins (if `node_modules` exists) and on entering Play mode (running `npm install`/`build` first if needed), and stops on Play mode exit. Running `npm run watch` manually also works and is the path for working outside the editor.

JSRunner itself watches the **built bundle** (`app.js.txt`), not source files: an MD5 content poll every 0.5 s plus a FileSystemWatcher. So the loop is simply: save a source file, the watcher rebuilds the bundle, Unity hot-reloads - in both edit-mode preview and Play mode.

Reload is a **hard reload** (fresh JS context, all JS state lost): `onStop()` (if playing) → Janitor destroys JS-created GameObjects → React teardown (`useEffect` cleanups DO run) → new context, globals re-injected → bundle re-runs → `onPlay()` (if playing).

Player builds: `JSRunnerBuildProcessor` embeds the bundle as a TextAsset automatically. The bundle never goes through StreamingAssets.

## Authoring

The entry file ends with a render call:

```tsx
import { render, View, Text } from "onejs-react"

function App() {
    return <View style={{ flexGrow: 1 }}><Text text="Hello" /></View>
}

render(<App />, __root)

export function onPlay() {}   // Play mode start + after hot reload while playing
export function onStop() {}   // Play mode exit + before hot reload while playing
```

- `__root` (root VisualElement) and `__isPlaying` are ambient globals (declared in `types/global.d.ts`).
- Module-level code also runs in **edit-mode preview**. Guard play-only logic with `__isPlaying` (play-mode C# singletons are null in preview).
- Components: `View, Text, Label, Button, TextField, Toggle, Slider, ScrollView, Image, ListView, FrostedGlass`, plus `ScreenProvider`, `Portal`, `ErrorBoundary`.
- Change handlers receive `e.value` (NOT `e.target.value`).
- Raw text children (`<View>Hi</View>`) create TextElements; prefer `<Text text="..." />`.

The esbuild config must keep `format: "iife"` + `globalName: "__exports"` (QuickJS evals in global scope, so ESM output throws, and lifecycle exports are discovered via `__exports`) and the react/jsx-runtime aliases (duplicate React copies break hooks).

### Styling

All four options embed in the bundle and work in player builds:

1. Inline objects: numbers are px; flexbox layout; shorthands `padding/margin/borderWidth/borderColor/borderRadius` auto-expand; transforms via `translate: [x, y]`, `rotate: "45deg"`, `scale: 1.5`.
2. Plain USS: `import uss from "./styles/main.uss"` then `compileStyleSheet(uss, "main.uss")` once at startup. (Avoid runtime `loadStyleSheet()` for app styles: it reads the filesystem and is not shipped in builds.)
3. CSS Modules: `import styles from "./x.module.uss"` → `className={styles.container}`; `.d.ts` auto-generated.
4. Tailwind: `import "onejs:tailwind"` once, then utility classNames. Built-in JIT generator, no npm dependency. Responsive prefixes need `<ScreenProvider>`.

Limits: no CSS grid, no `gap`, no `z-index` (paint order = sibling order), no shadows/filters.

## C# interop

JS → C#:

- ES6 imports in app code: `import { Vector3 } from "UnityEngine"` (esbuild rewrites module paths starting with an uppercase letter to `CS.*`).
- The `CS` global proxy reaches any type in any loaded assembly, including game code: `CS.MyGame.Bridge.Instance`. Prefer namespaced C# types.
- Structs read by field access (`pos.x`); C# lists/arrays support `.Count`/`.Length` + indexing; `toArray<T>(list)` (from `onejs-react`) makes a real JS array.
- Generic types work: `CS.System.Collections.Generic.List(CS.System.Int32)`. Generic **methods** are unsupported - wrap them in plain C# methods. Extension methods need `useExtensions(CS.The.StaticClass)` first.

C# → JS:

- C# `event` fields: subscribe with `obj.add_OnX(fn)` / `obj.remove_OnX(fn)` (pass the same fn reference to remove). Plain `OnX?.Invoke(...)` in C# then calls the JS handler. Invoke on Unity's main thread only.
- Delegate fields (`public Action OnX;`): assign directly, `obj.OnX = fn` (single handler; assign `null` to clear).
- Hooks from `onejs-react`: `useEventSync(source, "Health")` subscribes `add_OnHealthChanged` and re-reads `source.Health` (explicit form takes `[[source, "OnEvent"], ...]`); `useFrameSync(() => obj.Value)` polls per frame; `useThrottledSync(getter, ms)`.
- The JSRunner `_globals` inspector list injects `UnityEngine.Object` references as `globalThis[key]`.

Performance on QuickJS (an interpreter): every proxy access is a reflection crossing. For many values per frame, marshal one JSON string per frame and parse it in JS, or register `QuickJSNative.FastPath` accessors for zero-alloc reads. See https://onejs.com/docs/guides/zero-alloc

## Gotchas checklist

- IIFE bundle format is mandatory; never emit ESM.
- Keep the react aliases in the esbuild config.
- `e.value`, not `e.target.value`.
- `console.log(csObject)` prints a handle, not fields - log fields directly. JS logs land in the Unity Console.
- Native callback table = 4096 slots (one per JS function bound to a C# delegate/event; freed on reassign/`remove_`).
- IL2CPP/AOT builds strip dynamically-accessed code: ship a `link.xml` preserving your game assemblies or you get `[QuickJS] Type/Method not found` in builds only. See https://onejs.com/docs/guides/building
- Scene transitions destroy JSRunner before React cleanup runs - also clear static delegate subscriptions in `onStop`.
- On Android/WebGL, `StreamingAssets` is a URL, not a directory: use the async asset loaders (`loadImageAsync` etc.) from `onejs-unity/assets`.

## Verifying without the editor GUI

- `npm run build` and `npm run typecheck` validate the whole JS side from the CLI - no editor needed.
- Unity batchmode runs tests: `-batchmode -runTests` (EditMode/PlayMode).
- Edit-mode preview renders the UI without Play mode; JS errors surface in the Unity Console.

## Repo map

| Path | Contents |
|------|----------|
| `Runtime/` | JSRunner, JSPad, QuickJSContext/UIBridge, P/Invoke layer, fetch, GPU compute, particles |
| `Editor/` | Inspectors, build processor, scaffolding templates (`Editor/Templates/`), type generator |
| `Resources/OneJS/QuickJSBootstrap.js.txt` | JS runtime core: CS proxy, events, timers, teardown hooks |
| `Plugins/` | Native QuickJS libraries (Windows/macOS/Linux/Android/iOS) + WebGL jslib |
| `Tests/` | Unity PlayMode/EditMode tests |

## Docs map

- Quickstart: https://onejs.com/docs/quickstart
- Project setup (esbuild/tsconfig): https://onejs.com/docs/guides/project-setup
- C# interop: https://onejs.com/docs/core-concepts/csharp-interop
- State sync (C# → React): https://onejs.com/docs/guides/state-sync
- Styling: https://onejs.com/docs/core-concepts/styling (CSS Modules and Tailwind under `/docs/guides`)
- Building and deployment (link.xml, WebGL): https://onejs.com/docs/guides/building
- Component reference: https://onejs.com/docs/components/view (and siblings)
