# Build Validation

End-to-end testing for standalone builds.

## Overview

This system validates that OneJS works correctly in standalone builds by:

1. Building a test player with `BuildValidationRunner`
2. Running the executable and capturing output
3. Parsing `[BUILD_TEST]` log entries for pass/fail results
4. Asserting all tests pass

## Assembly Isolation

The asmdef carries the define constraint `ONEJS_BUILD_VALIDATION || UNITY_EDITOR`, and `BuildValidationRunner`'s functionality is additionally wrapped in `#if ONEJS_BUILD_VALIDATION`. This ensures:

1. **The class exists in the editor**: can be added to scenes and referenced while authoring
2. **Functionality only runs in validation builds**: the test uses `BuildPlayerOptions.extraScriptingDefines` to enable it, which also satisfies the constraint so the assembly compiles into that build
3. **User builds ship no test code at all**: without the define, the assembly is not compiled into a player, so `OneJS.BuildValidation.dll` never appears in `Managed/` (verified with a real standalone build)

## Setup

The scene and its project already exist in this folder: `BuildValidationScene.unity`
holds a GameObject with `JSRunner` + `BuildValidationRunner`, and `TestApp/` is the
runner's project (its `PanelSettings.asset` is the project marker; `app.js.txt` beside
it is a tiny hand-written bundle that sets `globalThis.__buildValidationBundleRan`).

If the wiring is ever lost, regenerate it headless with the project closed:

```
unity run . -- -executeMethod OneJS.Tests.Editor.BuildValidationSceneSetup.Configure
```

### Run the Test

In Unity Test Runner (Window > General > Test Runner):
1. Select **EditMode** tab
2. Find `BuildValidationTests`
3. Right-click and select **Run**

> **Note**: The test is marked `[Explicit]` because it's slow (30-60 seconds).

## Test Results Format

The `BuildValidationRunner` outputs results in this format:

```
[BUILD_TEST] PASS: Description of what passed
[BUILD_TEST] FAIL: Description of what failed
[BUILD_TEST] SKIP: Description of what was skipped (not a failure)
```

## What Gets Tested

1. **StreamingAssets Path**: informational (SKIP when absent; the folder only exists
   when the app ships package assets, since the bundle travels as a TextAsset)
2. **Package Assets**: Looks for `@namespace/` folders in assets
3. **JSRunner Execution**: Verifies the JS runtime works:
   - JSRunner running
   - bundle TextAsset assigned by the build preprocessor
   - the deployed bundle executed (`__buildValidationBundleRan` set by `TestApp/app.js.txt`)
   - `__root` global accessible
   - `__bridge` global accessible
   - `CS` proxy available

## Manual Testing

To run the built player manually:

```bash
# macOS
./BuildTest.app/Contents/MacOS/BuildTest -logFile output.log -batchmode

# Windows
BuildTest.exe -logFile output.log -batchmode

# Then check output.log for [BUILD_TEST] lines
```

> **Note**: `-nographics` is omitted because UI Toolkit requires a graphics context.

## Troubleshooting

**Build fails**: Check that all required scenes/assets are included.

**No test results**:
- Ensure `BuildValidationRunner.Start()` is being called (scene is active)
- Verify `ONEJS_BUILD_VALIDATION` define was added (check build log)
- The 30-second global timeout should force exit if tests hang

**Tests timeout**: Increase `RUN_TIMEOUT_MS` in `BuildValidationTests.cs`.

**BuildValidationRunner not found**: The test automatically adds the `ONEJS_BUILD_VALIDATION` define. If running manually, add this define to Player Settings > Scripting Define Symbols.
