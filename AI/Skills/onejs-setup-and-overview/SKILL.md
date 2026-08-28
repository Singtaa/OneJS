---
name: onejs-setup-and-overview
description: "Use this skill whenever the user wants to build or set up user interface in a Unity project using OneJS, React, TypeScript, or JSX, e.g. 'add a main menu to my game', 'build a settings screen', 'make a HUD', 'set up OneJS', 'my OneJS panel is blank', 'the UI is not hot reloading'. Covers confirming OneJS is installed, creating a project with the JSRunner component, the folder layout it scaffolds, the npm build loop, and the traps that make a panel render nothing. Do NOT use for per-component API detail, which lives at https://onejs.com/docs, and do NOT use for UI Toolkit, uGUI, or IMGUI work in a project where OneJS is not installed. When in doubt whether a Unity UI request could involve OneJS, use this skill: Prerequisites shows how to confirm the asset is installed in a single check."
metadata:
  asset: "OneJS"
  publisher: "DragonGround"
  asset-version: "3.4.0"
  skill-version: "1.0.1"
  unity: "6000.3+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/gui"
  asset-store-url: "https://assetstore.unity.com/packages/tools/gui/onejs-221317"
  documentation-url: "https://onejs.com/docs"
  support-url: "https://discord.gg/dwnYFte6SF"
  last-verified: "2026-08-28"
---

# Set Up a OneJS Project

OneJS runs React 19 and TypeScript user interfaces inside Unity. TSX source compiles with esbuild into a single bundle that the `JSRunner` component executes through QuickJS, or through the browser's own JavaScript engine on WebGL, and renders through UI Toolkit. There is no DOM and no webview. This skill takes a project from "OneJS is installed" to "a React component is rendering in the Game view and hot reloading on save", and lists the small number of mistakes that account for most blank panels.

## When to use this skill

- "set up OneJS in this project", "get OneJS running", "initialize a OneJS app"
- "add a main menu", "build a settings screen", "make a HUD", "add an inventory panel", in a project that has OneJS installed
- "write this UI in React instead of C#", "use TypeScript for my Unity UI"
- "my OneJS panel is blank", "nothing renders in the Game view", "hot reload stopped working"
- "where does the UI code live", "why is there a folder with a tilde in it"

Not for:

- Per-component props and API detail. Read https://onejs.com/docs instead.
- Projects without OneJS installed. Run the Prerequisites check first; if it fails, say so rather than writing OneJS code that cannot compile.
- uGUI (`Canvas`, `UnityEngine.UI`) or IMGUI (`OnGUI`) work. OneJS renders through UI Toolkit only.

## Prerequisites

- Unity 6000.3 or newer.
- Node.js 18 or newer on the development machine, on `PATH`. OneJS shells out to `npm` for install and build.
- No other Unity packages and no scoped registries are required.

**Install check.** Confirm the type `OneJS.JSRunner` resolves. Any one of these is sufficient:

```csharp
// In an editor script or an eval tool
var t = System.Type.GetType("OneJS.JSRunner, OneJS.Runtime");
UnityEngine.Debug.Log(t != null ? "OneJS installed" : "OneJS NOT installed");
```

Or confirm on disk that one of these paths exists: `Assets/Singtaa/OneJS/` (Asset Store), `Assets/OneJS/` (git clone), or `Packages/com.singtaa.onejs/` (Package Manager).

**If the check fails**, tell the user OneJS is not installed and stop. Do not scaffold OneJS files by hand. Installation is one of:

- Package Manager: `+` > **Add package from git URL** > `https://github.com/Singtaa/OneJS.git`
- Clone: `git clone https://github.com/Singtaa/OneJS.git Assets/OneJS`
- Asset Store: https://assetstore.unity.com/packages/tools/gui/onejs-221317 (bundles the runtime with premade themes and sample cartridges)

## Quick start

Get a rendering, hot reloading component in a saved scene.

1. Save the scene first. The scene's folder on disk determines where the app is created.
2. Create a GameObject and add the **JSRunner** component to it.
3. Click **Initialize Project** in the JSRunner inspector. This creates the project folder, scaffolds the source files, then runs `npm install` and `npm run build` for you. The first run takes a moment while packages install.
4. Open `{SceneDir}/{SceneName}/{GameObjectName}/~/index.tsx` and edit the JSX.
5. Save. The bundle rebuilds and Unity reloads it.

