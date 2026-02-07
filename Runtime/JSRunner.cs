using System;
using System.Collections.Generic;
using System.IO;
using OneJS;
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
    [Tooltip("Target path relative to WorkingDir (e.g., 'index.tsx' or 'styles/main.uss')")]
    public string path;
    [Tooltip("TextAsset containing the file content")]
    public TextAsset content;
}

/// <summary>
/// MonoBehaviour that runs JavaScript from an auto-managed working directory.
/// Automatically creates UIDocument and PanelSettings if not present.
///
/// Features:
/// - Zero-config: Working directory is auto-created next to the scene file
/// - Auto-scaffolding: Creates project structure on first run
/// - Live Reload: Polls entry file for changes and hot-reloads
/// - Hard Reload: Disposes context and recreates fresh on file change
/// - Zero-config UI: Auto-creates UIDocument (RequireComponent) and PanelSettings
///
/// Directory structure (for scene "Level1.unity" with JSRunner on "MainUI"):
///   Assets/Scenes/Level1/MainUI_abc123/MainUI~/  (working dir, ignored by Unity)
///   Assets/Scenes/Level1/MainUI_abc123/app.js.txt (bundle TextAsset)
///
/// Platform behavior:
/// - Editor: Loads JS from filesystem with live reload support
/// - Builds: Loads from embedded TextAsset (auto-generated during build)
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class JSRunner : MonoBehaviour {
    const string DefaultEntryContent = "console.log(\"OneJS is good to go!\");\n";
    const string DefaultBundleFile = "app.js.txt";

    // Instance identification (generated once, persisted)
    [SerializeField, HideInInspector] string _instanceId;

    // Tracks whether initialization has completed (for re-enable handling)
    [SerializeField, HideInInspector] bool _initialized;

    [Tooltip("UIDocument component for the UI. Auto-created at runtime if not assigned.")]
    [SerializeField] UIDocument _uiDocument;

    [Tooltip("PanelSettings asset for the UI. Auto-created in instance folder on first Play mode if not assigned.")]
    [SerializeField] PanelSettings _panelSettings;

    [Tooltip("VisualTreeAsset (UXML) for the UI. Auto-created in instance folder on first Play mode if not assigned.")]
    [SerializeField] VisualTreeAsset _visualTreeAsset;

    [SerializeField] ThemeStyleSheet _defaultThemeStylesheet;

    [Tooltip("Automatically reload when the entry file changes (Editor only)")]
    [SerializeField] bool _liveReload = true;
    [Tooltip("How often to check for file changes (in seconds)")]
    [SerializeField] float _pollInterval = 0.5f;
    [Tooltip("Enable Janitor to clean up GameObjects created by JS on reload")]
    [SerializeField] bool _enableJanitor = true;

    [Tooltip("Include source map in build for better error messages")]
    [SerializeField] bool _includeSourceMap = true;

    // Auto-generated TextAssets (created during build)
    [SerializeField, HideInInspector] TextAsset _bundleAsset;
    [SerializeField, HideInInspector] TextAsset _sourceMapAsset;

    [Tooltip("Default files to create in WorkingDir if missing. Path is relative to WorkingDir.")]
    [PairDrawer("←")]
    [SerializeField] List<DefaultFileEntry> _defaultFiles = new List<DefaultFileEntry>();

    [Tooltip("C# assemblies to generate TypeScript typings for (e.g., 'Assembly-CSharp').")]
    [SerializeField] List<string> _typingAssemblies = new List<string>();
    [Tooltip("Automatically regenerate typings when C# scripts are recompiled.")]
    [SerializeField] bool _autoGenerateTypings = true;
    [Tooltip("Output path for generated .d.ts file, relative to WorkingDir.")]
    [SerializeField] string _typingsOutputPath = "types/csharp.d.ts";

    [Tooltip("USS stylesheets to apply to the root element on init/reload.")]
    [SerializeField] List<StyleSheet> _stylesheets = new List<StyleSheet>();
    [Tooltip("TextAssets to load and evaluate before the entry file. Useful for polyfills or shared libraries.")]
    [SerializeField] List<TextAsset> _preloads = new List<TextAsset>();
    [Tooltip("Unity objects to expose as JavaScript globals (globalThis[key] = value).")]
    [PairDrawer("→")]
    [SerializeField] List<GlobalEntry> _globals = new List<GlobalEntry>();

    [Tooltip("UI Cartridges to load. Files are extracted at build time, accessible via __cart('slug') at runtime.")]
    [SerializeField] List<UICartridge> _cartridges = new List<UICartridge>();

    QuickJSUIBridge _bridge;
    bool _scriptLoaded;

    // Live reload state
    DateTime _lastModifiedTime;
    DateTime _lastReloadTime;
    float _nextPollTime;
    int _reloadCount;
    string _lastContentHash;
    Janitor _janitor;

    // Public API
    public QuickJSUIBridge Bridge => _bridge;
    public bool IsRunning => _scriptLoaded && _bridge != null;
    public bool IsLiveReloadEnabled => _liveReload;
    public int ReloadCount => _reloadCount;
    public DateTime LastModifiedTime => _lastModifiedTime;
    public DateTime LastReloadTime => _lastReloadTime;
    public bool IncludeSourceMap => _includeSourceMap;
    public TextAsset BundleAsset => _bundleAsset;
    public TextAsset SourceMapAsset => _sourceMapAsset;

    /// <summary>
    /// Unique instance ID for this JSRunner (generated once, persisted).
    /// </summary>
    public string InstanceId {
        get {
            if (string.IsNullOrEmpty(_instanceId)) {
                _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
            return _instanceId;
        }
    }

    /// <summary>
    /// Project root path (Assets folder parent in Editor, dataPath in builds).
    /// </summary>
    public string ProjectRoot {
        get {
#if UNITY_EDITOR
            return Path.GetDirectoryName(Application.dataPath);
#else
            return Application.dataPath;
#endif
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Scene folder path: {SceneDirectory}/{SceneName}/
    /// Returns null if scene is not saved.
    /// </summary>
    public string SceneFolder {
        get {
            var scenePath = gameObject.scene.path;
            if (string.IsNullOrEmpty(scenePath)) return null;

            var sceneDir = Path.GetDirectoryName(scenePath);
            var sceneName = Path.GetFileNameWithoutExtension(scenePath);
            return Path.Combine(sceneDir, sceneName);
        }
    }

    /// <summary>
    /// Instance folder path: {SceneFolder}/{GameObjectName}_{InstanceId}/
    /// </summary>
    public string InstanceFolder {
        get {
            var sceneFolder = SceneFolder;
            if (string.IsNullOrEmpty(sceneFolder)) return null;
            return Path.Combine(sceneFolder, $"{gameObject.name}_{InstanceId}");
        }
    }

    /// <summary>
    /// Working directory path (ignored by Unity due to ~ suffix):
    /// {InstanceFolder}/{GameObjectName}~/
    /// </summary>
    public string WorkingDirFullPath {
        get {
            var instanceFolder = InstanceFolder;
            if (string.IsNullOrEmpty(instanceFolder)) return null;
            return Path.Combine(instanceFolder, $"{gameObject.name}~");
        }
    }

    /// <summary>
    /// Full path to the bundle file: {InstanceFolder}/app.js.txt
    /// This is the same location as the TextAsset used in builds.
    /// </summary>
    public string EntryFileFullPath {
        get {
            var instanceFolder = InstanceFolder;
            if (string.IsNullOrEmpty(instanceFolder)) return null;
            return Path.Combine(instanceFolder, DefaultBundleFile);
        }
    }

    /// <summary>
    /// Full path to the source map file: {InstanceFolder}/app.js.map.txt
    /// </summary>
    public string SourceMapFilePath {
        get {
            var entryFile = EntryFileFullPath;
            if (string.IsNullOrEmpty(entryFile)) return null;
            // app.js.txt -> app.js.map.txt
            return entryFile.Replace(".js.txt", ".js.map.txt");
        }
    }

    /// <summary>
    /// Asset path for the bundle TextAsset: {InstanceFolder}/app.js.txt
    /// Same as EntryFileFullPath since esbuild outputs directly here.
    /// </summary>
    public string BundleAssetPath => EntryFileFullPath;

    /// <summary>
    /// Asset path for the source map TextAsset: {InstanceFolder}/app.js.map.txt
    /// </summary>
    public string SourceMapAssetPath => SourceMapFilePath;

    /// <summary>
    /// Asset path for the PanelSettings: {InstanceFolder}/PanelSettings.asset
    /// </summary>
    public string PanelSettingsAssetPath {
        get {
            var instanceFolder = InstanceFolder;
            if (string.IsNullOrEmpty(instanceFolder)) return null;
            return Path.Combine(instanceFolder, "PanelSettings.asset");
        }
    }

    /// <summary>
    /// Asset path for the VisualTreeAsset: {InstanceFolder}/UIDocument.uxml
    /// </summary>
    public string VisualTreeAssetPath {
        get {
            var instanceFolder = InstanceFolder;
            if (string.IsNullOrEmpty(instanceFolder)) return null;
            return Path.Combine(instanceFolder, "UIDocument.uxml");
        }
    }

    /// <summary>
    /// Whether the scene is saved and paths are valid.
    /// </summary>
    public bool IsSceneSaved => !string.IsNullOrEmpty(gameObject.scene.path);

    /// <summary>
    /// Set the bundle TextAsset (called by build processor).
    /// </summary>
    public void SetBundleAsset(TextAsset asset) {
        _bundleAsset = asset;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// Set the source map TextAsset (called by build processor).
    /// </summary>
    public void SetSourceMapAsset(TextAsset asset) {
        _sourceMapAsset = asset;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// Clear the generated assets.
    /// </summary>
    public void ClearGeneratedAssets() {
        _bundleAsset = null;
        _sourceMapAsset = null;
        UnityEditor.EditorUtility.SetDirty(this);
    }


    /// <summary>
    /// Creates a default PanelSettings asset in the instance folder.
    /// Called automatically on first Play mode if no PanelSettings is assigned.
    /// </summary>
    public void CreateDefaultPanelSettingsAsset() {
        var instanceFolder = InstanceFolder;
        if (string.IsNullOrEmpty(instanceFolder)) return;

        // Ensure instance folder exists
        if (!Directory.Exists(instanceFolder)) {
            Directory.CreateDirectory(instanceFolder);
        }

        // Create PanelSettings with sensible defaults
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1920, 1080);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 0.5f;

        // Apply theme stylesheet if set
        if (_defaultThemeStylesheet != null) {
            ps.themeStyleSheet = _defaultThemeStylesheet;
        }

        // Save as asset
        var assetPath = PanelSettingsAssetPath;
        UnityEditor.AssetDatabase.CreateAsset(ps, assetPath);
        UnityEditor.AssetDatabase.SaveAssets();

        // Auto-assign to this JSRunner
        _panelSettings = ps;
        UnityEditor.EditorUtility.SetDirty(this);

        Debug.Log($"[JSRunner] Created PanelSettings asset: {assetPath}");
    }

    /// <summary>
    /// Creates a default VisualTreeAsset (UXML) in the instance folder.
    /// Called automatically on first Play mode if no VisualTreeAsset is assigned.
    /// </summary>
    public void CreateDefaultVisualTreeAsset() {
        var instanceFolder = InstanceFolder;
        if (string.IsNullOrEmpty(instanceFolder)) return;

        // Ensure instance folder exists
        if (!Directory.Exists(instanceFolder)) {
            Directory.CreateDirectory(instanceFolder);
        }

        // Create minimal UXML content
        var uxmlContent = @"<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
</ui:UXML>";

        // Write UXML file
        var assetPath = VisualTreeAssetPath;
        File.WriteAllText(assetPath, uxmlContent);

        // Import the asset
        UnityEditor.AssetDatabase.ImportAsset(assetPath);

        // Load and assign the VisualTreeAsset
        _visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
        UnityEditor.EditorUtility.SetDirty(this);

        Debug.Log($"[JSRunner] Created VisualTreeAsset: {assetPath}");
    }

    /// <summary>
    /// Ensures the project is set up with all required directories and scaffolded files.
    /// Called before entering Play mode to ensure everything is ready.
    /// Returns true if scaffolding was performed (first-time setup).
    /// </summary>
    public bool EnsureProjectSetup() {
        if (!IsSceneSaved) return false;

        var workingDir = WorkingDirFullPath;
        var instanceFolder = InstanceFolder;

        if (string.IsNullOrEmpty(workingDir) || string.IsNullOrEmpty(instanceFolder)) return false;

        bool didScaffold = false;

        // Ensure instance folder exists
        if (!Directory.Exists(instanceFolder)) {
            Directory.CreateDirectory(instanceFolder);
        }

        // Ensure working directory exists
        if (!Directory.Exists(workingDir)) {
            Directory.CreateDirectory(workingDir);
            Debug.Log($"[JSRunner] Created working directory: {workingDir}");
            didScaffold = true;
        }

        // Scaffold default files
        if (_defaultFiles != null && _defaultFiles.Count > 0) {
            foreach (var entry in _defaultFiles) {
                if (string.IsNullOrEmpty(entry.path) || entry.content == null) continue;

                var fullPath = Path.Combine(workingDir, entry.path);

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
                didScaffold = true;
            }
        }

        // Extract cartridges
        if (_cartridges != null && _cartridges.Count > 0) {
            foreach (var cartridge in _cartridges) {
                if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) continue;

                var destPath = GetCartridgePath(cartridge);
                if (string.IsNullOrEmpty(destPath)) continue;

                // Skip if folder already exists
                if (Directory.Exists(destPath)) continue;

                Directory.CreateDirectory(destPath);

                // Extract files
                foreach (var file in cartridge.Files) {
                    if (file.content == null) continue;
                    var filePath = Path.Combine(destPath, file.path);
                    var fileDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir)) {
                        Directory.CreateDirectory(fileDir);
                    }
                    File.WriteAllText(filePath, file.content.text);
                }
                Debug.Log($"[JSRunner] Extracted cartridge: {cartridge.Slug}");
                didScaffold = true;
            }
        }

        return didScaffold;
    }

    /// <summary>
    /// Whether the bundle file exists (app.js.txt).
    /// </summary>
    public bool HasBundle => File.Exists(EntryFileFullPath);

    /// <summary>
    /// Whether node_modules exists in the working directory.
    /// </summary>
    public bool HasNodeModules {
        get {
            var workingDir = WorkingDirFullPath;
            if (string.IsNullOrEmpty(workingDir)) return false;
            return Directory.Exists(Path.Combine(workingDir, "node_modules"));
        }
    }

    /// <summary>
    /// Whether package.json exists in the working directory.
    /// </summary>
    public bool HasPackageJson {
        get {
            var workingDir = WorkingDirFullPath;
            if (string.IsNullOrEmpty(workingDir)) return false;
            return File.Exists(Path.Combine(workingDir, "package.json"));
        }
    }
#endif

    // Type Generation properties
    public IReadOnlyList<string> TypingAssemblies => _typingAssemblies;
    public bool AutoGenerateTypings => _autoGenerateTypings;
    public string TypingsOutputPath => _typingsOutputPath;
#if UNITY_EDITOR
    public string TypingsFullPath => WorkingDirFullPath != null ? Path.Combine(WorkingDirFullPath, _typingsOutputPath) : null;
#endif

    // Cartridge API
    public IReadOnlyList<UICartridge> Cartridges => _cartridges;

#if UNITY_EDITOR
    public string GetCartridgePath(UICartridge cartridge) {
        return CartridgeUtils.GetCartridgePath(WorkingDirFullPath, cartridge);
    }
#endif

    void Start() {
        try {
            // Get UIDocument (created by RequireComponent)
            if (_uiDocument == null) {
                _uiDocument = GetComponent<UIDocument>();
            }

            if (_uiDocument == null) {
                Debug.LogError("[JSRunner] UIDocument component not found");
                return;
            }

            // Ensure PanelSettings is assigned
            if (_uiDocument.panelSettings == null && _panelSettings != null) {
                _uiDocument.panelSettings = _panelSettings;
            }

            // Ensure VisualTreeAsset is assigned
            if (_uiDocument.visualTreeAsset == null && _visualTreeAsset != null) {
                _uiDocument.visualTreeAsset = _visualTreeAsset;
            }

            // Verify we have a valid root element
            if (_uiDocument.rootVisualElement == null) {
                Debug.LogError("[JSRunner] UIDocument rootVisualElement is null");
                return;
            }

            Initialize();
            _initialized = true;
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Start() exception: {ex}");
        }
    }

    void OnEnable() {
        if (!_initialized) {
            // First enable - let Start() handle initialization
            return;
        }

        // Re-enable - reload to reconnect to new rootVisualElement
        ReloadOnEnable();
    }

    void ReloadOnEnable() {
        // Clean up GameObjects created by JS (if Janitor enabled)
#if UNITY_EDITOR
        if (_enableJanitor && _janitor != null) {
            _janitor.Clean();
        }
#endif

        // Clear UI and dispose old bridge
        if (_uiDocument != null && _uiDocument.rootVisualElement != null) {
            _uiDocument.rootVisualElement.Clear();
            _uiDocument.rootVisualElement.styleSheets.Clear();
        }

        _bridge?.Dispose();
        _bridge = null;

        // Recreate bridge with fresh __root
        InitializeBridge();

#if UNITY_EDITOR
        // Editor: reload from file
        var entryFile = EntryFileFullPath;
        if (File.Exists(entryFile)) {
            var code = File.ReadAllText(entryFile);
            RunScript(code, Path.GetFileName(entryFile));
        }
#else
        // Build: reload from bundled asset
        if (_bundleAsset != null) {
            RunScript(_bundleAsset.text, "app.js");
        }
#endif
    }

    void Initialize() {
#if UNITY_EDITOR
        InitializeEditor();
#else
        InitializeBuild();
#endif
    }

#if UNITY_EDITOR
    void InitializeEditor() {
        // Check if scene is saved
        if (!IsSceneSaved) {
            Debug.LogError("[JSRunner] Scene must be saved before JSRunner can initialize. Save the scene and enter Play mode again.");
            return;
        }

        // Auto-create or load PanelSettings asset if not assigned
        if (_panelSettings == null) {
            var psPath = PanelSettingsAssetPath;
            var existingPS = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath);
            if (existingPS != null) {
                // Found existing asset, use it
                _panelSettings = existingPS;
                UnityEditor.EditorUtility.SetDirty(this);
            } else {
                // Create new PanelSettings asset
                CreateDefaultPanelSettingsAsset();
            }
        }

        // Auto-create or load VisualTreeAsset if not assigned
        if (_visualTreeAsset == null) {
            var vtaPath = VisualTreeAssetPath;
            var existingVTA = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(vtaPath);
            if (existingVTA != null) {
                // Found existing asset, use it
                _visualTreeAsset = existingVTA;
                UnityEditor.EditorUtility.SetDirty(this);
            } else {
                // Create new VisualTreeAsset
                CreateDefaultVisualTreeAsset();
            }
        }

        // Ensure UIDocument reference is set
        if (_uiDocument == null) {
            _uiDocument = GetComponent<UIDocument>();
        }

        // Ensure PanelSettings is assigned to UIDocument
        if (_uiDocument != null && _uiDocument.panelSettings == null && _panelSettings != null) {
            _uiDocument.panelSettings = _panelSettings;
        }

        // Ensure VisualTreeAsset is assigned to UIDocument
        if (_uiDocument != null && _uiDocument.visualTreeAsset == null && _visualTreeAsset != null) {
            _uiDocument.visualTreeAsset = _visualTreeAsset;
        }

        var workingDir = WorkingDirFullPath;
        var entryFile = EntryFileFullPath;

        if (string.IsNullOrEmpty(workingDir) || string.IsNullOrEmpty(entryFile)) {
            Debug.LogError("[JSRunner] Invalid paths. Ensure scene is saved.");
            return;
        }

        // Ensure working directory exists
        if (!Directory.Exists(workingDir)) {
            Directory.CreateDirectory(workingDir);
            Debug.Log($"[JSRunner] Created working directory: {workingDir}");
        }

        // Scaffold any missing default files
        ScaffoldDefaultFiles();

        // Extract cartridge files
        ExtractCartridges();

        // Ensure entry file exists (fallback if not in defaultFiles)
        EnsureEntryFile();

        // Refresh AssetDatabase so new folders/files appear in Project panel
        UnityEditor.AssetDatabase.Refresh();

        // Initialize bridge and run script
        InitializeBridge();

        // Spawn Janitor if enabled (only on first initialization)
        if (_enableJanitor && _janitor == null) {
            var janitorGO = new GameObject("Janitor");
            _janitor = janitorGO.AddComponent<Janitor>();
        }

        var code = File.ReadAllText(entryFile);
        RunScript(code, Path.GetFileName(entryFile));

        // Initialize file watching
        if (_liveReload) {
            _lastModifiedTime = File.GetLastWriteTime(entryFile);
            _lastContentHash = ComputeFileHash(entryFile);
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

        // Create minimal bundle file
        File.WriteAllText(EntryFileFullPath, DefaultEntryContent);
        Debug.Log($"[JSRunner] Created default bundle file: {DefaultBundleFile}");
    }

    /// <summary>
    /// Extract cartridge files to WorkingDir/@cartridges/{slug}/.
    /// Only extracts if the cartridge folder doesn't already exist.
    /// </summary>
    void ExtractCartridges() {
        CartridgeUtils.ExtractCartridges(WorkingDirFullPath, _cartridges, overwriteExisting: false, "[JSRunner]");
    }
#endif // UNITY_EDITOR

    void InitializeBridge() {
#if UNITY_EDITOR
        _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement, WorkingDirFullPath);
#else
        // In builds, use persistent data path (bundle is self-contained)
        _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement, Application.persistentDataPath);
#endif

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

        // Register UI debugging utilities
        RegisterUIDebugUtilities();

        // Inject custom globals
        InjectGlobals();

        // Run preload scripts
        RunPreloads();
    }

    /// <summary>
    /// Apply configured USS stylesheets to the root element.
    /// </summary>
    void ApplyStylesheets() {
        CartridgeUtils.ApplyStylesheets(_uiDocument.rootVisualElement, _stylesheets);
    }

    /// <summary>
    /// Register UI debugging utilities as global JavaScript functions.
    /// Provides __dumpUI(), __findByClass(), __findByType() for debugging USS selectors.
    /// </summary>
    void RegisterUIDebugUtilities() {
        _bridge.Eval(@"
            globalThis.__dumpUI = function(element, maxDepth, includeStyles) {
                element = element || __root;
                maxDepth = maxDepth || 10;
                includeStyles = includeStyles || false;
                return CS.OneJS.Utils.UIDebugger.DumpTree(element, maxDepth, includeStyles);
            };
            globalThis.__findByClass = function(className, element) {
                element = element || __root;
                return CS.OneJS.Utils.UIDebugger.FindByClass(element, className);
            };
            globalThis.__findByType = function(typeName, element) {
                element = element || __root;
                return CS.OneJS.Utils.UIDebugger.FindByType(element, typeName);
            };
        ");
    }

    /// <summary>
    /// Inject custom Unity objects as JavaScript globals.
    /// </summary>
    void InjectGlobals() {
        // Inject user-defined globals
        if (_globals != null && _globals.Count > 0) {
            foreach (var entry in _globals) {
                if (string.IsNullOrEmpty(entry.key) || entry.value == null) continue;

                var handle = QuickJSNative.RegisterObject(entry.value);
                var typeName = entry.value.GetType().FullName;
                _bridge.Eval($"globalThis['{CartridgeUtils.EscapeJsString(entry.key)}'] = __csHelpers.wrapObject('{typeName}', {handle})");
            }
        }

        // Inject cartridge objects
        CartridgeUtils.InjectCartridgeGlobals(_bridge, _cartridges);
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
                var message = TranslateErrorMessage(ex.Message);
                Debug.LogError($"[JSRunner] Preload '{preload.name}' failed: {message}");
            }
        }
    }

    /// <summary>
    /// Compute MD5 hash of a file for change detection.
    /// Uses streaming to avoid loading entire file into memory.
    /// </summary>
    static string ComputeFileHash(string filePath) {
        using var md5 = System.Security.Cryptography.MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash);
    }

    void RunScript(string code, string filename) {
        _bridge.Eval(code, filename);
        // Execute pending Promise jobs immediately to allow React's first render
        _bridge.Context.ExecutePendingJobs();
        _scriptLoaded = true;

        // Cache __tick callback handle for zero-allocation per-frame invocation
        _bridge.CacheTickCallback();

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
            // 0. Clean up GameObjects created by JS (if Janitor enabled)
            if (_enableJanitor && _janitor != null) {
                _janitor.Clean();
            }

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
            RunScript(code, Path.GetFileName(EntryFileFullPath));

            // 5. Update state
            _lastModifiedTime = File.GetLastWriteTime(EntryFileFullPath);
            _lastContentHash = ComputeFileHash(EntryFileFullPath);
            _lastReloadTime = DateTime.Now;
            _reloadCount++;
            Debug.Log($"[JSRunner] Reloaded ({_reloadCount})");
        } catch (Exception ex) {
            var message = TranslateErrorMessage(ex.Message);
            Debug.LogError($"[JSRunner] Reload failed: {message}");
        }
    }

    void CheckForFileChanges() {
        if (!_liveReload || !_scriptLoaded) return;
        if (Time.realtimeSinceStartup < _nextPollTime) return;

        _nextPollTime = Time.realtimeSinceStartup + _pollInterval;

        try {
            if (!File.Exists(EntryFileFullPath)) return;

            // Fast path: check mtime first (no I/O beyond stat)
            var currentModTime = File.GetLastWriteTime(EntryFileFullPath);
            if (currentModTime == _lastModifiedTime) return;

            // mtime changed - compute content hash to detect actual changes
            var currentHash = ComputeFileHash(EntryFileFullPath);
            _lastModifiedTime = currentModTime;

            // Skip reload if content unchanged (e.g., esbuild rebuild with same output)
            if (currentHash == _lastContentHash) return;

            _lastContentHash = currentHash;
            Reload();
        } catch (IOException) {
            // File might be locked by build process, skip this poll
        } catch (Exception ex) {
            Debug.LogWarning($"[JSRunner] Error checking file: {ex.Message}");
        }
    }

    [ContextMenu("Link Local Packages")]
    void LinkLocalPackagesContextMenu() {
        var workingDir = WorkingDirFullPath;
        if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir)) {
            Debug.LogError("[JSRunner] Working directory not found. Save the scene first.");
            return;
        }

        Debug.Log("[JSRunner] Running npm link onejs-react onejs-unity unity-types...");

        var npmPath = FindNpmPath();
        var nodeBinDir = Path.GetDirectoryName(npmPath);

        var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = npmPath,
            Arguments = "link onejs-react onejs-unity unity-types",
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!string.IsNullOrEmpty(nodeBinDir)) {
            startInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;
        }

        var process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, args) => {
            if (!string.IsNullOrEmpty(args.Data)) Debug.Log($"[npm] {args.Data}");
        };
        process.ErrorDataReceived += (_, args) => {
            if (!string.IsNullOrEmpty(args.Data)) Debug.Log($"[npm] {args.Data}");
        };
        process.Exited += (_, _) => {
            UnityEditor.EditorApplication.delayCall += () => {
                if (process.ExitCode == 0) {
                    Debug.Log("[JSRunner] Local packages linked successfully!");
                } else {
                    Debug.LogError($"[JSRunner] npm link failed with exit code {process.ExitCode}");
                }
            };
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    static string FindNpmPath() {
#if UNITY_EDITOR_WIN
        return "npm.cmd";
#else
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] searchPaths = {
            "/usr/local/bin/npm",
            "/opt/homebrew/bin/npm",
            "/usr/bin/npm",
            Path.Combine(home, "n/bin/npm"),
        };

        foreach (var path in searchPaths) {
            if (File.Exists(path)) return path;
        }

        // Check nvm
        var nvmDir = Path.Combine(home, ".nvm/versions/node");
        if (Directory.Exists(nvmDir)) {
            try {
                foreach (var nodeDir in Directory.GetDirectories(nvmDir)) {
                    var npmPath = Path.Combine(nodeDir, "bin", "npm");
                    if (File.Exists(npmPath)) return npmPath;
                }
            } catch { }
        }

        return "npm";
#endif
    }
