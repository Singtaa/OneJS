using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Attribute for configuring how pair fields are displayed in the inspector.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class PairDrawerAttribute : PropertyAttribute {
    public string Separator { get; }

    public PairDrawerAttribute(string separator = "→") {
        Separator = separator;
    }
}

/// <summary>
/// A key-value pair for exposing Unity objects as JavaScript globals.
/// Key is the global variable name, Value is any UnityEngine.Object.
/// </summary>
[Serializable]
public class GlobalEntry {
    [Tooltip("The global variable name (e.g., 'myTexture' becomes globalThis.myTexture)")]
    public string key;
    [Tooltip("Any Unity object to expose to JavaScript")]
    public UnityEngine.Object value;
}

/// <summary>
/// A key-value pair for default files to scaffold.
/// Key is the target path (relative to WorkingDir), Value is the TextAsset content.
/// </summary>
[Serializable]
public class DefaultFileEntry {
    [Tooltip("Target path relative to WorkingDir (e.g., 'index.tsx' or '@outputs/esbuild/app.js')")]
    public string path;
    [Tooltip("TextAsset containing the file content")]
    public TextAsset content;
}

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
/// - Editor: Loads JS from filesystem with live reload support
/// - Builds (Standalone/WebGL/Mobile): Loads from StreamingAssets or embedded TextAsset
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

    [Header("Build Settings")]
    [Tooltip("TextAsset containing the bundled JS. If set, used instead of StreamingAssets in builds.")]
    [SerializeField] TextAsset _embeddedScript;
    [Tooltip("Path relative to StreamingAssets for the JS bundle in builds (auto-copied during build).")]
    [SerializeField] string _streamingAssetsPath = "onejs/app.js";

    [Header("Scaffolding")]
    [Tooltip("Default files to create in WorkingDir if missing. Path is relative to WorkingDir.")]
    [PairDrawer("←")]
    [SerializeField] List<DefaultFileEntry> _defaultFiles = new List<DefaultFileEntry>();

    [Header("Advanced")]
    [Tooltip("USS stylesheets to apply to the root element on init/reload.")]
    [SerializeField] List<StyleSheet> _stylesheets = new List<StyleSheet>();
    [Tooltip("TextAssets to load and evaluate before the entry file. Useful for polyfills or shared libraries.")]
    [SerializeField] List<TextAsset> _preloads = new List<TextAsset>();
    [Tooltip("Unity objects to expose as JavaScript globals (globalThis[key] = value).")]
    [PairDrawer("→")]
    [SerializeField] List<GlobalEntry> _globals = new List<GlobalEntry>();

    QuickJSUIBridge _bridge;
    UIDocument _uiDocument;
    bool _scriptLoaded;

    // Live reload state
    DateTime _lastModifiedTime;
    float _nextPollTime;
    int _reloadCount;

#if UNITY_WEBGL && !UNITY_EDITOR
    // WebGL fetch state machine for safe async loading
    enum WebGLFetchState {
        NotStarted,
        Fetching,
        Ready,      // Script fetched, waiting to execute
        Executed,
        Error
    }
    WebGLFetchState _fetchState = WebGLFetchState.NotStarted;
#endif

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

#if UNITY_EDITOR
            InitializeEditor();
#else
            InitializeBuild();
#endif
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Start() exception: {ex}");
        }
    }

#if UNITY_EDITOR
    void InitializeEditor() {
        // Ensure working directory exists
        if (!Directory.Exists(WorkingDirFullPath)) {
            Directory.CreateDirectory(WorkingDirFullPath);
            Debug.Log($"[JSRunner] Created working directory: {WorkingDirFullPath}");
        }

        // Scaffold any missing default files
        ScaffoldDefaultFiles();

        // Ensure entry file exists (fallback if not in defaultFiles)
        EnsureEntryFile();

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

    /// <summary>
    /// Scaffold any missing default files from the _defaultFiles list.
    /// Existing files are left untouched.
    /// </summary>
    void ScaffoldDefaultFiles() {
        if (_defaultFiles == null || _defaultFiles.Count == 0) return;

        foreach (var entry in _defaultFiles) {
            if (string.IsNullOrEmpty(entry.path) || entry.content == null) continue;

            var fullPath = Path.Combine(WorkingDirFullPath, entry.path);

            // Skip if file already exists
            if (File.Exists(fullPath)) continue;

            // Ensure directory exists
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            // Write file content
            File.WriteAllText(fullPath, entry.content.text);
            Debug.Log($"[JSRunner] Created default file: {entry.path}");
        }
    }

    /// <summary>
    /// Ensure entry file exists. Creates a minimal fallback if missing.
    /// </summary>
    void EnsureEntryFile() {
        if (File.Exists(EntryFileFullPath)) return;

        // Ensure directory exists
        var entryDir = Path.GetDirectoryName(EntryFileFullPath);
        if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir)) {
            Directory.CreateDirectory(entryDir);
        }

        // Create minimal entry file
        File.WriteAllText(EntryFileFullPath, DefaultEntryContent);
        Debug.Log($"[JSRunner] Created default entry file: {_entryFile}");
    }