**Expected observable result:** the Game view shows the rendered interface without entering Play mode, `app.js.txt` exists next to the scene folder's `PanelSettings.asset`, and the Console has no OneJS errors.

There is a second path that skips the button: assign nothing and just enter Play mode. On first play, `JSRunnerAutoWatch` creates and assigns PanelSettings, scaffolds any missing files, runs `npm install` and `npm run build` in the background, and starts the file watcher.

## Workflows

### Workflow: Create a new OneJS project in a scene

**Goal.** Turn a saved scene into a OneJS app with a working build loop.

**Steps.**

1. Confirm the scene is saved. An unsaved scene has no folder on disk and Initialize Project cannot place the app.
2. Add `JSRunner` to a GameObject.
3. Click **Initialize Project**. It produces this layout next to the scene:

```
{SceneDir}/{SceneName}/{GameObjectName}/
├── ~/                      # TypeScript source. The ~ suffix keeps Unity from importing it.
│   ├── index.tsx           # entry point
│   ├── package.json, tsconfig.json, esbuild.config.mjs
│   ├── types/global.d.ts, styles/main.uss, .gitignore
│   └── AGENTS.md           # per-app agent notes, written by the scaffold
├── PanelSettings.asset     # project marker, the one mandatory JSRunner field
├── UIDocument.uxml
├── app.js.txt              # built bundle, esbuild writes ../app.js.txt
└── app.js.map.txt
```

4. Wait for `npm install` and `npm run build` to finish. Watch the Console.

**Expected result.** `app.js.txt` exists and is not empty, and the Game view renders in edit mode.

Two rules that matter here:

- **PanelSettings is the project marker.** The folder containing the assigned PanelSettings asset defines the project, and the bundle is always loaded from `{that folder}/app.js.txt`. Moving or reassigning that asset moves the project.
- **Do not add a UIDocument component yourself.** JSRunner adds and wires one at runtime, plus an EventSystem if the scene lacks one. On Unity 6.5 and newer, do not substitute the newer `PanelRenderer` either: it never attaches a panel outside Play mode, so edit-mode preview would silently render nothing.

Scaffolding never overwrites files that already exist, so running Initialize Project again on a set-up project is safe.

### Workflow: Add a UI component

**Goal.** Add a React component and see it in the Game view.

**Steps.**

1. Create the component under `~/`, for example `~/components/MainMenu.tsx`.
2. Import and use it from `index.tsx`. Keep the final line of `index.tsx` as the render call.
3. Save. If the watcher is running (it starts automatically inside the editor during Play mode and edit-mode preview), the bundle rebuilds. Otherwise run `npm run build` in `~/`.

```tsx
import { render, View, Text, Button } from "onejs-react"

function MainMenu() {
    return (
        <View style={{ padding: 24 }}>
            <Text text="Main Menu" style={{ fontSize: 28 }} />
            <Button text="Start" onClick={() => {}} />
        </View>
    )
}

render(<MainMenu />, __root)
```

**Expected result.** The Game view shows the component. Editing and saving updates it without entering Play mode.

Conventions that differ from web React and cause most first-time bugs:

- Use `<Text text="..." />` or `<Label text="..." />` for display text. A raw string child such as `<View>Hello</View>` also produces a `TextElement`, but an implicit one you cannot pass props to. The defect is the missing `text` prop, not the element type.
- Change handlers receive `e.value`, not `e.target.value`.
- Import C# types with ES6 syntax, for example `import { Texture2D } from "UnityEngine"`. An esbuild plugin transforms these into `CS.*` references at build time. Prefer this over writing `CS.UnityEngine.Texture2D` by hand.
- Style shorthands such as `padding` and `borderRadius` work and are expanded by the reconciler, even though UI Toolkit has no native shorthand for them.
- Module-level code runs in edit-mode preview as well as Play mode. Guard play-only logic with the `__isPlaying` global, or put it in the exported `onPlay()` and `onStop()` lifecycle functions. Play-mode singletons are null during preview.
- Hot reload is a hard reload. All JavaScript state is lost, `useEffect` cleanups run first, then the bundle re-runs.

### Workflow: Set up a project without clicking the inspector

**Goal.** Initialize an app from a script, for CI or headless work.

**Steps.** The three methods the Initialize Project button calls are public on `JSRunner`. Call them in this order from an editor script, which `-executeMethod` can then invoke:

