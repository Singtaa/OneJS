# OneJS Editor

Editor scripts for OneJS Unity integration.

## Files

| File | Purpose |
|------|---------|
| `JSRunnerEditor.cs` | Custom inspector for JSRunner component |
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
