# OneJS Tests

PlayMode and EditMode tests for the OneJS runtime. Run via Unity Test Runner (Window > General > Test Runner).

## Test Structure

```
Tests/
├── OneJS.Tests.asmdef           # PlayMode test assembly (runtime *.cs at this level)
├── Editor/                      # EditMode test assembly (OneJS.Tests.Editor.asmdef)
├── BuildValidation/             # Standalone player build test (see its README)
├── Fixtures/                    # PanelHost, text fixtures in Resources/, CustomElement~ React fixture (see its README)
└── Resources/TestShaders/       # SimpleCompute.compute for the GPU tests
```

## Running Tests

1. Open Unity Test Runner: `Window > General > Test Runner`
2. Select `PlayMode` or `EditMode` tab
3. Click `Run All` or select specific tests

## Test Files

PlayMode (`Tests/*.cs`):

| File | Purpose |
|------|---------|
| `QuickJSPlaymodeTests.cs` | Core eval, static calls, constructors, generics, async, callbacks, array marshaling |
| `QuickJSInteropPlaymodeTests.cs` | Proxy caching, property change detection, collection access through the bootstrap proxy |
| `QuickJSFastPathPlaymodeTests.cs` | Zero-allocation fast path: correctness, allocations, performance |
| `QuickJSZeroAllocTests.cs` | Zero-allocation bindings, GPU bindings, property ID caching (doubles as documentation) |
| `QuickJSCallbackBindingTests.cs` | C# to JS calls: Func wrappers, array returns, `GetJSFunction`, stale handles across reloads |
| `QuickJSExtensionDispatchTests.cs` | Extension methods with omitted optional arguments (`element.Q("name")`) |
| `QuickJSTypeResolutionTests.cs` | CS paths to types, nested types, unresolvable paths |
| `QuickJSMultiContextTests.cs` | Two live contexts at once (two JSRunners) |
| `QuickJSUIBridgePlaymodeTests.cs` | Event delegation, scheduling, Promises |
| `QuickJSSchedulerTests.cs` | Bounded scheduler passes and the WebGL timer teardown contract |
| `QuickJSBootstrapScopeTests.cs` | Global-scope contract: install-if-missing polyfills, IIFE non-leakage (WebGL host-page safety) |
| `QuickJSStabilityTests.cs` | Handle and task queue monitoring, buffer overflow detection |
| `QuickJSNetworkTests.cs` | `fetch`, against a loopback HttpListener (hermetic) |
| `QuickJSWebSocketTests.cs` | `WebSocket`, against the live `ws.postman-echo.com` echo service |
| `QuickJSAbortTests.cs` | `AbortController`/`AbortSignal` polyfill |
| `QuickJSStorageTests.cs` | `localStorage` over PlayerPrefs |
| `QuickJSFileSystemTests.cs` | FileSystem API: read, write, exists, delete, list |
| `QuickJSAssetLoaderTests.cs` | `loadResourceAsync` |
| `QuickJSURLTests.cs` | `URL` and `URLSearchParams` |
| `QuickJSBase64Tests.cs` | `atob`/`btoa` |
| `UIToolkitJSPlaymodeTests.cs` | Element creation, properties, styles, hierarchy from JS |
| `CustomElementPlaymodeTests.cs` | `registerElement`/`createComponent` end to end, using the prebuilt `TestCustomElement` fixture |
| `ControlAlignmentPlaymodeTests.cs` | Each control's visible part centred in its box, so an `align-items: center` row lines up |
| `TreeViewBridgeTests.cs` | `TreeViewBridge` parallel-array contract (mirrors onejs-react's `treeview.test.tsx`) |
| `ShaderFXTests.cs` | ShaderFX render-target lifecycle against real layout, uniform marshalling, ramp/texture caching |
| `ParticleTests.cs` | Particle wire parsing, deterministic simulation, imperative API, render smoke tests |
| `Physics2DBodyTests.cs` | How a wire body becomes a Rigidbody2D: no engine warnings on build, density driving mass |
| `GPUBridgePlaymodeTests.cs` | GPU compute shaders, buffers, dispatch, from C# and JS |
| `ProcPlaymodeTests.cs` | Procedural noise and texture generators through QuickJS |
| `JsLogSeverityPlaymodeTests.cs` | Console severity end to end through the real bootstrap and native callback |
| `JSRunnerEventSystemPlaymodeTests.cs` | The EventSystem JSRunner creates, per input backend |
| `JSPadPlaymodeTests.cs` | JSPad temp dirs, build state, execution |
| `CartridgeUtilsPlaymodeTests.cs` | Cartridge global injection, platform defines, `__cart()` API |
| `JSRunnerPlaymodeTests.cs` | A single `Assert.Pass` placeholder; the real JSRunner tests are disabled pending a scene-based rewrite |

EditMode (`Tests/Editor/`):

| File | Purpose |
|------|---------|
| `JSRunnerBuildProcessorTests.cs` | Asset copying, namespace detection |
| `JSRunnerBuildProcessorWindowsTests.cs` | Read-only file handling; Windows only |
| `JSRunnerBuildSceneListTests.cs` | Which scenes a build walks |
| `JSRunnerBundleAssignmentTests.cs` | Which runners come out of a build with a bundle |
| `JSRunnerPrefabAppTests.cs` | Apps a build reaches through a prefab, and Exclude From Build |
| `JSRunnerInspectorListTests.cs` | The X button in the Stylesheets, Preloads, Globals and Cartridges lists removes the row clicked, after an undo and after another inspector changed the list |
| `JSRunnerScaffoldOnceTests.cs` | Default files are written once: a new app, an app with a record, an app from before the record, a deleted package.json or record, two runners on one folder |
| `JSRunnerDefaultFilesTests.cs` | Initialize keeps a customized list, a missing default file is named with Restore, Template newer status, the Scaffolding list has no X, the scaffolded .gitignore keeps `.onejs/` |
| `JSRunnerInitializeReportingTests.cs` | What Initialize Project reports when it cannot create anything |
| `JSRunnerUIDocumentOwnershipTests.cs` | Which UIDocument JSRunner may remove |
| `CartridgeUtilsTests.cs` | String escaping, path calculation, file extraction, stylesheets |
| `PremadeCartridgeTests.cs` | The shipped `Assets/Singtaa/Premade/` cartridge **assets**: metadata completeness, path-safe slugs, unique identities, resolvable payloads, extraction round-trip |
| `EventIdContractTests.cs` | Event type ids agree between `QuickJSUIBridge.cs` and the bootstrap |
| `StructSerializationTests.cs` | The JSON a data-only struct becomes in JS |
| `StyleBridgeTests.cs` | One warning per unknown style key |
| `UssCompilerDiagnosticsTests.cs` | Diagnostics for typo'd USS properties, and the reflected property table |
| `JsLogSeverityTests.cs` | Splitting and routing of the console level the bootstrap encodes |
| `InputBridgeNamingTests.cs` | The retired `GetKeyDown` names stay deprecated |
| `AISkillContractTests.cs` | Shipped AI Skills' frontmatter versions match `package.json` |
| `BuildValidationTests.cs` | Full build and run of a standalone player (`[Explicit]`, slow) |
| `BuildValidationSceneSetup.cs` | Not a test: regenerates `BuildValidationScene`'s wiring (`-executeMethod`) |

## Test Categories

### JSPad Tests (JSPadPlaymodeTests)
- **Temp Directory**: Instance ID generation, config file creation
- **Source Code**: Index.tsx writing, content preservation
- **Build State**: State transitions, output detection
- **Execution**: Script execution, stop/cleanup

### CartridgeUtils Tests (CartridgeUtilsTests + CartridgeUtilsPlaymodeTests)
- **String Escaping**: JS string literal escaping for special characters
- **Path Calculation**: Cartridge path resolution with/without namespaces
- **File Extraction**: Folder creation, `.d.ts` generation, overwrite behavior, return value
- **Stylesheet Application**: USS stylesheet application to root elements
- **Cartridge Globals** (PlayMode): `__cart()` function, plain-object injection with individual object entries as properties, platform defines

### Build Processor Tests (JSRunnerBuildProcessorTests)
- **File Copying**: Recursive copy, content preservation
- **Asset Detection**: `@namespace/` folder detection
- **Scoped Packages**: `@scope/package` handling
- **Deduplication**: Multiple JSRunner handling

### Build Scene List Tests (JSRunnerBuildSceneListTests)

Which scenes the build processor walks: a passed `BuildPlayerOptions.scenes` wins over Build Settings, is spent after one read, and falls back to the enabled Build Settings scenes when a build passes none.

These cover the selection only. That Unity calls `PrepareForBuild` before `OnPreprocessBuild` and hands it the real list is not reachable from EditMode, since nothing here drives a `BuildPlayerContext`; only a player build shows it. A green run is not evidence of that half.

### Zero-Alloc Tests (QuickJSZeroAllocTests)

Tests and documentation for the zero-allocation interop system:

- **Binding Registration**: Basic zero-arg, multi-arg, and return-value bindings
- **Specialized GPU Bindings**: BindGpuSetFloatById, BindGpuSetVectorById, BindGpuDispatch
- **Property ID Caching**: Shader.PropertyToID pattern for zero-alloc shader uniforms
- **GPUBridge Integration**: GetZeroAllocBindingIds, SetFloatById, ID-based setters
- **JavaScript Integration**: Accessing binding IDs and PropertyToID from JS
- **Performance**: InvokeCallbackNoAlloc overhead, per-frame GPU update simulation

The test file also serves as comprehensive documentation with examples.

### Array Marshaling Tests (QuickJSPlaymodeTests)

Tests for automatic JS → C# array conversion:

- **TypedArray to C# array**: `Float32Array` → `float[]`, `Int32Array` → `int[]`
- **JS array to float/int array**: `[1, 2, 3]` → `int[]` or `float[]`
- **Object arrays to Vector3[]**: `[{ x, y, z }, ...]` → `Vector3[]`
- **Tuple arrays to Vector3[]**: `[[x, y, z], ...]` → `Vector3[]`
- **Color arrays**: `[{ r, g, b, a }, ...]` or `[[r,g,b,a], ...]` → `Color[]`
- **String arrays**: `["a", "b"]` → `string[]`
- **Empty arrays**: Typed vs untyped behavior
- **Large arrays**: Performance with 1000+ elements

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

See [Fixtures/README.md](Fixtures/README.md) for the list.

## Writing New Tests

### PlayMode Test Pattern

There is no JSRunner test helper: most fixtures drive a `QuickJSContext` (or a `QuickJSUIBridge` over a runtime-created `UIDocument`) directly, as `UIToolkitJSPlaymodeTests` does:

```csharp
[UnitySetUp]
public IEnumerator SetUp() {
    _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
    _go = new GameObject("TestHost");
    _go.AddComponent<UIDocument>().panelSettings = _panelSettings;
    yield return null; // let UIDocument initialize
    _ctx = new QuickJSContext();
}

[UnityTest]
public IEnumerator Creation_VisualElement_Works() {
    var result = _ctx.Eval("new CS.UnityEngine.UIElements.VisualElement().toString()");
    StringAssert.Contains("VisualElement", result);
    yield return null;
}

[UnityTearDown]
public IEnumerator TearDown() {
    _ctx?.Dispose();
    Object.Destroy(_go);
    Object.Destroy(_panelSettings);
    QuickJSNative.ClearAllHandles();
    yield return null;
}
```

Prefer inline `const string` scripts; a test that needs a real laid-out panel uses `Fixtures/PanelHost.cs`.

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

## Build Validation

`BuildValidation/BuildValidationScene.unity` and its `TestApp/` already exist. Run `BuildValidationTests` from the EditMode tab (it is `[Explicit]` because it builds and runs a real player). See [BuildValidation/README.md](BuildValidation/README.md).
