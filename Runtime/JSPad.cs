using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A simple inline JavaScript/TSX runner with no external working directory.
/// Write TSX directly in the inspector, hit Build & Run to execute.
///
/// Uses a temp directory (Temp/OneJSPad/) for build artifacts.
/// No live-reload - manual Build & Run only.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class JSPad : MonoBehaviour {
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

    QuickJSUIBridge _bridge;
    UIDocument _uiDocument;
    bool _scriptLoaded;
    bool _initialized;

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
    public bool HasBuiltOutput => File.Exists(OutputFile);

    void Start() {
        _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument == null || _uiDocument.rootVisualElement == null) {
            Debug.LogError("[JSPad] UIDocument or rootVisualElement is null");
            return;
        }

        // Auto-run if we have a built output
        if (HasBuiltOutput) {
            RunBuiltScript();
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
        if (_initialized && Directory.Exists(TempDir)) return;

        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(Path.Combine(TempDir, "@outputs"));

        // Write package.json
        var packageJson = GetPackageJsonContent();
        File.WriteAllText(Path.Combine(TempDir, "package.json"), packageJson);

        // Write tsconfig.json
        var tsConfig = GetTsConfigContent();
        File.WriteAllText(Path.Combine(TempDir, "tsconfig.json"), tsConfig);

        // Write esbuild.config.mjs
        var esbuildConfig = GetEsbuildConfigContent();
        File.WriteAllText(Path.Combine(TempDir, "esbuild.config.mjs"), esbuildConfig);

        // Write global.d.ts
        var globalDts = GetGlobalDtsContent();
        File.WriteAllText(Path.Combine(TempDir, "global.d.ts"), globalDts);

        _initialized = true;
        Debug.Log($"[JSPad] Initialized temp directory: {TempDir}");
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
    /// Run the built script (if available).
    /// </summary>
    public void RunBuiltScript() {
        if (!HasBuiltOutput) {
            Debug.LogWarning("[JSPad] No built output found. Build first.");
            return;
        }

        try {
            // Stop any existing execution
            Stop();

            // Initialize bridge
            _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement, TempDir);
            InjectPlatformDefines();

            // Expose root element
            var rootHandle = QuickJSNative.RegisterObject(_uiDocument.rootVisualElement);
            _bridge.Eval($"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");

            // Expose bridge
            var bridgeHandle = QuickJSNative.RegisterObject(_bridge);
            _bridge.Eval($"globalThis.__bridge = __csHelpers.wrapObject('QuickJSUIBridge', {bridgeHandle})");

            // Load and run script
            var code = File.ReadAllText(OutputFile);
            _bridge.Eval(code, "app.js");
            _bridge.Context.ExecutePendingJobs();
            _scriptLoaded = true;

            Debug.Log("[JSPad] Script running");
        } catch (Exception ex) {
            Debug.LogError($"[JSPad] Run error: {ex.Message}");
            Stop();
        }
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
        var defines = new System.Text.StringBuilder();
        defines.AppendLine("// Unity Platform Defines");

#if UNITY_EDITOR
        defines.AppendLine("globalThis.UNITY_EDITOR = true;");
#else
        defines.AppendLine("globalThis.UNITY_EDITOR = false;");
#endif

#if UNITY_WEBGL
        defines.AppendLine("globalThis.UNITY_WEBGL = true;");
#else
        defines.AppendLine("globalThis.UNITY_WEBGL = false;");
#endif

#if DEBUG || DEVELOPMENT_BUILD
        defines.AppendLine("globalThis.DEBUG = true;");
#else
        defines.AppendLine("globalThis.DEBUG = false;");
#endif

        _bridge.Eval(defines.ToString(), "platform-defines.js");
    }

    string GetPackageJsonContent() {
        // Get the path to JSModules/onejs-react relative to Temp/OneJSPad/{id}
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        var oneJsReactPath = Path.Combine(projectRoot, "JSModules", "onejs-react");
        var relativePath = GetRelativePath(TempDir, oneJsReactPath).Replace("\\", "/");

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
    ""onejs-react"": ""file:{relativePath}""
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
}
