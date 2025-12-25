# OneJS Editor

Editor scripts for OneJS Unity integration.

## Files

| File | Purpose |
|------|---------|
| `JSRunnerEditor.cs` | Custom inspector for JSRunner component |
| `JSPadEditor.cs` | Custom inspector for JSPad inline runner |
| `JSRunnerBuildProcessor.cs` | Build hook for auto-copying JS bundles |
| `TypeGenerator/` | Code generation tools |

## JSRunnerEditor

Custom inspector providing:
- **Status section**: Running/stopped indicator, reload count, entry file status
- **Actions**:
  - **Reload Now** - Force reload (Play mode only)
  - **Build** - Run `npm run build` in working directory
  - **Open Folder** - Open working directory in file explorer
  - **Open Terminal** - Open terminal at working directory
- **Build info**: Shows where bundle will be copied during build

### npm Path Detection

On macOS/Linux, Unity doesn't inherit terminal PATH. The editor searches for npm in:
1. `/usr/local/bin/npm` (Homebrew Intel)
2. `/opt/homebrew/bin/npm` (Homebrew Apple Silicon)
3. `~/.nvm/versions/node/*/bin/npm` (nvm)
4. Fallback: `bash -l -c "which npm"`

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
5. Executes built `@outputs/app.js` if in Play mode

## JSRunnerBuildProcessor

Implements `IPreprocessBuildWithReport` to auto-copy JS bundles before build:

1. Scans all enabled build scenes for JSRunner components
2. For each JSRunner without an embedded TextAsset:
   - Copies entry file to `StreamingAssets/{streamingAssetsPath}`
   - Default: `StreamingAssets/onejs/app.js`
3. Logs status during build

This ensures JS bundles are included in builds without manual steps.

### Skipping Auto-Copy

To skip auto-copy for a specific JSRunner:
- Assign a TextAsset to the `Embedded Script` field
- The build processor will use that instead
