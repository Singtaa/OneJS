# OneJS Tests

PlayMode and EditMode tests for the OneJS runtime. Run via Unity Test Runner (Window > General > Test Runner).

## Test Structure

```
Tests/
├── OneJS.Tests.asmdef           # PlayMode test assembly
├── QuickJSPlaymodeTests.cs      # Core QuickJS functionality
├── QuickJSFastPathPlaymodeTests.cs
├── QuickJSZeroAllocTests.cs     # Zero-allocation interop tests
├── QuickJSUIBridgePlaymodeTests.cs
├── UIToolkitJSPlaymodeTests.cs
├── QuickJSStabilityTests.cs
├── QuickJSNetworkTests.cs
├── QuickJSStorageTests.cs
├── QuickJSURLTests.cs
├── QuickJSBase64Tests.cs
├── GPUBridgePlaymodeTests.cs
├── JSRunnerPlaymodeTests.cs     # JSRunner MonoBehaviour tests
├── JSPadPlaymodeTests.cs        # JSPad MonoBehaviour tests
├── Editor/                      # EditMode tests
│   ├── OneJS.Tests.Editor.asmdef
│   ├── JSRunnerBuildProcessorTests.cs
│   └── BuildValidationTests.cs
├── BuildValidation/             # Standalone build testing
│   ├── BuildValidationRunner.cs
│   └── README.md
├── Fixtures/                    # Test fixtures (no GUIDs!)
│   ├── README.md
│   └── Resources/
│       ├── SimpleScript.txt
│       ├── UICreation.txt
│       └── EventTest.txt
└── Resources/
    └── TestShaders/
```

## Running Tests

1. Open Unity Test Runner: `Window > General > Test Runner`
2. Select `PlayMode` or `EditMode` tab
3. Click `Run All` or select specific tests

## Test Files

| File | Type | Purpose |
|------|------|---------|
| `QuickJSPlaymodeTests.cs` | PlayMode | Core eval, static calls, constructors, generics, async |
| `QuickJSFastPathPlaymodeTests.cs` | PlayMode | Zero-allocation property access, method calls |
| `QuickJSZeroAllocTests.cs` | PlayMode | Zero-allocation GPU bindings, property ID caching |
| `QuickJSUIBridgePlaymodeTests.cs` | PlayMode | Event delegation, scheduling, Promises |
| `UIToolkitJSPlaymodeTests.cs` | PlayMode | React component rendering |
| `QuickJSStabilityTests.cs` | PlayMode | Handle monitoring, task queue, exceptions |
| `JSRunnerPlaymodeTests.cs` | PlayMode | JSRunner scaffolding, init, reload, globals |
| `JSPadPlaymodeTests.cs` | PlayMode | JSPad temp dirs, build state, execution |
| `GPUBridgePlaymodeTests.cs` | PlayMode | GPU compute shaders, buffers, dispatch |
| `JSRunnerBuildProcessorTests.cs` | EditMode | Asset copying, namespace detection |
| `BuildValidationTests.cs` | EditMode | Full build + run validation (slow) |

## Test Categories

### JSRunner Tests (JSRunnerPlaymodeTests)
- **Scaffolding**: Working directory creation, default files
- **Initialization**: UIDocument/PanelSettings auto-creation
- **Execution**: Entry file execution, globals injection
- **Reload**: Hot reload, UI clearing, state preservation

### JSPad Tests (JSPadPlaymodeTests)
- **Temp Directory**: Instance ID generation, config file creation
- **Source Code**: Index.tsx writing, content preservation
- **Build State**: State transitions, output detection
- **Execution**: Script execution, stop/cleanup

### Build Processor Tests (JSRunnerBuildProcessorTests)
- **File Copying**: Recursive copy, content preservation
- **Asset Detection**: `@namespace/` folder detection
- **Scoped Packages**: `@scope/package` handling
- **Deduplication**: Multiple JSRunner handling

### Zero-Alloc Tests (QuickJSZeroAllocTests)

Tests and documentation for the zero-allocation interop system:

- **Binding Registration**: Basic zero-arg, multi-arg, and return-value bindings
- **Specialized GPU Bindings**: BindGpuSetFloatById, BindGpuSetVectorById, BindGpuDispatch
- **Property ID Caching**: Shader.PropertyToID pattern for zero-alloc shader uniforms
- **GPUBridge Integration**: GetZeroAllocBindingIds, SetFloatById, ID-based setters
- **JavaScript Integration**: Accessing binding IDs and PropertyToID from JS
- **Performance**: InvokeCallbackNoAlloc overhead, per-frame GPU update simulation

The test file also serves as comprehensive documentation with examples.

### Build Validation (BuildValidationTests)
- **End-to-End**: Builds player, runs, validates output
- Marked `[Explicit]` due to 30-60 second runtime

## Test Fixtures

Test fixtures are stored as `.txt` files in `Fixtures/Resources/` to avoid GUID dependencies.

```csharp
// Load fixture
var fixture = Resources.Load<TextAsset>("SimpleScript");
var code = fixture.text;
```

Available fixtures:
- `SimpleScript.txt` - Basic execution validation
- `UICreation.txt` - CS proxy and UI element creation
- `EventTest.txt` - Event registration and dispatch

## Writing New Tests

### PlayMode Test Pattern
```csharp
[UnityTest]
public IEnumerator MyTest_Condition_ExpectedResult() {
    // Arrange
    CreateJSRunner("console.log('test');");

    // Act
    yield return null; // Wait for Start()
    yield return null; // Extra frame for initialization

    // Assert
    Assert.IsTrue(_runner.IsRunning);
}
```

### Using Inline Test Code (Preferred)
```csharp
const string TestScript = @"
globalThis.__result = 'success';
";

CreateJSRunner(TestScript);
```

### Using Fixtures (For Complex Tests)
```csharp
var fixture = Resources.Load<TextAsset>("UICreation");
CreateJSRunner(fixture.text);
```

### Expected Log Messages
```csharp
LogAssert.Expect(LogType.Warning, new Regex(@"pattern"));
LogAssert.Expect(LogType.Error, "exact message");
```

## Test Design Decisions

1. **No GUIDs**: All fixtures are named files, not Unity asset references
2. **Temp Directories**: Tests use `Temp/` to avoid project pollution
3. **Frame Yields**: UIDocument requires frame delays for initialization
4. **Proper Cleanup**: Always dispose bridge, destroy GameObjects, clear handles
5. **Inline Code**: Prefer `const string` for simple test scripts

## Build Validation Setup

To run `BuildValidationTests`:
1. Create `BuildValidation/BuildValidationScene.unity`
2. Add `BuildValidationRunner` component to a GameObject
3. Configure a JSRunner in the scene
4. Add scene to Build Settings
5. Run test from EditMode tab (marked Explicit)

See `BuildValidation/README.md` for details.
