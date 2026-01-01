# Build Validation

End-to-end testing for standalone builds.

## Overview

This system validates that OneJS works correctly in standalone builds by:

1. Building a test player with `BuildValidationRunner`
2. Running the executable and capturing output
3. Parsing `[BUILD_TEST]` log entries for pass/fail results
4. Asserting all tests pass

## Assembly Isolation

`BuildValidationRunner` always compiles but its functionality is wrapped in `#if ONEJS_BUILD_VALIDATION`. This ensures:

1. **The class always exists** - can be added to scenes and referenced
2. **Functionality only runs in test builds** - the test uses `BuildPlayerOptions.extraScriptingDefines` to enable it
3. **User builds are unaffected** - the component exists but does nothing

## Setup

### 1. Create Test Scene

Create `BuildValidationScene.unity` in this folder with:

1. **Empty scene** (or default camera/light)
2. **GameObject** with:
   - `JSRunner` component configured with a working directory
   - `BuildValidationRunner` component (this folder)

### 2. Configure JSRunner

The JSRunner should have:
- `Working Dir`: Point to a valid JS project (e.g., `App`)
- `Entry File`: The built JS bundle path
- `Streaming Assets Path`: Path for build deployment

### 3. Run the Test

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

1. **StreamingAssets Path**: Verifies the path exists
2. **JS Bundle**: Checks for `.js` files in `StreamingAssets/onejs/`
3. **Package Assets**: Looks for `@namespace/` folders in assets
4. **JSRunner Execution**: Verifies the JS runtime works:
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
