using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
/// - Zero-config UI: UIDocument is added at runtime; PanelSettings/VisualTreeAsset assigned then.
///
/// Directory structure (for scene "Level1.unity" with JSRunner on "MainUI"):
///   Assets/Scenes/Level1/MainUI_abc123/~/  (working dir, ignored by Unity)
///   Assets/Scenes/Level1/MainUI_abc123/PanelSettings.asset (folder marker)
///   Assets/Scenes/Level1/MainUI_abc123/app.js.txt (bundle TextAsset)
///
/// Platform behavior:
/// - Editor: Loads JS from filesystem with live reload support
/// - Builds: Loads from embedded TextAsset (auto-generated during build)
/// </summary>
#if UNITY_EDITOR
[ExecuteAlways]
#endif
public class JSRunner : MonoBehaviour {
    const string DefaultEntryContent = "console.log(\"OneJS is good to go!\");\n";
    const string DefaultBundleFile = "app.js.txt";

    // Instance identification (generated once, persisted)
    [SerializeField, HideInInspector] string _instanceId;

    // Tracks whether initialization has completed (for re-enable handling)
    [SerializeField, HideInInspector] bool _initialized;

    // UIDocument is added at runtime and assigned from _panelSettings/_visualTreeAsset; not shown in editor.
    UIDocument _uiDocument;

    [Tooltip("PanelSettings asset for the UI. Assigned to the runtime UIDocument. Auto-created in instance folder on Initialize if not set.")]
    [SerializeField] PanelSettings _panelSettings;

    [Tooltip("VisualTreeAsset (UXML) for the UI. Assigned to the runtime UIDocument. Auto-synced from project folder when Panel Settings is set.")]
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

    [Tooltip("Editor-only: show the JSRunner tabs even when Panel Settings is not assigned (for debugging UI).")]
    [SerializeField, HideInInspector] bool _enableDebugMode;

#if UNITY_EDITOR
    [ContextMenu("Toggle Dev Mode")]
    void ToggleDevMode() {
        _enableDebugMode = !_enableDebugMode;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

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

    static readonly List<JSRunner> _instances = new List<JSRunner>();

    /// <summary>
    /// All enabled JSRunner instances. Avoids FindObjectsByType; register/unregister in OnEnable/OnDisable.
    /// Callers should filter for non-null, enabled, and gameObject.activeInHierarchy as needed.
    /// </summary>
    public static IReadOnlyList<JSRunner> Instances => _instances;

#if UNITY_EDITOR
    // Edit-mode preview state (non-serialized, rebuilt on enable)
    bool _editModePreviewActive;
    float _nextEditModeTick;
    const float EditModeTickInterval = 1f / 30f; // 30Hz throttle
    public static Func<JSRunner, bool> EditModeUpdateFilter;
    public static Func<JSRunner, bool> PlayModeUpdateFilter;
    FileSystemWatcher _editModeWatcher;
#endif

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
#if UNITY_EDITOR
    public bool IsEditModePreviewActive => _editModePreviewActive;

    /// <summary>
    /// Fired when edit-mode preview starts successfully. Editor code (e.g., JSRunnerAutoWatch)
    /// subscribes to this to start the esbuild watcher so source changes rebuild the bundle.
    /// </summary>
    public static event Action<JSRunner> EditModePreviewStarted;
#endif

    /// <summary>
    /// Set PanelSettings at runtime and sync to the runtime UIDocument. Use this instead of assigning the field when changing from script.
    /// </summary>
    public void SetPanelSettings(PanelSettings panelSettings) {
        _panelSettings = panelSettings;
        if (_uiDocument != null) _uiDocument.panelSettings = _panelSettings;
    }

    /// <summary>
    /// Set VisualTreeAsset at runtime and sync to the runtime UIDocument. Use this instead of assigning the field when changing from script.
    /// </summary>
    public void SetVisualTreeAsset(VisualTreeAsset visualTreeAsset) {
        _visualTreeAsset = visualTreeAsset;
        if (_uiDocument != null) _uiDocument.visualTreeAsset = _visualTreeAsset;
    }

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
    /// Instance folder path (full filesystem path). Derived from PanelSettings asset path; null when no PanelSettings or when Panel Settings is invalid (so we behave as if not attached).
    /// </summary>
    public string InstanceFolder {
        get {
#if UNITY_EDITOR
            if (_panelSettings == null) return null;
            if (!IsPanelSettingsInValidProjectFolder()) return null;
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(_panelSettings);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) return null;
            var dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir)) return null;
            var normalized = dir.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(ProjectRoot, normalized);
#else
            return null;
#endif
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Instance folder as Unity asset path (relative, forward slashes) for AssetDatabase APIs. Null when Panel Settings is invalid.
    /// </summary>
    public string InstanceFolderAssetPath {
        get {
            if (_panelSettings == null) return null;
            if (!IsPanelSettingsInValidProjectFolder()) return null;
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(_panelSettings);
            if (string.IsNullOrEmpty(assetPath)) return null;
            return Path.GetDirectoryName(assetPath);
        }
    }

