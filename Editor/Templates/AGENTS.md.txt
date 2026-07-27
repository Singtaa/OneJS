# OneJS App - Agent Guide

This folder is a OneJS v3 app: React 19 + TypeScript, bundled by esbuild into `../app.js.txt`, executed inside Unity by the JSRunner component (QuickJS engine; the browser's JS engine on WebGL), rendered through UI Toolkit. There is no DOM and no webview. Full docs: https://onejs.com/docs

## Commands (run in this folder)

- `npm install` - once, and after dependency changes
- `npm run build` - bundle to `../app.js.txt`
- `npm run watch` - rebuild on save; Unity hot-reloads whenever the bundle changes (edit-mode preview and Play mode)
- `npm run typecheck` - `tsc --noEmit`

Inside the Unity editor the watcher is started automatically (during Play mode and edit-mode preview), so manual `npm run watch` is mainly for working outside the editor.

`npm run build` + `npm run typecheck` are the fastest way to validate changes without touching the Unity editor. JS runtime errors appear in the Unity Console.

## Rules

- The entry point is `index.tsx` and it must end with `render(<App />, __root)`.
- Do not change `format`/`globalName` in `esbuild.config.mjs` or remove its react aliases (ESM output breaks QuickJS; duplicate React copies break hooks).
- Generated files, never edit by hand: `../app.js.txt`, `../app.js.map.txt`, `*.module.uss.d.ts`, `types/csharp.d.ts`.
- Change handlers receive `e.value`, not `e.target.value`.
- Use `<Text text="..." />` for display text (raw string children become bare TextElements).
- Hot reload is a hard reload: all JS state is lost; `useEffect` cleanups run first, then the bundle re-runs.
- Module-level code also runs in edit-mode preview (no Play mode). Guard play-only logic with the `__isPlaying` global and the exported `onPlay()`/`onStop()` lifecycle functions - play-mode C# singletons are null in preview.

## Quick reference

Components (from `onejs-react`): `View, Text, Label, Button, TextField, Toggle, Slider, ScrollView, Image, ListView, FrostedGlass, ScreenProvider, Portal, ErrorBoundary`.

Styling, all bundle-embedded (works in player builds):

- Inline: `style={{ padding: 8, backgroundColor: "#222" }}` - numbers are px, flexbox layout, shorthands like `padding`/`margin`/`borderRadius` auto-expand
- Plain USS: `import uss from "./styles/main.uss"` + `compileStyleSheet(uss, "main.uss")` once at startup
- CSS Modules: `import styles from "./x.module.uss"` → `className={styles.foo}`
- Tailwind: `import "onejs:tailwind"` once, then utility classes (responsive prefixes need `<ScreenProvider>`)
- Limits: no CSS grid, no `gap`, no `z-index` (paint order = sibling order), no shadows/filters

C# interop:

- `import { Vector3 } from "UnityEngine"` - any module path starting with an uppercase letter maps to the C# namespace
- The `CS` global reaches any loaded type: `CS.MyGame.Bridge.Instance`
- C# events: `obj.add_OnX(fn)` / `obj.remove_OnX(fn)` (same fn reference to remove); delegate fields: `obj.OnX = fn`
- Sync hooks from `onejs-react`: `useEventSync` (event-driven), `useFrameSync` (per-frame poll), `useThrottledSync`, plus `toArray` for C# collections
- Many values per frame? Marshal one JSON string per frame and parse it, instead of many proxy reads (QuickJS is an interpreter; each access is a reflection crossing)

Key docs: [quickstart](https://onejs.com/docs/quickstart), [C# interop](https://onejs.com/docs/core-concepts/csharp-interop), [state sync](https://onejs.com/docs/guides/state-sync), [styling](https://onejs.com/docs/core-concepts/styling), [building](https://onejs.com/docs/guides/building)
