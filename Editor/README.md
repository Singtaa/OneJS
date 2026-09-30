# OneJS Editor

Editor scripts for OneJS Unity integration.

## Files

| File | Purpose |
|------|---------|
| `JSRunnerEditor.cs` | Custom inspector for JSRunner component |
| `JSRunnerAutoWatch.cs` | Auto-starts file watchers on Play mode entry |
| `JSRunnerCleanup.cs` | Tracks JSRunner instances for cleanup bookkeeping |
| `JSPadEditor.cs` | Custom inspector for JSPad inline runner |
| `JSRunnerBuildProcessor.cs` | Build hook for auto-copying JS bundles |
| `SLShaderGenerator.cs` | Turns `*.sl.json` manifests into generated shaders, records programs the editor draws into `Assets/OneJS/Recorded.sl.json` on the next editor update, generates from the manifest beside a bundle before every load (`JSRunner.EditorLoadingBundle`), records JSPad's manifest after its build, and holds `SLShaderBuildStep`, which ships every program compiled in a native player through `SLShaderRegistry` and fails a build whose recorded programs are under a hash scheme no app manifest produces (`CheckRecorded`); recording drops those programs |
| `OneJSLinkXmlProcessor.cs` | Hands the linker `Plugins/link.xml` when OneJS is a package, since Unity reads a link.xml only under `Assets/`; without it IL2CPP strips the UI Toolkit internals OneJS reflects on |
| `NodeWatcherManager.cs` | Manages Node.js file watcher processes for live reload |
| `OneJSProcessUtils.cs` | Process helpers (tree-kill on Windows via `taskkill /T /F`, Unix via `pgrep -P`) |
| `OneJSEditorDesign.cs` | Centralized design tokens (colors, text labels) for editor UIs |
| `DefaultFileEntryDrawer.cs` | Property drawer for default file entries in JSRunner |
| `GlobalEntryDrawer.cs` | Property drawer for global entries in JSRunner |
| `CartridgeFileEntryDrawer.cs` | Property drawer for cartridge file entries |
| `CartridgeObjectEntryDrawer.cs` | Property drawer for cartridge object entries |
| `UICartridgeEditor.cs` | Custom inspector for `UICartridge` assets |
| `OneJSEditorOverlay.cs` | The Scene view **OneJS** overlay and its update modes, installed as `JSRunner.EditModeUpdateFilter`: decides which runners' edit-mode previews tick (default Auto: the selected runner, else the one closest to the Scene camera) |
| `OneJSWslHelper.cs` | Windows only: runs Open Terminal and npm through WSL when chosen from Open Terminal's right-click menu |
| `AISkillsInstaller.cs` | **Tools > OneJS > Install AI Skills**: copies `AI/Skills/` into the project's `.claude/skills/`, never overwriting an edited skill without asking |
| `Recording/PanelRecorder.cs` | Renders a running JSRunner's UI to an mp4 by frame-stepping it on `VirtualClock` (see below) |
| `Recording/OffscreenPanelRenderer.cs` | Draws a PanelSettings' panel into an offscreen RenderTexture at an exact size |
| `Recording/InputTrack.cs` | Scripted pointer and keyboard input for a recording (see below) |
| `Recording/CursorOverlay.cs` | Draws the pointer and click ripples into recorded frames |
| `Recording/FfmpegLocator.cs` | Locates ffmpeg (well-known paths, then login shell) |
| `Templates/` | Files Initialize Project scaffolds into `~/` (see Templates below) |
| `TypeGenerator/` | TypeScript declaration generator (see [TypeGenerator/README.md](TypeGenerator/README.md)) |

## Menu items

| Menu | Source |
|------|--------|
| Tools > OneJS > Type Generator | `TypeGenerator/TypeGeneratorWindow.cs` |
| Tools > OneJS > Regenerate All Project Typings | `TypeGenerator/TypeGeneratorService.cs` |
| Tools > OneJS > Generate Shader Programs | `SLShaderGenerator.cs` |
| Tools > OneJS > Install AI Skills | `AISkillsInstaller.cs` |
| Assets > Create > OneJS > UI Cartridge | `Runtime/UICartridge.cs` |

## JSRunnerEditor

Custom inspector for JSRunner, built with UI Toolkit. The inspector adapts its UI based on the current state of the component's PanelSettings assignment.

