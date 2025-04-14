using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Puerts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    [CreateAssetMenu(fileName = "EditorScriptEngine", menuName = "OneJS/EditorScriptEngine")]
    public class EditorScriptEngine : ScriptableObject, IScriptEngine, IDisposable {
        #region Public Fields
        [Tooltip("Folder name under your project root that you want to use for this EditorScriptEngine.")]
        public string folderName = "Editor";

        [PairMapping("path", "textAsset", ":")]
        public DefaultFileMapping[] defaultFiles;
        public ScriptEngine scriptEnginePrefab;
        public StyleSheet editorStyleSheet;
        public TextAsset packageJsonOverride;
        public TextAsset tasksJsonOverride;
        public TextAsset appJsOverride;

        public TextAsset[] preloads;
        public ObjectMappingPair[] globalObjects;
        public StyleSheet[] styleSheets;
        public DTSGenerator dtsGenerator;
        [Tooltip("Turns on Live Reload.")]
        public bool devMode = true;
        [Tooltip("Enables extra logging.")]
        public bool extraLogging;

        public bool initialized; // Denotes whether or not the working directory has been initialized
        #endregion

        #region Events
        public event Action OnReload;
        public event Action OnDispose;
        
        public event Action OnPreInit;
        #endregion

        #region Private Fields
        JsEnv _jsEnv;
        RendererRegistry _rendererRegistry;
        EditorDocument _document;
        EditorEngineHost _engineHost;
        FileSystemWatcher _fileWatcher;
        #endregion

        #region Lifecycle
        void Reset() {
            if (preloads == null || preloads.Length == 0) {
                preloads = scriptEnginePrefab.preloads.ToArray();
            }
            if (globalObjects == null || globalObjects.Length == 0) {
                globalObjects = scriptEnginePrefab.globalObjects.ToArray();
            }
            if (styleSheets == null || styleSheets.Length == 0) {
                // styleSheets = scriptEnginePrefab.styleSheets.ToArray();
                styleSheets = new[] { editorStyleSheet };
            }
        }

        void OnEnable() {
            if (!initialized)
                return;
            // _rendererRegistry should not be reset in Init() because it needs to be persisted across reloads
            _rendererRegistry = new RendererRegistry(this);
            // *May need to* introduce a 1-frame delay here prevents reimporting errors with Puerts. When Puerts
            // loads its init.mjs file, initializing a new JsEnv in the same frame can trigger these errors.
            // But this does interfere with the rendering of Custom Editors that depend on the engine being ready.
            EditorApplication.delayCall += Init;
            // try {
            //     Init();
            // } catch (Exception) {
            //     // Maybe this is the best way to handle the above issue
            //     EditorApplication.delayCall += Init;
            // }
        }

        void OnDisable() {
            if (!initialized)
                return;
            Dispose();
        }

        public void Init() {
            ExtractIfNotFound();
            _jsEnv = new JsEnv();
            OnPreInit?.Invoke();
            _engineHost = new EditorEngineHost(this);
            _document = new EditorDocument(this);
            foreach (var preload in preloads) {
                _jsEnv.Eval(preload.text);
            }
            var addToGlobal = _jsEnv.Eval<Action<string, object>>(@"__addToGlobal");
            addToGlobal("___document", _document);
            addToGlobal("___workingDir", WorkingDir);
            addToGlobal("___engineHost", _engineHost);
            addToGlobal("resource", new Resource(this));
            addToGlobal("onejs", _engineHost);
            foreach (var obj in globalObjects) {
                addToGlobal(obj.name, obj.obj);
            }
            EditorApplication.update += Tick;
            StartWatching();
            Run();
            initialized = true;
        }

        public void Dispose() {
            OnDispose?.Invoke();
            EditorApplication.update -= Tick;
            StopWatching();
            if (_document != null) {
                _document.Dispose();
                _document = null;
            }
            if (_jsEnv != null) {
                _jsEnv.Dispose();
                _jsEnv = null;
                _document = null;
            }
        }

        void Tick() {
            _jsEnv.Tick();
        }
        #endregion

        #region Public Methods
        public void Reload() {
            OnReload?.Invoke();
            Dispose();
            Init();
        }

        public void Run() {
            if (!File.Exists(ScriptFilePath))
                return;
            if (_rendererRegistry == null) // _rendererRegistry can be null during EditorScriptEngine's InitWorkingDir()
                _rendererRegistry = new RendererRegistry(this);
            var code = File.ReadAllText(ScriptFilePath);
            _jsEnv.Eval(code, "@outputs/esbuild/app.js");
            // ReSelect(); //
            // BaseEditor.Instance?.Refresh();
            _rendererRegistry.ReRenderAll();
            RefreshStyleSheets();
        }

        public void Execute(string code) {
            _jsEnv.Eval(code);
        }

        /// <summary>
        /// Renders the editor UI for the target object (e.g. Editor or EditorWindow). The renderer
        /// should already be registered from the JS side. If not, it will do nothing.
        /// </summary>
        public void Render(UnityEngine.Object target, VisualElement root) {
            var type = target.GetType();
            if (_rendererRegistry.TryGetRendererInfo(type, out var rendererInfo)) {
                // Debug.Log($"Renderers count: {_rendererRegistry.RenderersCount}");
                // Debug.Log(_rendererRegistry.RenderersTypesString);
                ApplyStyleSheets(root);
                rendererInfo.target = target;
                rendererInfo.roots.Add(root);
                rendererInfo.render(rendererInfo.target, root);
            }
        }

        public void RegisterRenderer(Type type, Action<UnityEngine.Object, VisualElement> render) {
            _rendererRegistry.Register(type, render);
        }

        public void RemoveRenderRoot(Type type, VisualElement root) {
            if (_rendererRegistry.TryGetRendererInfo(type, out var rendererInfo)) {
                rendererInfo.roots.Remove(root);
            }
        }

        public void ApplyStyleSheets(VisualElement ve) {
            foreach (var styleSheet in styleSheets) {
                if (!ve.styleSheets.Contains(styleSheet)) {
                    ve.styleSheets.Add(styleSheet);
                }
            }
            _document.ApplyRuntimeStyleSheets(ve);
        }

        // [MenuItem("Tools/List Root VisualElements")]
        // public static void ListRootVisualElements() {
        //     // Find all instances of EditorWindow
        //     EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
        //
        //     foreach (EditorWindow window in windows) {
        //         // Access the root VisualElement
        //         VisualElement rootVisual = window.rootVisualElement;
        //         Debug.Log($"Window: {window.titleContent.text}, Root VisualElement: {rootVisual.name}");
        //     }
        // }
        #endregion

        #region Properties
        public string WorkingDir {
            get {
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, folderName);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }
        public string ScriptFilePath => Path.Combine(WorkingDir, "@outputs/esbuild/app.js");

        public EditorDocument document => _document;

        public JsEnv JsEnv => _jsEnv;

        public EditorEngineHost EngineHost => _engineHost;
        #endregion

        #region Runner
        void StartWatching() {
            _fileWatcher = new FileSystemWatcher();
            _fileWatcher.Path = Path.GetDirectoryName(ScriptFilePath);
            _fileWatcher.Filter = Path.GetFileName(ScriptFilePath); // Only watch the specific file.

            _fileWatcher.Changed += OnFileChanged;
            // _fileWatcher.Deleted += OnFileDeleted;
            // _fileWatcher.Renamed += OnFileRenamed;

            _fileWatcher.EnableRaisingEvents = true;

            if (extraLogging)
                Debug.Log($"[OneJS Editor] Loaded: {folderName} ({ScriptFilePath})");
        }

        void StopWatching() {
            if (_fileWatcher != null) {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
                // Debug.Log("Stopped watching: " + ScriptFilePath);
            }
        }

        void OnFileChanged(object source, FileSystemEventArgs e) {
            // Debug.Log("File: " + e.FullPath + " " + e.ChangeType);
            EditorApplication.update += RunOnMainThread;
        }

        void RunOnMainThread() {
            EditorApplication.update -= RunOnMainThread;
            if (devMode) {
                Reload();
            }
        }

        /// <summary>
        /// This is for convenience for Live-Reload. Stylesheets need explicit refreshing
        /// when Unity Editor doesn't have focus. Otherwise, stylesheet changes won't be
        /// reflected in the Editor until it gains focus.
        /// </summary>
        void RefreshStyleSheets() {
            foreach (var ss in styleSheets) {
                if (ss != null) {
                    string assetPath = AssetDatabase.GetAssetPath(ss);
                    if (!string.IsNullOrEmpty(assetPath)) {
                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    }
                }
            }
        }
        #endregion

        #region Bundling
        void ExtractIfNotFound() {
            if (scriptEnginePrefab == null) { // Probably needed for reimporting caveats
                return;
            }
            var bundler = scriptEnginePrefab.GetComponent<Bundler>();
            var mappings = bundler.defaultFiles.ToList();
            mappings.Add(new DefaultFileMapping() { path = "@outputs/esbuild/app.js", textAsset = appJsOverride });

            foreach (var mapping in mappings) {
                CreateIfNotFound(mapping);
            }
            CreateVSCodeSettingsJsonIfNotFound();
        }

        void CreateIfNotFound(DefaultFileMapping mapping) {
            var path = Path.Combine(WorkingDir, mapping.path);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(path)) {
                var text = mapping.textAsset.text;
                if (mapping.path == ".vscode/tasks.json")
                    text = tasksJsonOverride.text;
                else if (mapping.path == "package.json")
                    text = packageJsonOverride.text;
                File.WriteAllText(path, text);
                Debug.Log($"'{mapping.path}' wasn't found. A new one was created.");
            }
        }

        void CreateVSCodeSettingsJsonIfNotFound() {
            var path = Path.Combine(WorkingDir, ".vscode/settings.json");
            // Create if path doesn't exist
            if (!File.Exists(path)) {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var jsonDict = JsonConvert.DeserializeObject<Dictionary<string, object>>("{}");

                jsonDict["window.title"] = Application.productName + " " + folderName;

                string updatedJsonString = JsonConvert.SerializeObject(jsonDict, Formatting.Indented);
                File.WriteAllText(path, updatedJsonString);
            }
        }

        /// <summary>
        /// Root folder at path still remains
        /// </summary>
        void DeleteEverythingInPath(string path) {
            var dotGitPath = Path.Combine(path, ".git");
            if (Directory.Exists(dotGitPath)) {
                Debug.Log($".git folder detected at {path}, aborting deletion.");
                return;
            }
            if (Directory.Exists(path)) {
                var di = new DirectoryInfo(path);
                foreach (FileInfo file in di.EnumerateFiles()) {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.EnumerateDirectories()) {
                    dir.Delete(true);
                }
            }
        }

        [ContextMenu("Purge")]
        void Purge() {
            if (EditorUtility.DisplayDialog("Are you absolutely sure?",
                    $"This will delete everything in your {folderName} folder and re-populate with default files. Are you sure you want to proceed?",
                    "Confirm", "Cancel")) {
                DeleteEverythingInPath(WorkingDir);
                ExtractIfNotFound();
            }
        }
        #endregion

        #region Misc
        [ContextMenu("Set Initialized")]
        void SetInitialized() {
            initialized = true;
        }

        // TODO need to find workaround for repainting issue
        // https://forum.unity.com/threads/is-there-something-like-oninspectorupdate-for-ui-toolkit.1223010/
        public VisualElement CreateInspectorVE(string filePath, object target) {
            var code = File.ReadAllText(Path.Combine(WorkingDir, "@outputs/esbuild", filePath));
            var func = _jsEnv.Eval<Func<object, Dom.Dom>>(code);

            var dom = func(target);
            return dom.ve;
        }
        #endregion
    }
}