```csharp
runner.PopulateDefaultFiles();
runner.EnsureProjectFolderAndAssets(true);
runner.EnsureProjectSetup();
```

Then run `npm install` and `npm run build` inside the created `~/` folder.

**Expected result.** The same layout as the button produces, with `app.js.txt` written.

## Verification

Confirm the work without asking a human to look at the screen:

- `app.js.txt` exists in the project folder (next to `PanelSettings.asset`) and its length is greater than zero.
- `npm run build` in `~/` exits with code 0.
- `npm run typecheck` in `~/` exits with code 0. This is `tsc --noEmit` and is the fastest way to catch TSX mistakes without the editor.
- The Unity Console contains no entries prefixed `[JSRunner]` at error level, and no JavaScript exceptions. JavaScript runtime errors surface in the Console, not in a browser devtools panel.
- The JSRunner GameObject has a PanelSettings asset assigned. With it unassigned, JSRunner does nothing at all and reports no error.

`npm run build` plus `npm run typecheck` together validate almost every change without the Unity editor being involved.

### Proving the UI actually rendered

The checks above prove the bundle built, not that React mounted anything. To prove
that without a human looking at the Game view, drive edit-mode preview from a
temporary editor script and read the live tree off the UIDocument. This works in
batch mode and does not need a Scene view, a graphics device, a selection, or Play
mode.

Preconditions: `runner.HasBundle` is true, `runner.IsPanelSettingsInValidProjectFolder()`
is true, and the script opens the scene containing the runner itself.

```csharp
using System.Text;
using OneJS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class OneJSVerify {
    static JSRunner _runner;
    static int _frames, _runningAt = -1;

    public static void Run() {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        _runner = Object.FindFirstObjectByType<JSRunner>();
        if (_runner == null) { Debug.LogError("[Verify] no JSRunner"); EditorApplication.Exit(1); return; }
        EditorApplication.update += Tick;
    }

    static void Tick() {
        _frames++;
        if (_runningAt < 0 && _runner.IsRunning) _runningAt = _frames;
        if (_frames > 4000) { Finish(1, "runner never started"); return; }  // required, this is the timeout
        if (_runningAt < 0) return;
        if (_frames < _runningAt + 100) return;                            // let React commit

        var root = _runner.GetComponent<UIDocument>()?.rootVisualElement;
        if (root == null) { Finish(1, "no rootVisualElement"); return; }

        var sb = new StringBuilder();
        Dump(root, 0, sb);
        Debug.Log("[Verify] UI TREE:\n" + sb);
        Finish(0, "ok");
    }

    static void Dump(VisualElement e, int depth, StringBuilder sb) {
        var text = e is TextElement te ? $" \"{te.text}\"" : "";
        sb.AppendLine($"{new string(' ', depth * 2)}{e.GetType().Name}{text}");
        foreach (var c in e.Children()) Dump(c, depth + 1, sb);
    }

    static void Finish(int code, string msg) {
        EditorApplication.update -= Tick;
        Debug.Log($"[Verify] RESULT({code}): {msg} after {_frames} frames");
        EditorApplication.Exit(code);
    }
}
```

Invoke it **without `-quit`**:

```bash
<editor> -batchmode -projectPath "$PWD" -executeMethod OneJSVerify.Run -logFile "$PWD/Logs/verify.log"
```

**Adding `-quit` silently breaks this check.** The script pumps
`EditorApplication.update` and exits itself. With `-quit`, Unity tears down as soon
as `Run()` returns, before `update` ever fires, and you get exit code 0 with no
tree: a false pass, which is worse than no check. The frame cap is equally
mandatory, since without it a runner that never starts hangs the build forever.

Gate on `IsRunning` rather than a fixed frame count. In practice it is true on the
first `update` callback, so a large fixed wait is dead time on a fast machine and
still a gamble on a slow one.

Expected output is the rendered hierarchy, which is what proves React committed:

```
UIDocumentRootElement
  VisualElement
    Label "YOUR GAME"
    Button "START"
    Button "SETTINGS"
```