### Inspector States

The inspector shows different UI depending on the PanelSettings configuration:

| State | Condition | UI Shown |
|-------|-----------|----------|
| **Not Initialized** | No PanelSettings assigned | PanelSettings field + "Initialize Project" button |
| **Not Valid** | PanelSettings assigned but folder lacks `~/` or `app.js`/`app.js.txt` | Status warning + "Initialize Project" button |
| **Initialized** | PanelSettings in a valid project folder | Full tabbed inspector |

### Tabbed Layout

When fully initialized, the Panel Settings field sits above the status section, outside the tabs, and the inspector shows four tabs:

- **Project**: tick mode, Don't Destroy On Load, live reload (poll interval, Janitor), preloads, globals
- **UI**: Stylesheets, and the assigned PanelSettings asset's own inspector
- **Cartridges**: UI Cartridge list with per-row Extract (**E**), Delete extracted (**D**) and remove (**X**), plus Extract All and Delete All Extracted
- **Build**: Build output (bundle, source map, Include Source Map, Exclude From Build), type generation, scaffolding (default files with Restore)

### Status Section

- **Running/Stopped/Loading indicator** with color-coded labels
- **Last Reload** time, shown in Play mode after the first hot reload
- **Watcher status** showing file watcher state (Running, Starting, Idle)
- **Project folder path** (clickable to open in file explorer)

### Actions

- **Reload**: Force reload the JavaScript runtime (works in both Play mode and edit-mode preview)
- **Rebuild**: Delete node_modules, reinstall dependencies, and rebuild
- **Open Folder**: Open working directory in file explorer
- **Open Terminal**: Open terminal at working directory (right-click on Windows to choose cmd or WSL)
- **Open Code Editor**: Open working directory in the editor set in Preferences > External Tools (right-click to pick another)

### Context Menu Options

Right-click the status section for:

- **Run in Background**: Toggles the project's `PlayerSettings.runInBackground`
- **Use Scene Name as Root Folder**: Toggle whether Initialize Project creates the folder under `{SceneDir}/{SceneName}/` or directly beside the scene (stored in `EditorPrefs`)

The component header's context menu has **Toggle Dev Mode**: it shows the tabs and actions even without a valid PanelSettings, for debugging.

### Initialize Project Button

When PanelSettings is not assigned or the folder is not valid, the inspector shows an "Initialize Project" button. This calls `EnsureProjectFolderAndAssets()` which:

1. Creates the PanelSettings asset (if needed) in the appropriate scene folder
2. Creates the `~/` working directory
3. Scaffolds default project files (package.json, esbuild.config, tsconfig, index.tsx, etc.)

### npm Path Detection

On macOS/Linux, Unity doesn't inherit terminal PATH. The editor searches for npm in:
1. `/usr/local/bin/npm` (Homebrew Intel)
2. `/opt/homebrew/bin/npm` (Homebrew Apple Silicon)
3. `~/.nvm/versions/node/*/bin/npm` (nvm)
4. Fallback: `bash -l -c "which npm"`

## JSRunnerAutoWatch

Automatically manages file watchers and project readiness for JSRunner instances across Play mode transitions.

### Features

- **Edit-mode watcher**: Starts the esbuild watcher when a runner's edit-mode preview starts, if `package.json` and `node_modules` exist
- **Project scaffolding**: Ensures a new app is scaffolded and each default file written once (`EnsureProjectSetup()`) before Play mode; a missing `package.json` is a warning naming the missing default files and pointing at Restore
- **Auto-install + build**: Runs `npm install` and `npm run build` if needed before starting watcher
- **Auto-start on Play**: Watchers start automatically when entering Play mode
- **Auto-stop on Exit**: All watchers are stopped when exiting Play mode (via `NodeWatcherManager.StopAll()`)

### Play Mode Lifecycle

1. Uses `[InitializeOnLoad]` to register `playModeStateChanged` callback
2. On `ExitingEditMode` (before Play starts):
   - `EnsurePanelSettingsAssets()`: Walks only runners that already resolve a project folder, which requires an assigned PanelSettings, so it creates nothing; a runner without one must go through Initialize Project
   - `EnsureProjectsReady()`: Calls `EnsureProjectSetup()` on each valid runner, which writes each default file once (see Runtime/README, Auto-Scaffolding)
   - `PrepareWatchers()`: Clears the session tracking set
