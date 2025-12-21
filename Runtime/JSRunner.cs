using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// MonoBehaviour that runs JavaScript from a file path under the project root.
/// Requires a UIDocument component on the same GameObject for UI rendering.
///
/// Features:
/// - Auto-scaffolding: Creates project structure on first run
/// - Live Reload: Polls entry file for changes and hot-reloads
/// - Hard Reload: Disposes context and recreates fresh on file change
///
/// Platform behavior:
/// - Editor/Standalone: Loads JS from filesystem with live reload support
/// - WebGL: Loads from StreamingAssets or uses embedded TextAsset (no live reload)
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class JSRunner : MonoBehaviour {
    const string DefaultEntryContent = "console.log(\"OneJS is good to go!\");\n";

    [SerializeField] string _workingDir = "App";
    [SerializeField] string _entryFile = "@outputs/esbuild/app.js";

    [Header("Live Reload")]
    [Tooltip("Automatically reload when the entry file changes (Editor/Standalone only)")]
    [SerializeField] bool _liveReload = true;
    [Tooltip("How often to check for file changes (in seconds)")]
    [SerializeField] float _pollInterval = 0.5f;

    [Header("WebGL")]
    [Tooltip("For WebGL: TextAsset containing the bundled JS. If set, this is used instead of loading from StreamingAssets.")]
    [SerializeField] TextAsset _embeddedScript;
    [Tooltip("For WebGL: Path relative to StreamingAssets (e.g., 'app.js'). Used if Embedded Script is not set.")]
    [SerializeField] string _streamingAssetsPath = "app.js";

    QuickJSUIBridge _bridge;
    UIDocument _uiDocument;
    bool _scriptLoaded;

    // Live reload state
    DateTime _lastModifiedTime;
    float _nextPollTime;
    int _reloadCount;

    // Public API
    public string WorkingDir {
        get => _workingDir;
        set => _workingDir = value;
    }

    public string EntryFile {
        get => _entryFile;
        set => _entryFile = value;
    }

    public QuickJSUIBridge Bridge => _bridge;
    public bool IsRunning => _scriptLoaded && _bridge != null;
    public bool IsLiveReloadEnabled => _liveReload;
    public int ReloadCount => _reloadCount;
    public DateTime LastModifiedTime => _lastModifiedTime;

    public string ProjectRoot {
        get {
#if UNITY_EDITOR
            return Path.GetDirectoryName(Application.dataPath);
#else
            return Application.dataPath;
#endif
        }
    }

    public string WorkingDirFullPath => Path.Combine(ProjectRoot, _workingDir);
    public string EntryFileFullPath => Path.Combine(WorkingDirFullPath, _entryFile);

    void Start() {
        try {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null || _uiDocument.rootVisualElement == null) {
                Debug.LogError("[JSRunner] UIDocument or rootVisualElement is null");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            InitializeWebGL();
#else
            InitializeEditor();
#endif
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Start() exception: {ex}");
        }
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    void InitializeEditor() {
        // Check if we need to scaffold the project
        if (!Directory.Exists(WorkingDirFullPath) || IsDirectoryEmpty(WorkingDirFullPath)) {
            ScaffoldProject();
        }

        // Ensure entry directory exists
        var entryDir = Path.GetDirectoryName(EntryFileFullPath);
        if (!Directory.Exists(entryDir)) {
            Directory.CreateDirectory(entryDir);
        }

        // Create default entry file if missing
        if (!File.Exists(EntryFileFullPath)) {
            File.WriteAllText(EntryFileFullPath, DefaultEntryContent);
            Debug.Log($"[JSRunner] Created default entry file at: {EntryFileFullPath}");
        }

        // Initialize bridge and run script
        InitializeBridge();
        var code = File.ReadAllText(EntryFileFullPath);
        RunScript(code, _entryFile);

        // Initialize file watching
        if (_liveReload) {
            _lastModifiedTime = File.GetLastWriteTime(EntryFileFullPath);
            _nextPollTime = Time.realtimeSinceStartup + _pollInterval;
        }
    }

    bool IsDirectoryEmpty(string path) {
        return Directory.GetFiles(path).Length == 0 && Directory.GetDirectories(path).Length == 0;
    }

    void ScaffoldProject() {
        Debug.Log($"[JSRunner] Scaffolding new project at: {WorkingDirFullPath}");

        // Create working directory
        Directory.CreateDirectory(WorkingDirFullPath);

        // Create @outputs/esbuild directory
        var outputDir = Path.Combine(WorkingDirFullPath, "@outputs", "esbuild");
        Directory.CreateDirectory(outputDir);

        // Create styles directory
        var stylesDir = Path.Combine(WorkingDirFullPath, "styles");
        Directory.CreateDirectory(stylesDir);

        // Write template files
        WriteTemplateFile("package.json", GetPackageJsonTemplate());
        WriteTemplateFile("esbuild.config.mjs", GetEsbuildConfigTemplate());
        WriteTemplateFile("tsconfig.json", GetTsConfigTemplate());
        WriteTemplateFile("global.d.ts", GetGlobalDtsTemplate());
        WriteTemplateFile("index.tsx", GetIndexTsxTemplate());
        WriteTemplateFile("styles/main.uss", GetMainUssTemplate());

        // Create default entry file
        var entryPath = Path.Combine(outputDir, "app.js");
        File.WriteAllText(entryPath, DefaultEntryContent);

        Debug.Log("[JSRunner] Project scaffolded successfully!");
        Debug.Log("[JSRunner] Next steps:");
        Debug.Log($"  1. cd {WorkingDirFullPath}");
        Debug.Log("  2. npm install");
        Debug.Log("  3. npm run build");
    }

    void WriteTemplateFile(string relativePath, string content) {
        var fullPath = Path.Combine(WorkingDirFullPath, relativePath);
        File.WriteAllText(fullPath, content);
    }
#endif

    void InitializeBridge() {
        _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement, WorkingDirFullPath);

        // Inject platform defines before any user code runs
        InjectPlatformDefines();

        // Expose the root element to JS as globalThis.__root
        var rootHandle = QuickJSNative.RegisterObject(_uiDocument.rootVisualElement);
        _bridge.Eval($"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");

        // Expose the bridge to JS for USS loading
        var bridgeHandle = QuickJSNative.RegisterObject(_bridge);
        _bridge.Eval($"globalThis.__bridge = __csHelpers.wrapObject('QuickJSUIBridge', {bridgeHandle})");
    }

    void RunScript(string code, string filename) {
        _bridge.Eval(code, filename);
        // Execute pending Promise jobs immediately to allow React's first render
        _bridge.Context.ExecutePendingJobs();
        _scriptLoaded = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Start the native RAF tick loop for WebGL
        StartWebGLTick();
#endif
    }

    /// <summary>
    /// Force a reload of the JavaScript context.
    /// Disposes the current context, clears UI, and recreates everything.
    /// </summary>
    public void ForceReload() {
#if !UNITY_WEBGL || UNITY_EDITOR
        Reload();
#else
        Debug.LogWarning("[JSRunner] Live reload is not supported on WebGL");
#endif
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    void Reload() {
        if (!File.Exists(EntryFileFullPath)) {
            Debug.LogWarning($"[JSRunner] Entry file not found: {EntryFileFullPath}");
            return;
        }

        try {
            // 1. Clear UI
            _uiDocument.rootVisualElement.Clear();

            // 2. Dispose old bridge/context
            _bridge?.Dispose();
            _bridge = null;
            _scriptLoaded = false;

            // 3. Recreate bridge and globals
            InitializeBridge();

            // 4. Load and run script
            var code = File.ReadAllText(EntryFileFullPath);
            RunScript(code, _entryFile);

            // 5. Update state
            _lastModifiedTime = File.GetLastWriteTime(EntryFileFullPath);
            _reloadCount++;

            Debug.Log($"[JSRunner] Reloaded ({_reloadCount}): {_entryFile}");
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Reload failed: {ex.Message}");
        }
    }

    void CheckForFileChanges() {
        if (!_liveReload || !_scriptLoaded) return;
        if (Time.realtimeSinceStartup < _nextPollTime) return;

        _nextPollTime = Time.realtimeSinceStartup + _pollInterval;

        try {
            if (!File.Exists(EntryFileFullPath)) return;

            var currentModTime = File.GetLastWriteTime(EntryFileFullPath);
            if (currentModTime != _lastModifiedTime) {
                Reload();
            }
        } catch (IOException) {
            // File might be locked by build process, skip this poll
        } catch (Exception ex) {
            Debug.LogWarning($"[JSRunner] Error checking file: {ex.Message}");
        }
    }
#endif

    /// <summary>
    /// Inject Unity platform defines as JavaScript globals.
    /// These can be used for conditional code: if (UNITY_WEBGL) { ... }
    /// </summary>
    void InjectPlatformDefines() {
        var defines = new System.Text.StringBuilder();
        defines.AppendLine("// Unity Platform Defines");

        // Platform flags
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

#if UNITY_STANDALONE
        defines.AppendLine("globalThis.UNITY_STANDALONE = true;");
#else
        defines.AppendLine("globalThis.UNITY_STANDALONE = false;");
#endif

#if UNITY_STANDALONE_OSX
        defines.AppendLine("globalThis.UNITY_STANDALONE_OSX = true;");
#else
        defines.AppendLine("globalThis.UNITY_STANDALONE_OSX = false;");
#endif

#if UNITY_STANDALONE_WIN
        defines.AppendLine("globalThis.UNITY_STANDALONE_WIN = true;");
#else
        defines.AppendLine("globalThis.UNITY_STANDALONE_WIN = false;");
#endif

#if UNITY_STANDALONE_LINUX
        defines.AppendLine("globalThis.UNITY_STANDALONE_LINUX = true;");
#else
        defines.AppendLine("globalThis.UNITY_STANDALONE_LINUX = false;");
#endif

#if UNITY_IOS
        defines.AppendLine("globalThis.UNITY_IOS = true;");
#else
        defines.AppendLine("globalThis.UNITY_IOS = false;");
#endif

#if UNITY_ANDROID
        defines.AppendLine("globalThis.UNITY_ANDROID = true;");
#else
        defines.AppendLine("globalThis.UNITY_ANDROID = false;");
#endif

#if DEBUG || DEVELOPMENT_BUILD
        defines.AppendLine("globalThis.DEBUG = true;");
#else
        defines.AppendLine("globalThis.DEBUG = false;");
#endif

        _bridge.Eval(defines.ToString(), "platform-defines.js");
    }

    static int _updateCount = 0;
    bool _loadStarted;
#if UNITY_WEBGL && !UNITY_EDITOR
    bool _fetchStarted;
#endif

    void Update() {
        _updateCount++;

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: Use native fetch for loading, then native RAF loop handles ticking
        if (!_loadStarted && !_scriptLoaded && _embeddedScript == null && !string.IsNullOrEmpty(_streamingAssetsPath)) {
            if (_updateCount >= 3) {
                _loadStarted = true;
                StartJSFetch();
            }
        }

        // Check if fetch completed by polling JS globals
        if (_fetchStarted && !_scriptLoaded) {
            PollFetchResult();
        }
        // Once script is loaded, native RAF loop handles all ticking - nothing to do here
#else
        // Editor/Standalone: Use Unity's Update loop to drive the tick
        if (_scriptLoaded) {
            _bridge?.Tick();
            CheckForFileChanges();
        }
#endif
    }

    // MARK: WebGL-specific code
#if UNITY_WEBGL && !UNITY_EDITOR
    void InitializeWebGL() {
        InitializeBridge();

        if (_embeddedScript != null) {
            RunScript(_embeddedScript.text, "embedded.js");
        } else if (!string.IsNullOrEmpty(_streamingAssetsPath)) {
            // Loading is deferred to Update and uses browser's native fetch API
        } else {
            // Fallback test script
            RunScript(@"
                console.log('Inline test script running');
                var label = new CS.UnityEngine.UIElements.Label('WebGL Inline Test');
                label.style.fontSize = 32;
                label.style.color = CS.UnityEngine.Color.yellow;
                __root.Add(label);
                console.log('Inline test complete');
            ", "inline-test.js");
        }
    }

    void StartWebGLTick() {
        _bridge.Eval("if (typeof __startWebGLTick === 'function') __startWebGLTick();", "webgl-tick-start.js");
    }

    void StartJSFetch() {
        _fetchStarted = true;
        var url = Path.Combine(Application.streamingAssetsPath, _streamingAssetsPath);
        Debug.Log($"[JSRunner] Fetching from: {url}");

        var fetchCode = $@"
(function() {{
    var url = '{url}';
    console.log('[JSRunner] JS fetch starting for: ' + url);
    fetch(url)
        .then(function(response) {{
            console.log('[JSRunner] fetch response status:', response.status);
            if (!response.ok) throw new Error('HTTP ' + response.status);
            return response.text();
        }})
        .then(function(text) {{
            console.log('[JSRunner] fetch success, length:', text.length);
            globalThis.__fetchedScript = text;
        }})
        .catch(function(err) {{
            console.error('[JSRunner] fetch error:', err);
            globalThis.__fetchError = err.message || String(err);
        }});
}})();
";
        try {
            _bridge.Eval(fetchCode, "fetch-loader.js");
            Debug.Log("[JSRunner] Fetch code executed");
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Fetch eval error: {ex}");
            try {
                _bridge.Eval($"globalThis.__fetchError = '{ex.Message.Replace("'", "\\'")}'");
            } catch { }
        }
    }

    void PollFetchResult() {
        try {
            var errorCheck = _bridge.Eval("globalThis.__fetchError || ''");
            if (!string.IsNullOrEmpty(errorCheck)) {
                Debug.LogError($"[JSRunner] Fetch failed: {errorCheck}");
                _bridge.Eval("globalThis.__fetchError = null");
                RunScript(DefaultEntryContent, "default.js");
                return;
            }

            var result = _bridge.Eval(@"
(function() {
    if (!globalThis.__fetchedScript) return '';
    var script = globalThis.__fetchedScript;
    globalThis.__fetchedScript = null;
    try {
        (0, eval)(script);
        return 'ok';
    } catch (e) {
        console.error('[JSRunner] Script execution error:', e);
        return 'error:' + (e.message || String(e));
    }
})()
");
            if (!string.IsNullOrEmpty(result)) {
                if (result == "ok") {
                    _bridge.Context.ExecutePendingJobs();
                    _scriptLoaded = true;
                    StartWebGLTick();
                } else if (result.StartsWith("error:")) {
                    var error = result.Substring(6);
                    Debug.LogError($"[JSRunner] Script execution failed: {error}");
                    RunScript(DefaultEntryContent, "default.js");
                }
            }
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Poll fetch error: {ex}");
        }
    }
#endif

    void OnDestroy() {
        _bridge?.Dispose();
        _bridge = null;
    }

    // MARK: Template Generators
#if !UNITY_WEBGL || UNITY_EDITOR
    string GetPackageJsonTemplate() {
        return @"{
  ""name"": ""onejs-app"",
  ""version"": ""1.0.0"",
  ""private"": true,
  ""type"": ""module"",
  ""scripts"": {
    ""build"": ""node esbuild.config.mjs"",
    ""watch"": ""node esbuild.config.mjs --watch"",
    ""typecheck"": ""tsc --noEmit""
  },
  ""dependencies"": {
    ""react"": ""^19.0.0"",
    ""onejs-react"": ""file:../JSModules/onejs-react""
  },
  ""devDependencies"": {
    ""@types/react"": ""^19.0.0"",
    ""esbuild"": ""^0.24.0"",
    ""typescript"": ""^5.7.0""
  }
}
";
    }

    string GetEsbuildConfigTemplate() {
        return @"import * as esbuild from 'esbuild';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Resolve react to the App's node_modules to prevent duplicate copies
const reactPath = path.resolve(__dirname, 'node_modules/react');
const reactJsxPath = path.resolve(__dirname, 'node_modules/react/jsx-runtime');
const reactJsxDevPath = path.resolve(__dirname, 'node_modules/react/jsx-dev-runtime');

const isWatch = process.argv.includes('--watch');

const config = {
  entryPoints: ['index.tsx'],
  bundle: true,
  outfile: '@outputs/esbuild/app.js',
  format: 'esm',
  target: 'es2022',
  jsx: 'automatic',
  alias: {
    // Force all react imports to use the App's copy
    'react': reactPath,
    'react/jsx-runtime': reactJsxPath,
    'react/jsx-dev-runtime': reactJsxDevPath,
  },
  // Ensure packages from node_modules are bundled, not externalized
  packages: 'bundle',
};

if (isWatch) {
  const ctx = await esbuild.context(config);
  await ctx.watch();
  console.log('Watching for changes...');
} else {
  await esbuild.build(config);
  console.log('Build complete!');
}
";
    }

    string GetTsConfigTemplate() {
        return @"{
  ""compilerOptions"": {
    ""target"": ""ES2022"",
    ""lib"": [""ES2022""],
    ""module"": ""ESNext"",
    ""moduleResolution"": ""Bundler"",

    ""allowJs"": true,
    ""checkJs"": false,
    ""noEmit"": true,

    ""strict"": true,
    ""skipLibCheck"": true,

    ""isolatedModules"": true,
    ""verbatimModuleSyntax"": true,
    ""esModuleInterop"": true,
    ""resolveJsonModule"": true,
    ""forceConsistentCasingInFileNames"": true,

    ""jsx"": ""react-jsx"",
    ""baseUrl"": ""."",
    ""paths"": {
      ""onejs-react"": [""../JSModules/onejs-react/src""]
    }
  },
  ""include"": [""**/*"", ""global.d.ts""],
  ""exclude"": [""node_modules"", ""@outputs""]
}
";
    }

    string GetGlobalDtsTemplate() {
        return @"// OneJS globals provided by QuickJSBootstrap.js

declare const CS: {
  UnityEngine: {
    Debug: {
      Log: (message: string) => void;
    };
    UIElements: {
      VisualElement: new () => CSObject;
      Label: new () => CSObject;
      Button: new () => CSObject;
      TextField: new () => CSObject;
      Toggle: new () => CSObject;
      Slider: new () => CSObject;
      ScrollView: new () => CSObject;
      Image: new () => CSObject;
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
  Insert: (index: number, child: CSObject) => void;
  Remove: (child: CSObject) => void;
  RemoveAt: (index: number) => void;
  IndexOf: (child: CSObject) => number;
  Clear: () => void;
  style: Record<string, unknown>;
  text?: string;
  value?: unknown;
  label?: string;
  AddToClassList: (className: string) => void;
  RemoveFromClassList: (className: string) => void;
  ClearClassList: () => void;
}

// Console (provided by QuickJS)
declare const console: {
  log: (...args: unknown[]) => void;
  error: (...args: unknown[]) => void;
  warn: (...args: unknown[]) => void;
};

// Timers (provided by QuickJSBootstrap.js)
declare function setTimeout(callback: () => void, ms?: number): number;
declare function clearTimeout(id: number): void;
declare function setInterval(callback: () => void, ms?: number): number;
declare function clearInterval(id: number): void;
declare function requestAnimationFrame(callback: (timestamp: number) => void): number;
declare function cancelAnimationFrame(id: number): void;
declare function queueMicrotask(callback: () => void): void;

declare const performance: {
  now: () => number;
};

// StyleSheet API
declare function loadStyleSheet(path: string): boolean;
declare function compileStyleSheet(ussContent: string, name?: string): boolean;
";
    }

    string GetIndexTsxTemplate() {
        return @"import { useState } from ""react""
import { render, View, Label, Button } from ""onejs-react""

// Load USS stylesheet
loadStyleSheet(""styles/main.uss"")

function App() {
    const [count, setCount] = useState(0)

    return (
        <View className=""container"">
            <Label className=""title"" text=""Hello from OneJS!"" />
            <Label className=""counter"" text={`Count: ${count}`} />
            <Button
                className=""button""
                text=""Click me!""
                onClick={() => setCount(c => c + 1)}
            />
        </View>
    )
}

render(<App />, __root)
";
    }

    string GetMainUssTemplate() {
        return @".container {
    flex-grow: 1;
    padding: 20px;
    background-color: #1a1a2e;
}

.title {
    font-size: 32px;
    color: #eee;
    margin-bottom: 20px;
}

.counter {
    color: #a0a0a0;
    font-size: 18px;
    margin-bottom: 10px;
}

.button {
    background-color: #e94560;
    padding: 12px 24px;
    border-radius: 8px;
    margin: 5px;
}

.button:hover {
    background-color: #ff6b6b;
}

.button:active {
    background-color: #c73e54;
}
";
    }
#endif
}
