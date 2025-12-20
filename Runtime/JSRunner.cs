using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// MonoBehaviour that runs JavaScript from a file path under the project root.
/// Requires a UIDocument component on the same GameObject for UI rendering.
///
/// Platform behavior:
/// - Editor/Standalone: Loads JS from filesystem (App/@outputs/esbuild/app.js)
/// - WebGL: Loads from StreamingAssets or uses embedded TextAsset
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class JSRunner : MonoBehaviour {
    const string DefaultEntryContent = "console.log(\"Hola, OneJS!\");\n";

    [SerializeField] string _workingDir = "App";
    [SerializeField] string _entryFile = "@outputs/esbuild/app.js";

    [Header("WebGL")]
    [Tooltip("For WebGL: TextAsset containing the bundled JS. If set, this is used instead of loading from StreamingAssets.")]
    [SerializeField] TextAsset _embeddedScript;
    [Tooltip("For WebGL: Path relative to StreamingAssets (e.g., 'app.js'). Used if Embedded Script is not set.")]
    [SerializeField] string _streamingAssetsPath = "app.js";

    QuickJSUIBridge _bridge;
    UIDocument _uiDocument;
    bool _scriptLoaded;

    public string WorkingDir {
        get => _workingDir;
        set => _workingDir = value;
    }

    public string EntryFile {
        get => _entryFile;
        set => _entryFile = value;
    }

    public QuickJSUIBridge Bridge => _bridge;

    string ProjectRoot {
        get {
#if UNITY_EDITOR
            return Path.GetDirectoryName(Application.dataPath);
#else
            return Application.dataPath;
#endif
        }
    }

    string WorkingDirFullPath => Path.Combine(ProjectRoot, _workingDir);
    string EntryFileFullPath => Path.Combine(WorkingDirFullPath, _entryFile);

    void Start() {
        try {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null || _uiDocument.rootVisualElement == null) {
                Debug.LogError("[JSRunner] UIDocument or rootVisualElement is null");
                return;
            }

            _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement, WorkingDirFullPath);

            // Inject platform defines before any user code runs
            InjectPlatformDefines();

            // Expose the root element to JS as globalThis.__root
            var rootHandle = QuickJSNative.RegisterObject(_uiDocument.rootVisualElement);
            _bridge.Eval($"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");

            // Expose the bridge to JS for USS loading
            var bridgeHandle = QuickJSNative.RegisterObject(_bridge);
            _bridge.Eval($"globalThis.__bridge = __csHelpers.wrapObject('QuickJSUIBridge', {bridgeHandle})");

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: Load from embedded TextAsset or StreamingAssets
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
#else
            // Editor/Standalone: Load from filesystem
            var entryPath = EntryFileFullPath;
            var entryDir = Path.GetDirectoryName(entryPath);

            if (!Directory.Exists(entryDir)) {
                Directory.CreateDirectory(entryDir);
            }

            string code;
            if (File.Exists(entryPath)) {
                code = File.ReadAllText(entryPath);
            } else {
                code = DefaultEntryContent;
                File.WriteAllText(entryPath, code);
                Debug.Log($"[JSRunner] Created default entry file at: {entryPath}");
            }

            RunScript(code, _entryFile);
#endif
        } catch (System.Exception ex) {
            Debug.LogError($"[JSRunner] Start() exception: {ex}");
        }
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

#if UNITY_WEBGL && !UNITY_EDITOR
    void StartWebGLTick() {
        _bridge.Eval("if (typeof __startWebGLTick === 'function') __startWebGLTick();", "webgl-tick-start.js");
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
        }
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    void StartJSFetch() {
        _fetchStarted = true;
        var url = Path.Combine(Application.streamingAssetsPath, _streamingAssetsPath);
        Debug.Log($"[JSRunner] Fetching from: {url}");

        // Use JavaScript's native fetch API via the bridge
        // Store the callback in a global that JS can call
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
        } catch (System.Exception ex) {
            Debug.LogError($"[JSRunner] Fetch eval error: {ex}");
            // Set the error in JS so polling can pick it up
            try {
                _bridge.Eval($"globalThis.__fetchError = '{ex.Message.Replace("'", "\\'")}'");
            } catch { }
        }
    }

    void PollFetchResult() {
        try {
            // Check for error first
            var errorCheck = _bridge.Eval("globalThis.__fetchError || ''");
            if (!string.IsNullOrEmpty(errorCheck)) {
                Debug.LogError($"[JSRunner] Fetch failed: {errorCheck}");
                _bridge.Eval("globalThis.__fetchError = null");
                RunScript(DefaultEntryContent, "default.js");
                return;
            }

            // Check for success - execute the script directly in JS to avoid buffer limits
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
                    // Start the native RAF tick loop
                    StartWebGLTick();
                } else if (result.StartsWith("error:")) {
                    var error = result.Substring(6);
                    Debug.LogError($"[JSRunner] Script execution failed: {error}");
                    RunScript(DefaultEntryContent, "default.js");
                }
            }
        } catch (System.Exception ex) {
            Debug.LogError($"[JSRunner] Poll fetch error: {ex}");
        }
    }
#endif

    void OnDestroy() {
        _bridge?.Dispose();
        _bridge = null;
    }
}
