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
| `OneJSEditorDesign.cs` | Centralized design tokens (colors, text labels) for editor UIs |
| `DefaultFileEntryDrawer.cs` | Property drawer for default file entries in JSRunner |
| `GlobalEntryDrawer.cs` | Property drawer for global entries in JSRunner |
| `CartridgeFileEntryDrawer.cs` | Property drawer for cartridge file entries |
| `CartridgeObjectEntryDrawer.cs` | Property drawer for cartridge object entries |
| `CodeFieldTestWindow.cs` | Editor window for testing CodeField control |
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

- **Project** - Status section, actions, watcher status, project folder path
- **UI** - Panel Settings reference, scale mode, stylesheets, preloads, globals
- **Cartridges** - UI Cartridge management
- **Build** - Build output, type generation, scaffolding

### Status Section

- **Running/Stopped/Loading indicator** with color-coded labels
- **Reload count** showing number of hot reloads since entering Play mode
- **Watcher status** showing file watcher state (Running, Starting, Idle)
- **Project folder path** (clickable to open in file explorer)

### Actions

- **Reload** - Force reload the JavaScript runtime (works in both Play mode and edit-mode preview)
- **Rebuild** - Delete node_modules, reinstall dependencies, and rebuild
- **Open Folder** - Open working directory in file explorer
- **Open Terminal** - Open terminal at working directory
- **Open in Code Editor** - Open working directory in configured code editor (VSCode, Cursor, etc.)

### Context Menu Options

Right-click the JSRunner component header for additional options:

- **Run in Background** - Toggle whether this JSRunner starts watchers and runs on Play mode
- **Use Scene Name as Root Folder** - Toggle whether the project folder name derives from the scene name (stored in `EditorPrefs`)
- **Dev Mode** - Show the full tabbed UI even without a valid PanelSettings (for debugging)

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
   - `EnsurePanelSettingsAssets()` - Creates/assigns PanelSettings for runners missing one
   - `EnsureProjectsReady()` - Calls `EnsureProjectSetup()` on each valid runner to scaffold files
   - `PrepareWatchers()` - Clears the session tracking set
3. On `EnteredPlayMode`:
   - Finds all active JSRunner components with valid working directories
   - For each: installs dependencies if needed, builds if needed, then starts watcher
4. On `ExitingPlayMode`:
   - `NodeWatcherManager.StopAll()` - Stops ALL running watchers (not just session-started ones), so folders are unlocked for move/rename in Edit mode

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
  - **Build & Run** - Build TSX and run immediately (Play mode)
  - **Build Only** - Build without running
  - **Run** - Execute previously built output
  - **Stop** - Stop execution and clear UI
  - **Open Temp Folder** - Reveal `Temp/OneJSPad/{id}/`
  - **Clean** - Delete temp directory and node_modules

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

- **`Colors`** - Color palette (surfaces, borders, text, status indicators, buttons, per-editor overrides)
- **`Texts`** - Repeated string labels (status, actions, tabs, section headers, empty states, watcher labels)

All editor scripts (JSRunnerEditor, JSPadEditor, UICartridgeEditor) reference these tokens instead of hardcoding colors or text strings. This ensures visual consistency and makes theme changes a single-file edit.

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