#endif // UNITY_EDITOR

    void InitializeBridge() {
        _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement, WorkingDirFullPath);

        // Apply stylesheets first so styles are ready when JS runs
        ApplyStylesheets();

        // Inject platform defines before any user code runs
        InjectPlatformDefines();

        // Expose the root element to JS as globalThis.__root
        var rootHandle = QuickJSNative.RegisterObject(_uiDocument.rootVisualElement);
        _bridge.Eval($"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");

        // Expose the bridge to JS for USS loading
        var bridgeHandle = QuickJSNative.RegisterObject(_bridge);
        _bridge.Eval($"globalThis.__bridge = __csHelpers.wrapObject('QuickJSUIBridge', {bridgeHandle})");

        // Inject custom globals
        InjectGlobals();

        // Run preload scripts
        RunPreloads();
    }

    /// <summary>
    /// Apply configured USS stylesheets to the root element.
    /// </summary>
    void ApplyStylesheets() {
        if (_stylesheets == null || _stylesheets.Count == 0) return;

        foreach (var stylesheet in _stylesheets) {
            if (stylesheet == null) continue;
            _uiDocument.rootVisualElement.styleSheets.Add(stylesheet);
        }
    }

    /// <summary>
    /// Inject custom Unity objects as JavaScript globals.
    /// </summary>
    void InjectGlobals() {
        if (_globals == null || _globals.Count == 0) return;

        foreach (var entry in _globals) {
            if (string.IsNullOrEmpty(entry.key) || entry.value == null) continue;

            var handle = QuickJSNative.RegisterObject(entry.value);
            var typeName = entry.value.GetType().FullName;
            _bridge.Eval($"globalThis['{EscapeJsString(entry.key)}'] = __csHelpers.wrapObject('{typeName}', {handle})");
        }
    }

    /// <summary>
    /// Execute preload scripts before the main entry file.
    /// </summary>
    void RunPreloads() {
        if (_preloads == null || _preloads.Count == 0) return;

        foreach (var preload in _preloads) {
            if (preload == null) continue;

            try {
                _bridge.Eval(preload.text, preload.name);
                _bridge.Context.ExecutePendingJobs();
            } catch (Exception ex) {
                Debug.LogError($"[JSRunner] Preload '{preload.name}' failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Escape a string for safe use in JavaScript string literals.
    /// </summary>
    static string EscapeJsString(string s) {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
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
    /// Only works in Editor.
    /// </summary>
    public void ForceReload() {
#if UNITY_EDITOR
        Reload();
#else
        Debug.LogWarning("[JSRunner] Live reload is only supported in the Editor");
#endif
    }

#if UNITY_EDITOR
    void Reload() {
        if (!File.Exists(EntryFileFullPath)) {
            Debug.LogWarning($"[JSRunner] Entry file not found: {EntryFileFullPath}");
            return;
        }

        try {
            // 1. Clear UI and stylesheets
            _uiDocument.rootVisualElement.Clear();
            _uiDocument.rootVisualElement.styleSheets.Clear();

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
#endif // UNITY_EDITOR

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

    void Update() {
        _updateCount++;

#if UNITY_EDITOR
        // Editor: Use Unity's Update loop to drive the tick
        if (_scriptLoaded) {
            _bridge?.Tick();
            CheckForFileChanges();
        }
#elif UNITY_WEBGL
        // WebGL: State machine for safe async loading
        switch (_fetchState) {
            case WebGLFetchState.NotStarted:
                // Wait a few frames for Unity to stabilize before fetching
                if (_updateCount >= 3 && _embeddedScript == null && !string.IsNullOrEmpty(_streamingAssetsPath)) {
                    _fetchState = WebGLFetchState.Fetching;
                    StartJSFetch();
                }
                break;

            case WebGLFetchState.Fetching:
                // Poll for fetch completion - state transitions happen in PollFetchResult
                PollFetchResult();
                break;

            case WebGLFetchState.Ready:
                // Script is fetched and ready - execute it
                ExecuteFetchedScript();
                break;

            case WebGLFetchState.Executed:
            case WebGLFetchState.Error:
                // Terminal states - native RAF loop handles ticking for Executed
                break;
        }
#else
        // Standalone/Mobile builds: Use Unity's Update loop
        if (_scriptLoaded) {
            _bridge?.Tick();
        }
#endif
    }

    // MARK: Build-specific code (non-Editor)
#if !UNITY_EDITOR
    void InitializeBuild() {
        InitializeBridge();

        if (_embeddedScript != null) {
            // Use pre-assigned TextAsset
            RunScript(_embeddedScript.text, "embedded.js");
            return;
        }

        if (string.IsNullOrEmpty(_streamingAssetsPath)) {
            Debug.LogError("[JSRunner] No embedded script or streaming assets path configured");
            RunScript(DefaultEntryContent, "default.js");
            return;
        }

#if UNITY_WEBGL
        // WebGL: Loading is deferred to Update and uses browser's native fetch API
#elif UNITY_ANDROID
        // Android: StreamingAssets is inside APK, needs async loading
        StartCoroutine(LoadFromStreamingAssetsAsync());
#else
        // Desktop/iOS: Direct file access
        LoadFromStreamingAssetsSync();
#endif
    }

#if !UNITY_WEBGL && !UNITY_ANDROID
    void LoadFromStreamingAssetsSync() {
        var path = Path.Combine(Application.streamingAssetsPath, _streamingAssetsPath);
        if (File.Exists(path)) {
            var code = File.ReadAllText(path);
            RunScript(code, _streamingAssetsPath);
            Debug.Log($"[JSRunner] Loaded from StreamingAssets: {_streamingAssetsPath}");
        } else {
            Debug.LogError($"[JSRunner] Bundle not found: {path}");
            RunScript(DefaultEntryContent, "default.js");
        }
    }
#endif

#if UNITY_ANDROID
    System.Collections.IEnumerator LoadFromStreamingAssetsAsync() {
        var path = Path.Combine(Application.streamingAssetsPath, _streamingAssetsPath);
        using (var request = UnityEngine.Networking.UnityWebRequest.Get(path)) {
            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success) {
                var code = request.downloadHandler.text;
                RunScript(code, _streamingAssetsPath);
                Debug.Log($"[JSRunner] Loaded from StreamingAssets: {_streamingAssetsPath}");
            } else {
                Debug.LogError($"[JSRunner] Failed to load: {request.error}");
                RunScript(DefaultEntryContent, "default.js");
            }
        }
    }
#endif

#if UNITY_WEBGL
    void StartWebGLTick() {
        _bridge.Eval("if (typeof __startWebGLTick === 'function') __startWebGLTick();", "webgl-tick-start.js");
    }

    void StartJSFetch() {
        var url = Path.Combine(Application.streamingAssetsPath, _streamingAssetsPath);
        Debug.Log($"[JSRunner] Fetching from: {url}");

        // Use atomic state flags to prevent race conditions
        // __fetchState: 0 = fetching, 1 = ready, 2 = error
        var fetchCode = $@"
(function() {{
    globalThis.__fetchState = 0;  // Fetching
    globalThis.__fetchedScript = null;
    globalThis.__fetchError = null;

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
            globalThis.__fetchState = 1;  // Ready - atomic state transition
        }})
        .catch(function(err) {{
            console.error('[JSRunner] fetch error:', err);
            globalThis.__fetchError = err.message || String(err);
            globalThis.__fetchState = 2;  // Error - atomic state transition
        }});
}})();
";
        try {
            _bridge.Eval(fetchCode, "fetch-loader.js");
            Debug.Log("[JSRunner] Fetch initiated");
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Fetch eval error: {ex}");
            _fetchState = WebGLFetchState.Error;
            RunScript(DefaultEntryContent, "default.js");
        }
    }

    void PollFetchResult() {
        try {
            // Check the atomic state flag
            var stateStr = _bridge.Eval("globalThis.__fetchState");
            if (!int.TryParse(stateStr, out int state)) return;

            switch (state) {
                case 0: // Still fetching
                    return;

                case 1: // Ready
                    _fetchState = WebGLFetchState.Ready;
                    break;

                case 2: // Error
                    var errorMsg = _bridge.Eval("globalThis.__fetchError || 'Unknown fetch error'");
                    Debug.LogError($"[JSRunner] Fetch failed: {errorMsg}");
                    _fetchState = WebGLFetchState.Error;
                    _bridge.Eval("globalThis.__fetchError = null; globalThis.__fetchState = -1;");
                    RunScript(DefaultEntryContent, "default.js");
                    break;
            }
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Poll fetch error: {ex}");
            _fetchState = WebGLFetchState.Error;
        }
    }

    void ExecuteFetchedScript() {
        try {
            // Execute the fetched script in a separate step for clean state transitions
            var result = _bridge.Eval(@"
(function() {
    var script = globalThis.__fetchedScript;
    globalThis.__fetchedScript = null;
    globalThis.__fetchState = -1;  // Clear state
    if (!script) return 'error:No script content';
    try {
        (0, eval)(script);
        return 'ok';
    } catch (e) {
        console.error('[JSRunner] Script execution error:', e);
        return 'error:' + (e.message || String(e));
    }
})()
");
            if (result == "ok") {
                _bridge.Context.ExecutePendingJobs();
                _scriptLoaded = true;
                _fetchState = WebGLFetchState.Executed;
                StartWebGLTick();
                Debug.Log("[JSRunner] Script executed successfully");
            } else if (result.StartsWith("error:")) {
                var error = result.Substring(6);
                Debug.LogError($"[JSRunner] Script execution failed: {error}");
                _fetchState = WebGLFetchState.Error;
                RunScript(DefaultEntryContent, "default.js");
            }
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Execute script error: {ex}");
            _fetchState = WebGLFetchState.Error;
        }
    }
#endif
#endif // !UNITY_EDITOR

    void OnDestroy() {
        _bridge?.Dispose();
        _bridge = null;
    }
}
