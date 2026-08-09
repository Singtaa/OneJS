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
| `NodeWatcherManager.cs` | Manages Node.js file watcher processes for live reload |
| `OneJSProcessUtils.cs` | Process helpers (tree-kill on Windows via `taskkill /T /F`, Unix via `pgrep -P`) |
| `OneJSEditorDesign.cs` | Centralized design tokens (colors, text labels) for editor UIs |
| `DefaultFileEntryDrawer.cs` | Property drawer for default file entries in JSRunner |
| `GlobalEntryDrawer.cs` | Property drawer for global entries in JSRunner |
| `CartridgeFileEntryDrawer.cs` | Property drawer for cartridge file entries |
| `CartridgeObjectEntryDrawer.cs` | Property drawer for cartridge object entries |
| `CodeFieldTestWindow.cs` | Editor window for testing CodeField control |
| `Recording/PanelRecorder.cs` | Renders a running JSRunner's UI to an mp4 by frame-stepping it on `VirtualClock` (see below) |
| `Recording/OffscreenPanelRenderer.cs` | Draws a PanelSettings' panel into an offscreen RenderTexture at an exact size |
| `Recording/FfmpegLocator.cs` | Locates ffmpeg (well-known paths, then login shell) |
| `TypeGenerator/` | TypeScript declaration generator (see [TypeGenerator/README.md](TypeGenerator/README.md)) |

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

When fully initialized, the inspector shows four tabs:

- **Project**: Status section, actions, watcher status, project folder path
- **UI**: Panel Settings reference, scale mode, stylesheets, preloads, globals
- **Cartridges**: UI Cartridge management
- **Build**: Build output, type generation, scaffolding

### Status Section

- **Running/Stopped/Loading indicator** with color-coded labels
- **Reload count** showing number of hot reloads since entering Play mode
- **Watcher status** showing file watcher state (Running, Starting, Idle)
- **Project folder path** (clickable to open in file explorer)

### Actions

- **Reload**: Force reload the JavaScript runtime (works in both Play mode and edit-mode preview)
- **Rebuild**: Delete node_modules, reinstall dependencies, and rebuild
- **Open Folder**: Open working directory in file explorer
- **Open Terminal**: Open terminal at working directory
- **Open in Code Editor**: Open working directory in configured code editor (VSCode, Cursor, etc.)

### Context Menu Options

Right-click the JSRunner component header for additional options:

- **Run in Background**: Toggle whether this JSRunner starts watchers and runs on Play mode
- **Use Scene Name as Root Folder**: Toggle whether the project folder name derives from the scene name (stored in `EditorPrefs`)
- **Dev Mode**: Show the full tabbed UI even without a valid PanelSettings (for debugging)

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

- **PanelSettings auto-creation**: Creates PanelSettings assets for JSRunners that don't have one before entering Play mode
- **Project scaffolding**: Ensures project files are scaffolded (`EnsureProjectSetup()`) before Play mode
- **Auto-install + build**: Runs `npm install` and `npm run build` if needed before starting watcher
- **Auto-start on Play**: Watchers start automatically when entering Play mode
- **Auto-stop on Exit**: All watchers are stopped when exiting Play mode (via `NodeWatcherManager.StopAll()`)

### Play Mode Lifecycle

1. Uses `[InitializeOnLoad]` to register `playModeStateChanged` callback
2. On `ExitingEditMode` (before Play starts):
   - `EnsurePanelSettingsAssets()`: Creates/assigns PanelSettings for runners missing one
   - `EnsureProjectsReady()`: Calls `EnsureProjectSetup()` on each valid runner to scaffold files
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
- **Status section**: Current state (building, running, ready)
- **Actions**:
  - **Build & Run**: Build TSX and run immediately (Play mode)
  - **Build Only**: Build without running
  - **Run**: Execute previously built output
  - **Stop**: Stop execution and clear UI
  - **Open Temp Folder**: Reveal `Temp/OneJSPad/{id}/`
  - **Clean**: Delete temp directory and node_modules

### Build Process
1. On first build, creates temp directory with package.json, tsconfig.json, esbuild.config.mjs
2. Runs `npm install` if node_modules missing (~10s)
3. Writes source code to `index.tsx`
4. Runs `npm run build` (esbuild)
5. Executes built output if in Play mode

### Static Initialization (`[InitializeOnLoad]`)

JSPadEditor uses `[InitializeOnLoad]` with a static constructor to register a global `playModeStateChanged` callback. This ensures all JSPad instances are built before entering Play mode, regardless of which object is selected in the hierarchy.

**Flow**:
1. Static constructor registers `OnPlayModeStateChangedStatic`
2. On `ExitingEditMode`, `BuildAllJSPadsSync()` finds all JSPad components
3. Each JSPad is built synchronously (npm install if needed, then esbuild)
4. Bundle and source map are saved to serialized fields
5. Scene is saved to persist data for standalone builds
6. On `EnteredPlayMode`, JSPad.Start() runs the serialized bundle

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

Implements `IPreprocessBuildWithReport` to handle TextAssets for builds:

1. Scans all enabled build scenes for JSRunner components
2. For each JSRunner without a bundle TextAsset assigned:
   - The bundle at `{InstanceFolder}/app.js.txt` (esbuild output) is already there
   - Loads it as a TextAsset and assigns to the JSRunner component
   - Loads source map TextAsset if `Include Source Map` is enabled
   - Saves modified scenes
3. Extracts Cartridge files to `{WorkingDir}/@cartridges/{slug}/`
4. Logs status during build

Since esbuild outputs directly to `app.js.txt`, the build processor just needs to load the existing file as a TextAsset.

### Skipping Auto-Assignment

To skip auto-assignment for a specific JSRunner:
- Pre-assign a TextAsset to the `Bundle Asset` field in the inspector
- The build processor will skip processing for that JSRunner

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

The `Templates/` directory contains TextAsset templates scaffolded by `Initialize Project`:

- `esbuild.config.mjs.txt` uses `format: "iife"` with `globalName: "__exports"` (not ESM). This is required for `onPlay()`/`onStop()` lifecycle hook support. QuickJS evaluates in global scope where ESM `export {}` would be a syntax error.
- `global.d.ts.txt` declares runtime globals (`__root`, `__isPlaying`, `__eventAPI`, etc.)
- `AGENTS.md.txt` is scaffolded into the working dir as `AGENTS.md`: a condensed guide (commands, rules, interop quick reference) for AI coding agents working on the user's app. Keep it in sync with the repo-root `AGENTS.md`.

## TypeGenerator

Generates TypeScript declaration files (`.d.ts`) from C# types. Provides:

- **Interactive UI**: `OneJS > Type Generator` menu
- **Quick menu items**: `OneJS > Generate Typings > ...`
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