#endif // UNITY_EDITOR

    /// <summary>
    /// Inject Unity platform defines as JavaScript globals.
    /// These can be used for conditional code: if (UNITY_WEBGL) { ... }
    /// </summary>
    void InjectPlatformDefines() {
        CartridgeUtils.InjectPlatformDefines(_bridge);
    }

    void Update() {
#if UNITY_EDITOR
        // Editor: Use Unity's Update loop to drive the tick
        if (_scriptLoaded) {
            _bridge?.Tick();
            CheckForFileChanges();
        }
#elif !UNITY_WEBGL
        // Standalone/Mobile builds: Use Unity's Update loop
        // WebGL uses native RAF tick loop started in RunScript()
        if (_scriptLoaded) {
            _bridge?.Tick();
        }
#endif
    }

    // MARK: Build-specific code (non-Editor)
#if !UNITY_EDITOR
    void InitializeBuild() {
        InitializeBridge();

        // Use the auto-generated bundle TextAsset
        if (_bundleAsset != null) {
            RunScript(_bundleAsset.text, "app.js");
            return;
        }

        Debug.LogError("[JSRunner] No bundle asset found. Ensure project was built correctly.");
        RunScript(DefaultEntryContent, "default.js");
    }

#if UNITY_WEBGL
    void StartWebGLTick() {
        _bridge.Eval("if (typeof __startWebGLTick === 'function') __startWebGLTick();", "webgl-tick-start.js");
    }