Verified on Unity 6000.5.2f1, macOS, one JSRunner in the scene, with and without
`-nographics`. The state gate and the frame cap are what make it portable; treat
other editor versions, other platforms, and scenes with several runners as
untested rather than guaranteed.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `OneJS.JSRunner` | MonoBehaviour | Runs a JavaScript app from a project folder. The one component a scene needs. |
| `JSRunner.PanelSettingsAsset` | Property | The assigned PanelSettings. Its folder is the project marker. |
| `JSRunner.PopulateDefaultFiles()` | Method | Loads the scaffold templates into the runner's default file list. |
| `JSRunner.EnsureProjectFolderAndAssets(bool)` | Method | Creates the project folder, PanelSettings, and UIDocument.uxml. |
| `JSRunner.EnsureProjectSetup()` | Method | Writes any missing scaffolded source files. Never overwrites. |
| `JSRunner.GetJSFunction<T>(string)` | Method | Binds a JavaScript global to a typed C# delegate that survives hot reload. |
| `JSRunner.Reloaded` | Event | Fires after each hot reload, for C# code caching anything JavaScript side. |
| `OneJS.JSPad` | MonoBehaviour | Prototyping alternative with an inline code editor and no npm project. No hot reload. |
| `render(element, __root)` | JS, `onejs-react` | Mounts a React tree. The last line of `index.tsx`. |
| `__root` | JS global | The root VisualElement to render into. |
| `__isPlaying` | JS global | `true` in Play mode, `false` in edit-mode preview. |
| `onPlay()` / `onStop()` | JS exports | Play-mode lifecycle hooks, for logic that must not run during preview. |
| `Tools/OneJS/Type Generator` | Menu | Opens the C# type declaration generator. |
| `Tools/OneJS/Regenerate All Project Typings` | Menu | Regenerates `types/csharp.d.ts` for every OneJS project in the Unity project. |

Deeper reference, including every component and hook, lives at https://onejs.com/docs. Prefer reading it over guessing an API.

## Common issues

**Game view is blank in edit mode.**
Cause: edit-mode ticking is gated by the Scene view's OneJS overlay update mode. In the default Auto mode only the selected runner ticks, or with nothing selected the runner closest to the Scene view camera.
Fix: select the JSRunner GameObject, or switch the overlay's Scene mode to Camera. This gating affects what a human sees in the Game view. It is not a reason to skip verification: the rendered tree can still be read directly from the UIDocument, including headlessly. See Verification.

**Game view is blank and the Console is silent.**
Cause: no PanelSettings assigned, or `app.js.txt` missing. JSRunner treats both as "not set up yet" rather than as errors.
Fix: assign PanelSettings (or click Initialize Project), then run `npm run build` in `~/`.

**Text does not appear where expected.**
Cause: a raw string child was used instead of the `text` prop.
Fix: use `<Text text="Hello" />` rather than `<View>Hello</View>`.

**A change handler receives `undefined`.**
Cause: `e.target.value` was used, copying a web React habit.
Fix: use `e.value`.

**Nothing rebuilds on save.**
Cause: the esbuild watcher is not running, which happens when working outside the editor.
Fix: run `npm run watch` in `~/`, or let the editor start it by entering Play mode or edit-mode preview.

**`npm` fails or is not found.**
Cause: Node.js is missing from `PATH`, or is older than 18.
Fix: install Node 18 or newer. OneJS also probes common `nvm` install locations.

**State resets on every save.**
Cause: none. Hot reload is a hard reload by design.
Fix: if state must survive, keep it in C# and read it back, or persist it deliberately.

**The build has no user interface, but the editor did.**
Cause: the bundle was not built before the player build.
Fix: run `npm run build` in `~/`, then build. The bundle ships as a serialized TextAsset, so StreamingAssets is not involved.

## Boundaries

- **No DOM and no webview.** There is no `document`, no `window` layout, no HTML elements, and no CSS beyond the USS subset that UI Toolkit supports. An npm package that reaches for DOM APIs or Node builtins will not run.
- **UI Toolkit only.** OneJS does not render to uGUI (`Canvas`) or IMGUI. Mixing is possible at the Unity level but is outside this asset.
- **Node.js is a development dependency, not a runtime one.** Builds ship a compiled bundle; the player never needs Node.
- **JSPad has no hot reload.** It is for prototyping. Use JSRunner for anything maintained.
- **Deep API surface is documented, not duplicated here.** Component props, hooks, C# interop detail, styling, vector drawing, particles, the GPU image pipeline, and platform specifics live at https://onejs.com/docs. Read the relevant page rather than inferring an API.