    /// <summary>
    /// Default instance folder path for new projects (scene + GameObject + instanceId). Used when creating folder and assets.
    /// When useSceneNameAsRootFolder is true (default), folder is created under {SceneDirectory}/{SceneName}/.
    /// When false, folder is created directly under {SceneDirectory}/ (next to the scene file).
    /// When this runner is a prefab in the Project (not in a scene), returns a folder next to the prefab asset.
    /// </summary>
    string GetDefaultInstanceFolderPath(bool useSceneNameAsRootFolder) {
        var scenePath = gameObject.scene.path;
        if (!string.IsNullOrEmpty(scenePath)) {
            var sceneDir = Path.GetDirectoryName(scenePath);
            if (string.IsNullOrEmpty(sceneDir)) return null;

            var root = useSceneNameAsRootFolder
                ? Path.Combine(sceneDir, Path.GetFileNameWithoutExtension(scenePath))
                : sceneDir;

            return Path.GetFullPath(Path.Combine(ProjectRoot, root.Replace('/', Path.DirectorySeparatorChar), $"{gameObject.name}_{InstanceId}"));
        }

        // Prefab in Project (not in a scene): create project folder next to the prefab asset
        var prefabPath = GetPrefabAssetPath();
        if (string.IsNullOrEmpty(prefabPath) || !prefabPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) return null;

        var prefabDir = Path.GetDirectoryName(prefabPath);
        if (string.IsNullOrEmpty(prefabDir)) return null;

        var prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        var relativeDir = Path.Combine(prefabDir, $"{prefabName}_{InstanceId}").Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(ProjectRoot, relativeDir));
    }

    /// <summary>
    /// Returns the asset path of the prefab when this runner is a prefab in the Project (not in a scene). Null otherwise.
    /// </summary>
    string GetPrefabAssetPath() {
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject);
        if (stage != null && !string.IsNullOrEmpty(stage.assetPath)) return stage.assetPath;
        var path = UnityEditor.AssetDatabase.GetAssetPath(transform.root.gameObject);
        return !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? path : null;
    }