#endif
#endif // !UNITY_EDITOR

    string TranslateErrorMessage(string message) {
        if (string.IsNullOrEmpty(message)) return message;

#if UNITY_EDITOR
        var parser = SourceMapParser.Load(SourceMapFilePath);
#else
        var parser = _sourceMapAsset != null ? SourceMapParser.Parse(_sourceMapAsset.text) : null;
#endif
        if (parser == null) return message;

        return parser.TranslateStackTrace(message);
    }

    void OnDestroy() {
        _bridge?.Dispose();
        _bridge = null;
        _initialized = false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Called when the component is first added or Reset from context menu.
    /// Auto-creates PanelSettings, configures UIDocument, and populates default files.
    /// </summary>
    void Reset() {
        // Get UIDocument (created by RequireComponent)
        _uiDocument = GetComponent<UIDocument>();

        // Create PanelSettings if scene is saved
        if (IsSceneSaved && _panelSettings == null) {
            CreateDefaultPanelSettingsAsset();
        }

        // Create VisualTreeAsset if scene is saved
        if (IsSceneSaved && _visualTreeAsset == null) {
            CreateDefaultVisualTreeAsset();
        }

        // Assign PanelSettings to UIDocument
        if (_uiDocument != null && _panelSettings != null && _uiDocument.panelSettings == null) {
            _uiDocument.panelSettings = _panelSettings;
        }

        // Assign VisualTreeAsset to UIDocument
        if (_uiDocument != null && _visualTreeAsset != null && _uiDocument.visualTreeAsset == null) {
            _uiDocument.visualTreeAsset = _visualTreeAsset;
        }

        PopulateDefaultFiles();
    }

    /// <summary>
    /// Finds and loads default template files from the OneJS Editor/Templates folder.
    /// Uses PackageInfo to robustly locate the package regardless of installation method.
    /// </summary>
    public void PopulateDefaultFiles() {
        // Template mapping: source file name → target path in WorkingDir
        // Note: esbuild outputs directly to ../app.js.txt (outside working dir)
        var templateMapping = new (string templateName, string targetPath)[] {
            ("package.json.txt", "package.json"),
            ("tsconfig.json.txt", "tsconfig.json"),
            ("esbuild.config.mjs.txt", "esbuild.config.mjs"),
            ("index.tsx.txt", "index.tsx"),
            ("global.d.ts.txt", "types/global.d.ts"),
            ("main.uss.txt", "styles/main.uss"),
            ("gitignore.txt", ".gitignore"),
        };

        _defaultFiles.Clear();

        // Find the OneJS package using the assembly that contains JSRunner
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(JSRunner).Assembly);
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
                Debug.LogWarning("[JSRunner] Could not find OneJS Editor/Templates folder");
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
                Debug.LogWarning($"[JSRunner] Template not found: {templatePath}");
            }
        }

        Debug.Log($"[JSRunner] Populated {_defaultFiles.Count} default files from templates");
    }
#endif
}
