# OneJS Editor

Editor scripts for OneJS Unity integration.

## Files

| File | Purpose |
|------|---------|
| `JSRunnerEditor.cs` | Custom inspector for JSRunner component |
| `JSRunnerAutoWatch.cs` | Auto-starts file watchers on Play mode entry |
| `JSPadEditor.cs` | Custom inspector for JSPad inline runner |
| `JSRunnerBuildProcessor.cs` | Build hook for auto-copying JS bundles |
| `TypeGenerator/` | TypeScript declaration generator (see [TypeGenerator/README.md](TypeGenerator/README.md)) |

## JSRunnerEditor

Custom inspector providing:
- **Status section**: Running/stopped indicator, reload count, working directory path
- **Actions**:
  - **Refresh** - Force reload the JavaScript runtime (Play mode only)
  - **Rebuild** - Delete node_modules, reinstall dependencies, and rebuild
  - **Open Folder** - Open working directory in file explorer
  - **Open Terminal** - Open terminal at working directory
  - **Open VSCode** - Open working directory in Visual Studio Code
- **Watcher status**: Shows auto-watch state (auto-starts on Play mode)

### npm Path Detection

On macOS/Linux, Unity doesn't inherit terminal PATH. The editor searches for npm in:
1. `/usr/local/bin/npm` (Homebrew Intel)
2. `/opt/homebrew/bin/npm` (Homebrew Apple Silicon)
3. `~/.nvm/versions/node/*/bin/npm` (nvm)
4. Fallback: `bash -l -c "which npm"`

## JSRunnerAutoWatch

Automatically manages file watchers for JSRunner instances when entering/exiting Play mode.

### Features

- **Auto-start on Play**: Watchers start automatically when entering Play mode
- **Auto-install**: Runs `npm install` if `node_modules/` is missing before starting watcher
- **Auto-stop on Exit**: Watchers that were auto-started are stopped when exiting Play mode
- **Status display**: Inspector shows watcher state (Running, Starting, Auto-starts on Play mode)

### How It Works

1. Uses `[InitializeOnLoad]` to register `playModeStateChanged` callback
2. On `EnteredPlayMode`: Finds all active JSRunner components
3. For each runner with a valid working directory:
   - If `node_modules/` missing: runs `npm install` first
   - Starts the file watcher via `NodeWatcherManager`
4. On `ExitingPlayMode`: Stops all watchers that were started this session

### Integration with Inspector

The inspector's watcher status label shows:
- **Running**: Watcher is active and auto-rebuilding on file changes
- **Starting...**: npm install or watcher startup in progress
- **Auto-starts on Play mode**: Not in Play mode, will start automatically

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
