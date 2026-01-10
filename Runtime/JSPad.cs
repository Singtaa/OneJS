using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OneJS;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A module dependency entry for JSPad.
/// </summary>
[Serializable]
public class JSPadModuleEntry {
    [Tooltip("npm package name (e.g., 'lodash', '@types/lodash')")]
    public string name;
    [Tooltip("Version specifier (e.g., '^4.17.0', 'latest'). Leave empty for latest.")]
    public string version;
}

/// <summary>
/// A simple inline JavaScript/TSX runner for rapid prototyping.
/// Write TSX directly in the inspector, build, and reload to execute.
///
/// Uses a temp directory (Temp/OneJSPad/) for build artifacts.
/// No live-reload - manual Build and Reload workflow.
///
/// Workflow:
/// 1. Build: npm install (if needed) + esbuild → stores bundle in serialized field
/// 2. Reload: Evaluates the stored bundle (PlayMode only)
///
/// The bundle is automatically evaluated on OnEnable in PlayMode.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class JSPad : MonoBehaviour, ISerializationCallbackReceiver {
    const string DefaultSourceCode = @"import { useState } from ""react""
import { render, View, Label, Button } from ""onejs-react""

function App() {
    const [count, setCount] = useState(0)

    return (
        <View style={{ padding: 20, backgroundColor: ""#1a1a2e"" }}>
            <Label
                style={{ fontSize: 24, color: ""#eee"", marginBottom: 20 }}
                text=""Hello from JSPad!""
            />
            <Label
                style={{ fontSize: 18, color: ""#a0a0a0"", marginBottom: 10 }}
                text={`Count: ${count}`}
            />
            <Button
                style={{
                    backgroundColor: ""#e94560"",
                    paddingTop: 12, paddingBottom: 12,
                    paddingLeft: 24, paddingRight: 24
                }}
                text=""Click me!""
                onClick={() => setCount(c => c + 1)}
            />
        </View>
    )
}

render(<App />, __root)
";

    [SerializeField, TextArea(15, 30)]
    string _sourceCode = DefaultSourceCode;

    [SerializeField, HideInInspector]
    string _instanceId;

    [Tooltip("Embedded PanelSettings for the UIDocument. Edit directly in the Settings > UI tab.")]
    [SerializeField, HideInInspector] PanelSettings _panelSettings;

    [Tooltip("UI Cartridges to load. Files are extracted to temp directory, objects are injected as __cartridges.{slug}.{key}.")]
    [SerializeField] List<UICartridge> _cartridges = new List<UICartridge>();

    [Tooltip("Additional npm modules to include in the build. These are added to package.json dependencies.")]
    [SerializeField] List<JSPadModuleEntry> _modules = new List<JSPadModuleEntry>();

    [Tooltip("USS StyleSheets to apply. Applied in order after script runs.")]
    [SerializeField] List<StyleSheet> _stylesheets = new List<StyleSheet>();

    [Tooltip("Default files to create in TempDir. Loaded from OneJS Editor/Templates.")]
    [SerializeField] List<DefaultFileEntry> _defaultFiles = new List<DefaultFileEntry>();

    // Serialized build output for standalone players (no npm/node available)
    [SerializeField, HideInInspector] string _builtBundle;
    [SerializeField, HideInInspector] string _builtSourceMap;

    QuickJSUIBridge _bridge;
    UIDocument _uiDocument;
    bool _scriptLoaded;
    bool _tempDirInitialized;
    bool _startCalled;

    // Build state (used by editor)
    public enum BuildState {
        Idle,
        InstallingDeps,
        Building,
        Ready,
        Error
    }

    BuildState _buildState = BuildState.Idle;
    string _lastBuildError;
    string _lastBuildOutput;

    // Public API
    public string SourceCode {
        get => _sourceCode;
        set => _sourceCode = value;
    }

    public QuickJSUIBridge Bridge => _bridge;
    public bool IsRunning => _scriptLoaded && _bridge != null;
    public BuildState CurrentBuildState => _buildState;
    public string LastBuildError => _lastBuildError;
    public string LastBuildOutput => _lastBuildOutput;

    public string TempDir {
        get {
            if (string.IsNullOrEmpty(_instanceId)) {
                _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            return Path.Combine(Application.dataPath, "..", "Temp", "OneJSPad", _instanceId);
        }
    }

    public string OutputFile => Path.Combine(TempDir, "@outputs", "app.js");
    public string SourceMapFile => OutputFile + ".map";

    /// <summary>
    /// Returns true if there's a built bundle available in the serialized field.
    /// </summary>
    public bool HasBuiltBundle => !string.IsNullOrEmpty(_builtBundle);

    /// <summary>
    /// Size of the built bundle in bytes (0 if empty).
    /// </summary>
    public int BundleSize => _builtBundle?.Length ?? 0;

    /// <summary>
    /// The serialized bundle string (for standalone builds).
    /// </summary>
    public string BuiltBundle => _builtBundle;

    /// <summary>
    /// The serialized source map string (for standalone builds).
    /// </summary>
    public string BuiltSourceMap => _builtSourceMap;

    /// <summary>
    /// Saves the current build output to serialized fields for standalone use.
    /// Call this from the editor after a successful build.
    /// </summary>
    public void SaveBundleToSerializedFields() {
#if UNITY_EDITOR
        if (File.Exists(OutputFile)) {
            _builtBundle = File.ReadAllText(OutputFile);
        }
        if (File.Exists(SourceMapFile)) {
            _builtSourceMap = File.ReadAllText(SourceMapFile);
        }
        UnityEditor.EditorUtility.SetDirty(this);

        // Save the scene immediately so the bundle persists for standalone builds
        if (!Application.isPlaying && gameObject.scene.IsValid()) {
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(gameObject.scene);
        }
#endif
    }

    /// <summary>
    /// Clears the serialized bundle fields.
    /// </summary>
    public void ClearSerializedBundle() {
        _builtBundle = null;
        _builtSourceMap = null;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // Cartridge API
    public IReadOnlyList<UICartridge> Cartridges => _cartridges;

    // Modules API
    public IReadOnlyList<JSPadModuleEntry> Modules => _modules;

    // Stylesheets API
    public IReadOnlyList<StyleSheet> Stylesheets => _stylesheets;

    /// <summary>
    /// The embedded PanelSettings. Edit via inspector or access programmatically.
    /// </summary>
    public PanelSettings EmbeddedPanelSettings {
        get {
            EnsureEmbeddedPanelSettings();
            return _panelSettings;
        }
    }

    public string GetCartridgePath(UICartridge cartridge) {
        return CartridgeUtils.GetCartridgePath(TempDir, cartridge);
    }

    void OnEnable() {
        // Get UIDocument (guaranteed by RequireComponent)
        _uiDocument = GetComponent<UIDocument>();

        // Ensure PanelSettings is assigned
        EnsurePanelSettings();

        // Auto-reload on re-enable (after Start has been called once)
        // On re-enable, rootVisualElement is already ready since panel was created before
        if (_startCalled && Application.isPlaying && HasBuiltBundle) {
            Reload();
        }
    }

    void Start() {
        _startCalled = true;

        // Auto-reload in PlayMode if we have a bundle
        // Using Start() for first load ensures UIDocument's rootVisualElement is ready
        if (Application.isPlaying && HasBuiltBundle) {
            Reload();
        }
    }

    void OnDisable() {
        Stop();
    }

    /// <summary>
    /// Ensures UIDocument has a PanelSettings assigned.
    /// Uses the embedded PanelSettings.
    /// </summary>
    void EnsurePanelSettings() {
        if (_uiDocument.panelSettings != null) return;

        EnsureEmbeddedPanelSettings();
        _uiDocument.panelSettings = _panelSettings;
    }

    /// <summary>
    /// Ensures the embedded PanelSettings exists and is assigned to UIDocument.
    /// Creates one if missing.
    /// </summary>
    void EnsureEmbeddedPanelSettings() {
        if (_panelSettings == null) {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.name = "Embedded PanelSettings";
            _panelSettings.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

            // Set sensible defaults
            _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            _panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panelSettings.match = 0.5f;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // Assign to UIDocument if available and not already set
        var uiDoc = GetComponent<UIDocument>();
        if (uiDoc != null && uiDoc.panelSettings == null) {
            uiDoc.panelSettings = _panelSettings;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(uiDoc);
#endif
        }
    }

    void Update() {
        if (_scriptLoaded) {
            _bridge?.Tick();
        }
    }

    void OnDestroy() {
        Stop();
    }

    /// <summary>
    /// Initialize the temp directory with required files.
    /// Called by the editor before building.
    /// </summary>
    public void EnsureTempDirectory() {
        if (_tempDirInitialized && Directory.Exists(TempDir)) return;

        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(Path.Combine(TempDir, "@outputs"));

        // Write package.json (always dynamic - needs _modules list)
        var packageJson = GetPackageJsonContent();
        File.WriteAllText(Path.Combine(TempDir, "package.json"), packageJson);

        // Write tsconfig.json (from template if available)
        var tsconfigEntry = _defaultFiles.Find(e => e.path == "tsconfig.json");
        if (tsconfigEntry?.content != null) {
            File.WriteAllText(Path.Combine(TempDir, "tsconfig.json"), tsconfigEntry.content.text);
        } else {
            File.WriteAllText(Path.Combine(TempDir, "tsconfig.json"), GetTsConfigContent());
        }

        // Write esbuild.config.mjs
        var esbuildConfig = GetEsbuildConfigContent();
        File.WriteAllText(Path.Combine(TempDir, "esbuild.config.mjs"), esbuildConfig);

        // Write global.d.ts (from template if available)
        var globalDtsEntry = _defaultFiles.Find(e => e.path == "global.d.ts");
        if (globalDtsEntry?.content != null) {
            File.WriteAllText(Path.Combine(TempDir, "global.d.ts"), globalDtsEntry.content.text);
        } else {
            File.WriteAllText(Path.Combine(TempDir, "global.d.ts"), GetGlobalDtsContent());
        }

        _tempDirInitialized = true;
    }

    /// <summary>
    /// Write the source code to the temp directory.
    /// Called by the editor before building.
    /// </summary>
    public void WriteSourceFile() {
        EnsureTempDirectory();
        var indexPath = Path.Combine(TempDir, "index.tsx");
        File.WriteAllText(indexPath, _sourceCode);
    }

    /// <summary>
    /// Reload the UI by evaluating the stored bundle.
    /// Only works in PlayMode when there's a built bundle available.
    /// </summary>
    public void Reload() {
        if (!Application.isPlaying) {
            Debug.LogWarning("[JSPad] Reload only works in PlayMode.");
            return;
        }

        if (!HasBuiltBundle) {
            Debug.LogWarning("[JSPad] No bundle found. Build first.");
            return;
        }

        if (_uiDocument == null || _uiDocument.rootVisualElement == null) {
            Debug.LogError("[JSPad] UIDocument or rootVisualElement is null.");
            return;
        }

        try {
            // Stop any existing execution
            Stop();

            // Initialize bridge
#if UNITY_EDITOR
            _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement, TempDir);
#else
            // In standalone, use persistent data path (bundle is self-contained, no file access needed)
            _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement, Application.persistentDataPath);
#endif
            InjectPlatformDefines();

            // Expose root element
            var rootHandle = QuickJSNative.RegisterObject(_uiDocument.rootVisualElement);
            _bridge.Eval($"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");

            // Expose bridge
            var bridgeHandle = QuickJSNative.RegisterObject(_bridge);
            _bridge.Eval($"globalThis.__bridge = __csHelpers.wrapObject('QuickJSUIBridge', {bridgeHandle})");

            // Inject cartridge objects
            InjectCartridgeGlobals();

            // Apply stylesheets
            ApplyStylesheets();

            // Evaluate the stored bundle
            _bridge.Eval(_builtBundle, "app.js");
            _bridge.Context.ExecutePendingJobs();
            _scriptLoaded = true;
        } catch (Exception ex) {
            var message = TranslateErrorMessage(ex.Message);
            Debug.LogError($"[JSPad] Reload error: {message}");
            Stop();
        }
    }

    string TranslateErrorMessage(string message) {
        if (string.IsNullOrEmpty(message)) return message;

#if UNITY_EDITOR
        var parser = SourceMapParser.Load(SourceMapFile);
#else
        var parser = !string.IsNullOrEmpty(_builtSourceMap) ? SourceMapParser.Parse(_builtSourceMap) : null;
#endif
        if (parser == null) return message;

        return parser.TranslateStackTrace(message);
    }

    /// <summary>
    /// Stop execution and clear UI.
    /// </summary>
    public void Stop() {
        if (_bridge != null) {
            _uiDocument?.rootVisualElement?.Clear();
            _bridge.Dispose();
            _bridge = null;
        }
        _scriptLoaded = false;
    }

    /// <summary>
    /// Set build state (called by editor).
    /// </summary>
    public void SetBuildState(BuildState state, string output = null, string error = null) {
        _buildState = state;
        _lastBuildOutput = output;
        _lastBuildError = error;
    }

    /// <summary>
    /// Check if node_modules exists in temp directory.
    /// </summary>
    public bool HasNodeModules() {
        return Directory.Exists(Path.Combine(TempDir, "node_modules"));
    }

    void InjectPlatformDefines() {
        CartridgeUtils.InjectPlatformDefines(_bridge);
    }

    /// <summary>
    /// Extract cartridge files to TempDir/@cartridges/{slug}/.
    /// Called before building.
    /// </summary>
    public void ExtractCartridges() {
        CartridgeUtils.ExtractCartridges(TempDir, _cartridges, overwriteExisting: true);
    }

    /// <summary>
    /// Apply USS StyleSheets to the root visual element.
    /// </summary>
    void ApplyStylesheets() {
        CartridgeUtils.ApplyStylesheets(_uiDocument.rootVisualElement, _stylesheets);
    }

    /// <summary>
    /// Inject Unity objects from cartridges as JavaScript globals under __cartridges namespace.
    /// Access pattern: __cartridges.{slug}.{key}
    /// </summary>
    void InjectCartridgeGlobals() {
        CartridgeUtils.InjectCartridgeGlobals(_bridge, _cartridges);
    }

    string GetPackageJsonContent() {
        // Get the path to JSModules/onejs-react relative to Temp/OneJSPad/{id}
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        var oneJsReactPath = Path.Combine(projectRoot, "JSModules", "onejs-react");
        var relativePath = GetRelativePath(TempDir, oneJsReactPath).Replace("\\", "/");

        // Build additional dependencies from _modules list
        var additionalDeps = new StringBuilder();
        foreach (var module in _modules) {
            if (string.IsNullOrEmpty(module.name)) continue;
            var version = string.IsNullOrEmpty(module.version) ? "latest" : module.version;
            additionalDeps.Append($",\n    \"{module.name}\": \"{version}\"");
        }

        return $@"{{
  ""name"": ""jspad-temp"",
  ""version"": ""1.0.0"",
  ""private"": true,
  ""type"": ""module"",
  ""scripts"": {{
    ""build"": ""node esbuild.config.mjs""
  }},
  ""dependencies"": {{
    ""react"": ""^19.0.0"",
    ""onejs-react"": ""file:{relativePath}""{additionalDeps}
  }},
  ""devDependencies"": {{
    ""@types/react"": ""^19.0.0"",
    ""esbuild"": ""^0.24.0"",
    ""typescript"": ""^5.7.0""
  }}
}}
";
    }

    string GetTsConfigContent() {
        return @"{
  ""compilerOptions"": {
    ""target"": ""ES2022"",
    ""lib"": [""ES2022""],
    ""module"": ""ESNext"",
    ""moduleResolution"": ""Bundler"",
    ""allowJs"": true,
    ""noEmit"": true,
    ""strict"": true,
    ""skipLibCheck"": true,
    ""jsx"": ""react-jsx""
  },
  ""include"": [""**/*"", ""global.d.ts""]
}
";
    }

    string GetEsbuildConfigContent() {
        return @"import * as esbuild from 'esbuild';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const reactPath = path.resolve(__dirname, 'node_modules/react');
const reactJsxPath = path.resolve(__dirname, 'node_modules/react/jsx-runtime');
const reactJsxDevPath = path.resolve(__dirname, 'node_modules/react/jsx-dev-runtime');

await esbuild.build({
  entryPoints: ['index.tsx'],
  bundle: true,
  outfile: '@outputs/app.js',
  format: 'esm',
  target: 'es2022',
  jsx: 'automatic',
  sourcemap: true,
  alias: {
    'react': reactPath,
    'react/jsx-runtime': reactJsxPath,
    'react/jsx-dev-runtime': reactJsxDevPath,
  },
  packages: 'bundle',
});

console.log('Build complete!');
";
    }

    string GetGlobalDtsContent() {
        return @"declare const CS: {
  UnityEngine: {
    Debug: { Log: (message: string) => void };
    UIElements: {
      VisualElement: new () => CSObject;
      Label: new () => CSObject;
      Button: new () => CSObject;
      TextField: new () => CSObject;
      Toggle: new () => CSObject;
    };
  };
};

declare const __root: CSObject;

declare const __eventAPI: {
  addEventListener: (element: CSObject, eventType: string, callback: Function) => void;
  removeEventListener: (element: CSObject, eventType: string, callback: Function) => void;
  removeAllEventListeners: (element: CSObject) => void;
};

declare const __csHelpers: {
  newObject: (typeName: string, ...args: unknown[]) => CSObject;
  callMethod: (obj: CSObject, methodName: string, ...args: unknown[]) => unknown;
  callStatic: (typeName: string, methodName: string, ...args: unknown[]) => unknown;
  wrapObject: (typeName: string, handle: number) => CSObject;
  releaseObject: (obj: CSObject) => void;
};

interface CSObject {
  __csHandle: number;
  __csType: string;
  Add: (child: CSObject) => void;
  Remove: (child: CSObject) => void;
  Clear: () => void;
  style: Record<string, unknown>;
  text?: string;
  value?: unknown;
}

declare const console: {
  log: (...args: unknown[]) => void;
  error: (...args: unknown[]) => void;
  warn: (...args: unknown[]) => void;
};

declare function setTimeout(callback: () => void, ms?: number): number;
declare function clearTimeout(id: number): void;
declare function setInterval(callback: () => void, ms?: number): number;
declare function clearInterval(id: number): void;
declare function requestAnimationFrame(callback: (timestamp: number) => void): number;
declare function cancelAnimationFrame(id: number): void;

// StyleSheet API
declare function loadStyleSheet(path: string): boolean;
declare function compileStyleSheet(ussContent: string, name?: string): boolean;
declare function removeStyleSheet(name: string): boolean;
declare function clearStyleSheets(): number;

// FileSystem API - Path globals
declare const __persistentDataPath: string;
declare const __streamingAssetsPath: string;
declare const __dataPath: string;
declare const __temporaryCachePath: string;

// FileSystem API - Functions
declare function readTextFile(path: string): Promise<string>;
declare function writeTextFile(path: string, content: string): Promise<void>;
declare function fileExists(path: string): boolean;
declare function directoryExists(path: string): boolean;
declare function deleteFile(path: string): boolean;
declare function listFiles(path: string, pattern?: string, recursive?: boolean): string[];
";
    }

    /// <summary>
    /// Get relative path from one directory to another.
    /// </summary>
    static string GetRelativePath(string fromPath, string toPath) {
        var fromUri = new Uri(fromPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var toUri = new Uri(toPath);
        var relativeUri = fromUri.MakeRelativeUri(toUri);
        return Uri.UnescapeDataString(relativeUri.ToString());
    }

    // MARK: ISerializationCallbackReceiver

    public void OnBeforeSerialize() {
        // Ensure embedded PanelSettings exists before serialization
        EnsureEmbeddedPanelSettings();
    }

    public void OnAfterDeserialize() {
        // Nothing needed here - PanelSettings will be deserialized automatically
    }

    // MARK: Editor callbacks

#if UNITY_EDITOR
    void Reset() {
        // Called when component is first added or reset
        EnsureEmbeddedPanelSettings();
        PopulateDefaultFiles();
    }

    void OnValidate() {
        // Ensure PanelSettings exists when values change in inspector
        EnsureEmbeddedPanelSettings();
    }

    /// <summary>
    /// Finds and loads default template files from the OneJS Editor/Templates folder.
    /// Uses PackageInfo to robustly locate the package regardless of installation method.
    /// </summary>
    public void PopulateDefaultFiles() {
        // Template mapping: source file name → target path in TempDir
        // Note: JSPad uses a simpler structure than JSRunner (no types/ subfolder)
        var templateMapping = new (string templateName, string targetPath)[] {
            ("tsconfig.json.txt", "tsconfig.json"),
            ("global.d.ts.txt", "global.d.ts"),
        };

        _defaultFiles.Clear();

        // Find the OneJS package using the assembly that contains JSPad
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(JSPad).Assembly);
        string templatesFolder;

        if (packageInfo != null) {
            // Installed as a package (Packages/com.singtaa.onejs)
            templatesFolder = Path.Combine(packageInfo.assetPath, "Editor/Templates").Replace("\\", "/");
        } else {
            // Fallback: might be in Assets folder (e.g., as submodule)
            // Search for the templates folder
            var guids = UnityEditor.AssetDatabase.FindAssets("package.json t:TextAsset");
            templatesFolder = null;

            foreach (var guid in guids) {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.ToLowerInvariant().Contains("onejs") && path.Contains("Editor/Templates")) {
                    templatesFolder = Path.GetDirectoryName(path);
                    break;
                }
            }

            if (string.IsNullOrEmpty(templatesFolder)) {
                Debug.LogWarning("[JSPad] Could not find OneJS Editor/Templates folder");
                return;
            }
        }

        foreach (var (templateName, targetPath) in templateMapping) {
            var templatePath = Path.Combine(templatesFolder, templateName).Replace("\\", "/");
            var textAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(templatePath);

            if (textAsset != null) {
                _defaultFiles.Add(new DefaultFileEntry {
                    path = targetPath,
                    content = textAsset
                });
            } else {
                Debug.LogWarning($"[JSPad] Template not found: {templatePath}");
            }
        }

        Debug.Log($"[JSPad] Populated {_defaultFiles.Count} default files from templates");
    }
#endif
}