#endif

    /// <summary>
    /// Working directory path (ignored by Unity due to ~ suffix): {InstanceFolder}/~/
    /// </summary>
    public string WorkingDirFullPath {
        get {
            var instanceFolder = InstanceFolder;
            if (string.IsNullOrEmpty(instanceFolder)) return null;
            return Path.Combine(instanceFolder, "~");
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
    /// Asset path for the PanelSettings (Unity relative path for AssetDatabase).
    /// </summary>
    public string PanelSettingsAssetPath {
        get {
#if UNITY_EDITOR
            var folder = InstanceFolderAssetPath;
            if (string.IsNullOrEmpty(folder)) return null;
            return folder + "/PanelSettings.asset";
#else
            return null;
#endif
        }
    }

    /// <summary>
    /// Asset path for the VisualTreeAsset (Unity relative path for AssetDatabase).
    /// </summary>
    public string VisualTreeAssetPath {
        get {
#if UNITY_EDITOR
            var folder = InstanceFolderAssetPath;
            if (string.IsNullOrEmpty(folder)) return null;
            return folder + "/UIDocument.uxml";
#else
            return null;
#endif
        }
    }

    /// <summary>
    /// True when this runner is in a saved scene; false when it is a prefab in the Project (not placed in a scene).
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
    /// Ensures project folder and PanelSettings/VisualTreeAsset exist. Creates folder and assets when PanelSettings not set.
    /// PanelSettings is the single marker for the project folder (no separate ProjectConfig).
    /// </summary>
    public void EnsureProjectFolderAndAssets(bool useSceneNameAsRootFolder = true) {
        if (_panelSettings != null) return;

        var instanceFolder = GetDefaultInstanceFolderPath(useSceneNameAsRootFolder);
        if (string.IsNullOrEmpty(instanceFolder)) return;

        if (!Directory.Exists(instanceFolder)) {
            Directory.CreateDirectory(instanceFolder);
            var workingDir = Path.Combine(instanceFolder, "~");
            Directory.CreateDirectory(workingDir);
            Debug.Log($"[JSRunner] Created working directory: {workingDir}");
        } else {
            var oldWorkingDir = Path.Combine(instanceFolder, $"{gameObject.name}~");
            var newWorkingDir = Path.Combine(instanceFolder, "~");
            if (Directory.Exists(oldWorkingDir) && !string.Equals(oldWorkingDir, newWorkingDir, StringComparison.OrdinalIgnoreCase)) {
                try {
                    Directory.Move(oldWorkingDir, newWorkingDir);
                    Debug.Log($"[JSRunner] Migrated working directory to ~");
                } catch (Exception ex) {
                    Debug.LogWarning($"[JSRunner] Could not rename {gameObject.name}~ to ~: {ex.Message}");
                }
            } else if (!Directory.Exists(newWorkingDir)) {
                Directory.CreateDirectory(newWorkingDir);
            }
        }

        var relativeDir = Path.GetRelativePath(ProjectRoot, instanceFolder).Replace(Path.DirectorySeparatorChar, '/');
        var psAssetPath = relativeDir + "/PanelSettings.asset";
        var existingPS = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(psAssetPath);
        if (existingPS != null) {
            _panelSettings = existingPS;
            UnityEditor.EditorUtility.SetDirty(this);
            SyncVisualTreeAssetFromPanelSettingsFolder();
            EnsureUIDocumentInEditor();
            return;
        }

        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1920, 1080);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 0.5f;
        if (_defaultThemeStylesheet != null) ps.themeStyleSheet = _defaultThemeStylesheet;
        UnityEditor.AssetDatabase.CreateAsset(ps, psAssetPath);
        _panelSettings = ps;

        var uxmlPath = Path.Combine(instanceFolder, "UIDocument.uxml");
        var uxmlContent = @"<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
</ui:UXML>";
        File.WriteAllText(uxmlPath, uxmlContent);
        var vtaAssetPath = relativeDir + "/UIDocument.uxml";
        UnityEditor.AssetDatabase.ImportAsset(vtaAssetPath);
        _visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(vtaAssetPath);

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        EnsureUIDocumentInEditor();
        Debug.Log($"[JSRunner] Created project folder and PanelSettings: {psAssetPath}");
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

        // Write UXML file (full path for filesystem)
        var uxmlFullPath = Path.Combine(instanceFolder, "UIDocument.uxml");
        File.WriteAllText(uxmlFullPath, uxmlContent);

        var assetPath = VisualTreeAssetPath;
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

    /// <summary>
    /// True when PanelSettings is assigned and its folder is a valid project folder:
    /// the folder contains a "~" subfolder or an "app.js" / "app.js.txt" file.
    /// When false, we behave as if Panel Settings is not attached (no paths, no file creation).
    /// </summary>
    public bool IsPanelSettingsInValidProjectFolder() {
        if (_panelSettings == null) return false;
        var assetPath = UnityEditor.AssetDatabase.GetAssetPath(_panelSettings);
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) return false;
        var dir = Path.GetDirectoryName(assetPath);
        if (string.IsNullOrEmpty(dir)) return false;
        var normalized = dir.Replace('/', Path.DirectorySeparatorChar);
        var instanceFolder = Path.Combine(ProjectRoot, normalized);
        if (string.IsNullOrEmpty(instanceFolder) || !Directory.Exists(instanceFolder)) return false;
        var tildeDir = Path.Combine(instanceFolder, "~");
        var appJs = Path.Combine(instanceFolder, "app.js");
        var appJsTxt = Path.Combine(instanceFolder, "app.js.txt");
        return Directory.Exists(tildeDir) || File.Exists(appJs) || File.Exists(appJsTxt);
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

    /// <summary>
    /// Gets or adds UIDocument at runtime and assigns PanelSettings/VisualTreeAsset. No UIDocument in editor.
    /// </summary>
    bool EnsureUIDocument() {
        if (_uiDocument == null) {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) {
                _uiDocument = gameObject.AddComponent<UIDocument>();
            }
        }
#if UNITY_EDITOR
        bool valid = _panelSettings != null && IsPanelSettingsInValidProjectFolder();
        if (!valid) return false;
        if (_uiDocument.panelSettings != _panelSettings)
            _uiDocument.panelSettings = _panelSettings;
        if (_visualTreeAsset != null && _uiDocument.visualTreeAsset != _visualTreeAsset)
            _uiDocument.visualTreeAsset = _visualTreeAsset;
#else
        if (_panelSettings != null && _uiDocument.panelSettings != _panelSettings)
            _uiDocument.panelSettings = _panelSettings;
        if (_visualTreeAsset != null && _uiDocument.visualTreeAsset != _visualTreeAsset)
            _uiDocument.visualTreeAsset = _visualTreeAsset;
#endif
        return _uiDocument != null && _uiDocument.rootVisualElement != null;
    }

    void Start() {
        if (!Application.isPlaying) return; // [ExecuteAlways] guard
        try {
#if UNITY_EDITOR
            if (_panelSettings != null && !IsPanelSettingsInValidProjectFolder()) {
                Debug.LogError("[JSRunner] Panel Settings is not valid: its folder must contain a '~' subfolder or an 'app.js' file. Assign a PanelSettings from a valid project folder or use Initialize.");
                return;
            }
#endif
            if (!EnsureUIDocument()) {
                Debug.LogError("[JSRunner] UIDocument could not be created or rootVisualElement is null. Assign PanelSettings and VisualTreeAsset (e.g. via Initialize).");
                return;
            }

            Initialize();
            _initialized = true;
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Start() exception: {ex}");
        }
    }

    void OnEnable() {
        if (!_instances.Contains(this))
            _instances.Add(this);
#if UNITY_EDITOR
        if (!Application.isPlaying) {
            // Defer to let UIDocument panel settle after domain reload
            UnityEditor.EditorApplication.delayCall += TryStartEditModePreview;
            return;
        }
#endif
        if (!_initialized) {
            // First enable - let Start() handle initialization
            return;
        }

        // Re-enable - reload to reconnect to new rootVisualElement
        ReloadOnEnable();
    }

    void OnDisable() {
        _instances.Remove(this);
#if UNITY_EDITOR
        if (!Application.isPlaying) {
            StopEditModePreview();
            return;
        }
#endif
    }

    void ReloadOnEnable() {
        if (!EnsureUIDocument()) {
            Debug.LogWarning("[JSRunner] Reload on enable skipped: UIDocument is not ready yet.");
            return;
        }

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
        if (!IsSceneSaved) {
            Debug.LogError("[JSRunner] Scene must be saved before JSRunner can initialize. Save the scene and enter Play mode again.");
            return;
        }

        // Only run when project was initialized (PanelSettings marks the folder)
        if (_panelSettings == null) {
            return;
        }

        // When Panel Settings is not valid (folder has no ~ or app.js), do nothing in Play mode
        if (!IsPanelSettingsInValidProjectFolder()) {
            return;
        }

        // Sync VisualTreeAsset from PanelSettings folder if not already set
        if (_visualTreeAsset == null) {
            var vtaPath = VisualTreeAssetPath;
            if (!string.IsNullOrEmpty(vtaPath)) {
                var existingVTA = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(vtaPath);
                if (existingVTA != null) {
                    _visualTreeAsset = existingVTA;
                    UnityEditor.EditorUtility.SetDirty(this);
                } else {
                    CreateDefaultVisualTreeAsset();
                }
            }
        }

        // UIDocument is added at runtime and assigned from _panelSettings/_visualTreeAsset
        EnsureUIDocument();

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

        // Skip scaffolding/npm when already initialized (e.g. via Initialize button); just load and run
        bool alreadyInitialized = HasPackageJson && HasNodeModules && File.Exists(entryFile);
        if (!alreadyInitialized) {
            ScaffoldDefaultFiles();
            ExtractCartridges();
            EnsureEntryFile();
            UnityEditor.AssetDatabase.Refresh();
        }

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

        // Expose the working directory to JS for asset path resolution
        var escapedWorkingDir = CartridgeUtils.EscapeJsString(_bridge.WorkingDir);
        _bridge.Eval($"globalThis.__workingDir = '{escapedWorkingDir}'");

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
        // Use FileShare.ReadWrite so we can read even when another process (esbuild, Unity
        // asset importer) has the file open for writing. File.OpenRead uses FileShare.Read
        // which fails on Windows when a write handle is held by another process.
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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

        if (_uiDocument == null || _uiDocument.rootVisualElement == null) {
            Debug.LogWarning("[JSRunner] Reload skipped: UIDocument or rootVisualElement is null");
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

            // 4. Load and run script (retry on sharing violation while esbuild is still writing)
            var entryPath = EntryFileFullPath;
            string code = null;
            for (int i = 0; i < 5; i++) {
                try {
                    code = File.ReadAllText(entryPath);
                    break;
                } catch (IOException) {
                    if (i == 4) throw;
                    Thread.Sleep(50);
                }
            }
            RunScript(code, Path.GetFileName(entryPath));

            // 5. Update state
            _lastModifiedTime = File.GetLastWriteTime(EntryFileFullPath);
            _lastContentHash = ComputeFileHash(EntryFileFullPath);
            _lastReloadTime = DateTime.Now;
            _reloadCount++;
            Debug.Log($"[JSRunner] Reloaded ({_reloadCount})");

            // Force UI Toolkit to process layout so the Game view reflects
            // the new content when the editor regains focus.
            if (_editModePreviewActive)
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        } catch (Exception ex) {
            var message = TranslateErrorMessage(ex.Message);
            Debug.LogError($"[JSRunner] Reload failed: {message}");
            // If edit-mode preview is active, stop it to avoid a broken state
            // (bridge may be disposed but EditModeTick still firing)
            if (_editModePreviewActive)
                StopEditModePreview();
        }
    }

    void CheckForFileChanges() {
        if (!_liveReload || !_scriptLoaded) return;
        if (Time.realtimeSinceStartup < _nextPollTime) return;

        _nextPollTime = Time.realtimeSinceStartup + _pollInterval;

        try {
            if (!File.Exists(EntryFileFullPath)) return;

            // Content hash is the source of truth for detecting changes.
            // mtime is unreliable on Windows (NTFS tunneling can return stale timestamps
            // when files are deleted and recreated, which is how esbuild writes output).
            var currentHash = ComputeFileHash(EntryFileFullPath);
            if (currentHash == _lastContentHash) return;

            _lastContentHash = currentHash;
            Reload();
        } catch (IOException) {
            // File might be locked by build process, skip this poll
        } catch (Exception ex) {
            Debug.LogWarning($"[JSRunner] Error checking file: {ex.Message}");
        }
    }

    // MARK: Edit-Mode Preview

    void TryStartEditModePreview() {
        if (this == null || Application.isPlaying) return;
        if (_editModePreviewActive) return;
        if (_panelSettings == null || !IsPanelSettingsInValidProjectFolder()) return;

        // Need a built bundle to preview
        var entryFile = EntryFileFullPath;
        if (string.IsNullOrEmpty(entryFile) || !File.Exists(entryFile)) return;

        // Ensure UIDocument exists and has a valid rootVisualElement
        _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument == null || _uiDocument.rootVisualElement == null) return;

        try {
            // Reuse existing init infrastructure
            _uiDocument.rootVisualElement.Clear();
            _uiDocument.rootVisualElement.styleSheets.Clear();
            InitializeBridge();

            var code = File.ReadAllText(entryFile);
            RunScript(code, Path.GetFileName(entryFile));

            // Set up file watching for live reload in edit-mode
            _lastModifiedTime = File.GetLastWriteTime(entryFile);
            _lastContentHash = ComputeFileHash(entryFile);
            _nextPollTime = Time.realtimeSinceStartup + _pollInterval;

            _editModePreviewActive = true;
            UnityEditor.EditorApplication.update += EditModeTick;

            // Watch the entry file so we can wake the editor when it's unfocused.
            // EditorApplication.update throttles when the editor loses focus; the
            // FileSystemWatcher fires from a thread pool thread regardless of focus
            // and QueuePlayerLoopUpdate wakes the editor to process the change.
            try {
                _editModeWatcher = new FileSystemWatcher(
                    Path.GetDirectoryName(entryFile), Path.GetFileName(entryFile));
                _editModeWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                _editModeWatcher.Changed += (_, __) =>
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                _editModeWatcher.EnableRaisingEvents = true;
            } catch {
                // Non-critical: fall back to polling-only (e.g., network drives)
            }

            // Force initial repaint so the Game view shows the UI
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();

            EditModePreviewStarted?.Invoke(this);
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Edit-mode preview failed: {ex.Message}");
            // Clean up partial init
            _bridge?.Dispose();
            _bridge = null;
            _scriptLoaded = false;
        }
    }

    void StopEditModePreview() {
        if (!_editModePreviewActive) return;

        UnityEditor.EditorApplication.update -= EditModeTick;
        _editModeWatcher?.Dispose();
        _editModeWatcher = null;
        _editModePreviewActive = false;

        if (_uiDocument != null && _uiDocument.rootVisualElement != null) {
            _uiDocument.rootVisualElement.Clear();
            _uiDocument.rootVisualElement.styleSheets.Clear();
        }

        _bridge?.Dispose();
        _bridge = null;
        _scriptLoaded = false;
    }

    void EditModeTick() {
        if (this == null || !_editModePreviewActive || _bridge == null) return;
        if (Application.isPlaying) {
            // PlayMode started - stop edit-mode preview, Start() will take over
            StopEditModePreview();
            return;
        }
        // Always check for file changes so TSX edits apply even when overlay filter disables ticking
        CheckForFileChanges();
        if (EditModeUpdateFilter != null && !EditModeUpdateFilter(this)) {
            return;
        }

        // Throttle tick rate to ~30Hz
        if (Time.realtimeSinceStartup < _nextEditModeTick) return;
        _nextEditModeTick = Time.realtimeSinceStartup + EditModeTickInterval;

        _bridge.Tick();
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
        if (!Application.isPlaying) return; // [ExecuteAlways] guard - edit-mode uses EditorApplication.update
#if UNITY_EDITOR
        // Editor: Use Unity's Update loop to drive the tick
        if (_scriptLoaded && (PlayModeUpdateFilter == null || PlayModeUpdateFilter(this))) {
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
#if UNITY_EDITOR
        if (_editModePreviewActive) {
            StopEditModePreview();
        }
#endif
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
        // UIDocument is added at runtime only. Project folder and PanelSettings are created when user clicks Initialize.
        PopulateDefaultFiles();
    }

    /// <summary>
    /// When PanelSettings is assigned or cleared, sync VisualTreeAsset from the same folder.
    /// In editor: add UIDocument with populated Panel Settings and Visual Tree when assigned; remove UIDocument when cleared.
    /// In Play mode: push to UIDocument so both use the same assets.
    /// </summary>
    void OnValidate() {
        bool shouldUsePanelSettings = _panelSettings != null && IsPanelSettingsInValidProjectFolder();
        if (_panelSettings == null || !shouldUsePanelSettings) {
            if (_panelSettings == null && _visualTreeAsset != null) {
                _visualTreeAsset = null;
                UnityEditor.EditorUtility.SetDirty(this);
            }
            // Remove UIDocument when Panel Settings is cleared or invalid (deferred so removal isn't ignored during OnValidate)
            if (!Application.isPlaying) {
                var ud = GetComponent<UIDocument>();
                if (ud != null) {
                    var toRemove = ud;
                    UnityEditor.EditorApplication.delayCall += () => {
                        if (toRemove != null && (_panelSettings == null || !IsPanelSettingsInValidProjectFolder()))
                            UnityEditor.Undo.DestroyObjectImmediate(toRemove);
                    };
                }
            }
            if (!shouldUsePanelSettings) return;
        }
        SyncVisualTreeAssetFromPanelSettingsFolder();
        var udoc = GetComponent<UIDocument>();
        if (Application.isPlaying) {
            if (udoc != null && shouldUsePanelSettings) {
                udoc.panelSettings = _panelSettings;
                udoc.visualTreeAsset = _visualTreeAsset;
            }
        } else {
            // Defer so we never AddComponent during OnValidate (SendMessage not allowed there)
            UnityEditor.EditorApplication.delayCall += () => {
                if (this == null) return;
                if (_panelSettings != null && IsPanelSettingsInValidProjectFolder())
                    EnsureUIDocumentInEditor();
            };
            // Also try to start edit-mode preview when PanelSettings changes
            if (!_editModePreviewActive) {
                UnityEditor.EditorApplication.delayCall += TryStartEditModePreview;
            }
        }
    }

    /// <summary>Adds UIDocument if missing and syncs Panel Settings / Visual Tree. Call after assigning _panelSettings (e.g. from Initialize). Only runs when Panel Settings is in a valid project folder.</summary>
    void EnsureUIDocumentInEditor() {
        if (_panelSettings == null || !IsPanelSettingsInValidProjectFolder()) return;
        SyncVisualTreeAssetFromPanelSettingsFolder();
        var udoc = GetComponent<UIDocument>();
        if (udoc == null)
            udoc = UnityEditor.Undo.AddComponent<UIDocument>(gameObject);
        if (udoc != null && (udoc.panelSettings != _panelSettings || udoc.visualTreeAsset != _visualTreeAsset)) {
            UnityEditor.Undo.RecordObject(udoc, "Sync UIDocument to Panel Settings");
            udoc.panelSettings = _panelSettings;
            udoc.visualTreeAsset = _visualTreeAsset;
            UnityEditor.EditorUtility.SetDirty(udoc);
        }
    }

    void SyncVisualTreeAssetFromPanelSettingsFolder() {
        if (_panelSettings == null) return;
        var assetPath = UnityEditor.AssetDatabase.GetAssetPath(_panelSettings);
        if (string.IsNullOrEmpty(assetPath)) return;
        var folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        if (string.IsNullOrEmpty(folder)) return;
        var vtaPath = folder + "/UIDocument.uxml";
        var vta = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(vtaPath);
        if (_visualTreeAsset != vta) {
            _visualTreeAsset = vta;
            UnityEditor.EditorUtility.SetDirty(this);
        }
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