3. On `EnteredPlayMode`:
   - Finds all active JSRunner components with valid working directories
   - For each: installs dependencies if needed, builds if needed, then starts watcher
4. On `ExitingPlayMode`:
   - `NodeWatcherManager.StopAll()`: Stops ALL running watchers (not just session-started ones), so folders are unlocked for move/rename in Edit mode

### Null Guards

Runners are skipped if any of these are true:
- Runner is null, disabled, or on an inactive GameObject
- Scene is not saved (`!runner.IsSceneSaved`)
- `runner.InstanceFolder` is null (no PanelSettings assigned or folder doesn't exist)

### Integration with Inspector

The inspector's watcher status label shows:
- **Running**: Watcher is active and auto-rebuilding on file changes
- **Starting...**: npm install or watcher startup in progress
- **Idle (enter Play Mode to run)**: Not in Play mode, will start automatically

## JSPadEditor

Custom inspector for the inline TSX runner:
- **Status**: Processing, Running, Ready, or Not built, plus the bundle size
- **Action button**: **Build** in Edit mode (writes `index.tsx`, runs `npm install` if `node_modules` is missing, then esbuild) and **Build & Reload** in Play mode (skips `npm install`, rebuilds, reloads)
- **Overflow menu** (**⋮**): **Open Folder** reveals `Temp/OneJSPad/{id}/`; **Clean** deletes it, `node_modules` included
- **Tabs**: UI, Cartridges, Modules (extra npm packages, with **Install**), and a Settings foldout

Entering Play mode does not build: `JSPad.Start()` runs the bundle already serialized on the component. Work done in Play mode survives the return to Edit mode: source edits are kept in `EditorPrefs`, and a bundle built in Play mode is cached to `Temp/JSPadCache/` on exit, restored onto the component, and the scene is saved so standalone builds pick it up.

## JSRunnerCleanup

Tracks JSRunner instances by their PanelSettings-derived folder paths. When a JSRunner is removed, tracking is updated but the folder is left on disk (no delete prompt).

### Features

- **Component removal detection**: Detects when a JSRunner component is removed from a GameObject
- **GameObject deletion detection**: Detects when a GameObject with JSRunner is deleted
- **Non-destructive**: Folders are never deleted automatically; only the internal tracking dictionary is updated
- **Edit mode only**: No false positives during Play mode transitions or domain reloads

### How It Works

Uses Unity's `ObjectChangeEvents` API for reliable destruction detection:

1. `[InitializeOnLoad]` subscribes to `ObjectChangeEvents.changesPublished` and `hierarchyChanged`
2. Tracks all JSRunner instances by `GlobalObjectId` → folder path (via `runner.InstanceFolder`)
3. On `DestroyGameObjectHierarchy` or `ChangeGameObjectStructure` events:
   - Schedules a deferred check via `EditorApplication.delayCall`
   - Compares current JSRunner set against tracked set
   - Removes entries for destroyed runners (folder remains on disk)

### Filtering False Positives

Only processes when:
- Not in Play mode (`!Application.isPlaying`)
- Not transitioning to/from Play mode (`!EditorApplication.isPlayingOrWillChangePlaymode`)

## JSRunnerBuildProcessor

Implements `IPreprocessBuildWithReport` and `IPostprocessBuildWithReport` to handle TextAssets for builds, alongside its nested `PrefabAppBaker`, which is a `BuildPlayerProcessor`:

1. `PrefabAppBaker.PrepareForBuild` bakes every prefab under `Assets/` that holds a JSRunner, before Addressables packs content; `OnPreprocessBuild` then walks the scenes this build is shipping
2. For each JSRunner not marked Exclude From Build and without a bundle assigned:
   - The bundle at `{InstanceFolder}/app.js.txt` (esbuild output) is already there
   - Loads it as a TextAsset and assigns to the JSRunner component
   - Loads source map TextAsset if `Include Source Map` is enabled
   - Saves modified scenes
3. Extracts Cartridge files, with overwrite, to `{WorkingDir}/@cartridges/{slug}/` (namespaced: `@cartridges/@{namespace}/{slug}/`)
4. Logs status during build

Since esbuild outputs directly to `app.js.txt`, the build processor just needs to load the existing file as a TextAsset.

### Which scenes it walks

`BuildPlayerOptions.scenes`, whatever the build passed. `PrefabAppBaker.PrepareForBuild` records the list; `OnPreprocessBuild` reads and spends it.

Three cases, because Unity treats them differently:

| The build passed | Unity ships | The processor walks |
|---|---|---|
| A scene list | those scenes | those scenes |
| An empty list, or none | the open scene | the open scene |
| Nothing at all, because `PrepareForBuild` did not run | unknown | the enabled Build Settings scenes |

This detour exists because `BuildReport` has no scene list: at `OnPreprocessBuild` time its `packedAssets` and `scenesUsingAssets` are both empty, `files` throws, and `BuildSummary` carries no scenes at all. `BuildPlayerContext` is the only place the list can be read before the build runs, and only a `BuildPlayerProcessor` is handed one, which is why the record rides along in `PrefabAppBaker`.

It matters because a command line or CI build passes its own list and ignores Build Settings. Reading Build Settings here meant the player shipped one set of scenes and another set's assets, with the scenes that did ship getting no bundle.

### Skipping Auto-Assignment

- A runner whose bundle is already assigned (by an earlier build, or by `JSRunner.SetBundleAsset` from a script; the field is hidden in the inspector) is skipped
- To leave a runner out of the build entirely, turn on **Exclude From Build** in its Build tab

## OneJSEditorDesign

Centralized design tokens for all OneJS editor UIs (`OneJSEditorDesign.cs`). Provides two static inner classes:

- **`Colors`**: Color palette (surfaces, borders, text, status indicators, buttons, per-editor overrides)
- **`Texts`**: Repeated string labels (status, actions, tabs, section headers, empty states, watcher labels)

All editor scripts (JSRunnerEditor, JSPadEditor, UICartridgeEditor) reference these tokens instead of hardcoding colors or text strings. This ensures visual consistency and makes theme changes a single-file edit.

## Recording (`Recording/` folder)

Renders a running JSRunner's UI straight to an mp4, for docs and demos.

```csharp
var runner = Object.FindAnyObjectByType<JSRunner>();
PanelRecorder.Record(runner, new PanelRecordingOptions {
    Width = 1280, Height = 720, Fps = 60, DurationSeconds = 5.0,
    OutputPath = "/abs/path/demo.mp4",
});
```

Nothing is captured from the screen. Frames come from an offscreen RenderTexture, so
output is exact-sized, free of editor chrome, unaffected by window occlusion or the
cursor, needs no OS screen-recording permission, and is identical on macOS and
Windows. Requires ffmpeg on the machine (`brew install ffmpeg`).

The run steps `VirtualClock` by exactly `1/Fps` per frame, so frame N always lands on
virtual time `N/Fps`: duration and frame count come out exact no matter how slow
rendering is, and rendering is normally far faster than realtime (10s of footage in
well under a second). This pins *timing*, not app state, so successive clips only
match if the UI itself is deterministic. Anything driven by RNG (particles) or by
carried-over React state is not.

`Record` is synchronous and blocks the editor, which keeps stepping exact (nothing
else can tick the bridge mid-run); a cancelable progress bar keeps it from looking
hung. On cancel it throws `OperationCanceledException` and removes the partial file.
The clock, the panel's render target and its transition clock are all restored in a
`finally`, so a mid-run failure cannot leave the editor frozen.

Encoding is H.264 / yuv420p with `+faststart`, which is what browsers need to
autoplay inline and to begin playback before the file finishes downloading.

`Width`/`Height` are the *capture* size, which with a ConstantPixelSize PanelSettings
also decides how much of the UI is in frame, not just the resolution. `OutputWidth`/
`OutputHeight` set the encoded size; making them smaller supersamples (render at
1920x1080, encode at 1280x720) for cleaner text and particle edges at a smaller file.

Note when driving this from the Unity MCP `unity_eval` tool: a long recording can
outrun the MCP request timeout and return "fetch failed" even though the editor
finished the job and wrote the file. Check for the output rather than re-running, or
the second run will silently overwrite the first.

### Scripted input (`InputTrack`)

Most component demos are about interaction, so a recording can drive one:

```csharp
var track = new InputTrack()
    .StartAt(120, 470)
    .MoveTo(536, 103, 0.9)   // glide to the button, hover it
    .Wait(0.6)
    .Click()
    .Wait(1.3)
    .DragTo(230, 175, 0.7);

PanelRecorder.Record(runner, new PanelRecordingOptions {
    Width = 1920, Height = 1080, OutputWidth = 1280, OutputHeight = 720,
    Fps = 60, DurationSeconds = track.Duration + 0.4,
    Input = track,
    OutputPath = "/abs/path/demo.mp4",
});
```

Actions are authored sequentially: each call appends at the current time and
advances an internal clock, so a track reads in the order it happens.
`MoveTo`/`DragTo` glide with a smoothstep ease, `Wait` lets the UI settle,
and `Duration` is the total so `DurationSeconds` can be derived from it (the
recorder warns rather than silently truncating if the track is longer).

Available: `StartAt`, `MoveTo`, `Wait`, `Click`, `Press`, `Release`, `DragTo`,
`Scroll`, `Type`, `Key`, `NavigateNext`, `NavigatePrevious`, `Navigate`.

Focus movement rides `NavigationMoveEvent`, not the Tab key: `Key(KeyCode.Tab)`
leaves focus exactly where it was, so use `NavigateNext` to walk a focus ring.
Note also that a `TextField` consumes navigation events to move its caret, so
focus entering one never leaves.

Input is delivered as genuine UI Toolkit events via `VisualElement.SendEvent`,
so `:hover` and `:active` styling, focus rings, ScrollView scrolling and every
React handler behave exactly as they do for a real user. Nothing simulates state
directly. `Type` and `Key` go to the focused element, so click a field first.

**Coordinates are panel-logical, not capture pixels.** With a ConstantPixelSize
PanelSettings at `scale: 2`, a 1920x1080 capture has a 960x540 logical space and
track coordinates are in that space. To find a position, measure with the panel
already at capture size:

```csharp
using (var probe = new OffscreenPanelRenderer(runner.PanelSettingsAsset, 1920, 1080)) {
    for (int i = 0; i < 3; i++) { runner.Bridge.Tick(); probe.Render(); }
    var bound = someElement.worldBound;   // logical coords, ready for the track
}
```

`CursorOverlay` draws a pointer into each frame (procedural, no texture asset)
plus an expanding ripple on every click. The ripple outlives the few frames a
press actually lasts, which is what makes a click legible at normal playback
speed. Disable with `ShowCursor = false`, resize with `CursorScale`.

## Templates

The `Templates/` directory contains TextAsset templates scaffolded by `Initialize Project`: `package.json`, `tsconfig.json`, `esbuild.config.mjs`, `index.tsx`, `main.uss`, `global.d.ts`, `gitignore` (written as `.gitignore`) and `AGENTS.md`. Notes on three of them:

- `esbuild.config.mjs.txt` uses `format: "iife"` with `globalName: "__exports"` (not ESM). This is required for `onPlay()`/`onStop()` lifecycle hook support. QuickJS evaluates in global scope where ESM `export {}` would be a syntax error.
- `global.d.ts.txt` declares runtime globals (`__root`, `__isPlaying`, `__eventAPI`, etc.)
- `AGENTS.md.txt` is scaffolded into the working dir as `AGENTS.md`: a condensed guide (commands, rules, interop quick reference) for AI coding agents working on the user's app. Keep it in sync with the repo-root `AGENTS.md`.

## TypeGenerator

Generates TypeScript declaration files (`.d.ts`) from C# types. Provides:

- **Interactive UI**: `Tools > OneJS > Type Generator` menu
- **Per-runner typings**: the Build tab's Type Generation section (assemblies, Auto Generate, output path, default `types/csharp.d.ts` in `~/`), regenerated on domain reload and by `Tools > OneJS > Regenerate All Project Typings`
- **Programmatic API**: Static facade, fluent builder, presets

### Quick Start

```csharp
// One-liner
TypeGenerator.Generate("output.d.ts", typeof(Vector3), typeof(GameObject));

// Fluent builder
TypeGenerator.Create()
    .AddType<Vector3>()
    .AddNamespace("UnityEngine.UIElements")
    .Build()
    .WriteTo("output.d.ts");

// Presets
TypeGenerator.Presets.UnityCore.WriteTo("unity-core.d.ts");
```

See [TypeGenerator/README.md](TypeGenerator/README.md) for full documentation.
