using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jint;
using Jint.CommonJS;
using Jint.Runtime.Interop;
using OneJS.Dom;
using OneJS.Engine;
using OneJS.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OneJS {
    [RequireComponent(typeof(UIDocument), typeof(CoroutineUtil))]
    public class ScriptEngine : MonoBehaviour {
        public string WorkingDir {
            get {
#if UNITY_EDITOR
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!,
                    _editorModeWorkingDirInfo.relativePath);
                if (_editorModeWorkingDirInfo.baseDir == EditorModeWorkingDirInfo.EditorModeBaseDir.PersistentDataPath)
                    path = Path.Combine(Application.persistentDataPath, _editorModeWorkingDirInfo.relativePath);
                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }
                return path;
#else
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!,
                    _playerModeWorkingDirInfo.relativePath);
                if (_playerModeWorkingDirInfo.baseDir == PlayerModeWorkingDirInfo.PlayerModeBaseDir.PersistentDataPath)
                    path = Path.Combine(Application.persistentDataPath, _playerModeWorkingDirInfo.relativePath);
                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }
                return path;
#endif
            }
        }

        public Jint.Engine JintEngine => _engine;
        public ModuleLoadingEngine ModuleEngine => _cjsEngine;
        public Dom.Document Document => _document;
        public Dom.Dom DocumentBody => _document.body;
        public int Tick => _tick;
        public DateTime StartTime { get; private set; }

        #region public properties for ScriptEngine options
        public string[] Assemblies { get { return _assemblies; } set { _assemblies = value; } }
        public string[] Extensions { get { return _extensions; } set { _extensions = value; } }
        public NamespaceModulePair[] Namespaces { get { return _namespaces; } set { _namespaces = value; } }
        public StaticClassModulePair[] StaticClasses { get { return _staticClasses; } set { _staticClasses = value; } }
        public ObjectModulePair[] Objects { get { return _objects; } set { _objects = value; } }
        public List<string> PreloadedScripts { get { return _preloadedScripts; } set { _preloadedScripts = value; } }
        public List<string> PostloadedScripts { get { return _postloadedScripts; } set { _postloadedScripts = value; } }
        public StyleSheet[] StyleSheets { get { return _styleSheets; } set { _styleSheets = value; } }
        public int[] Breakpoints { get { return _breakpoints; } set { _breakpoints = value; } }
        public bool CatchDotNetExceptions {
            get { return _catchDotNetExceptions; }
            set { _catchDotNetExceptions = value; }
        }
        public bool AllowReflection { get { return _allowReflection; } set { _allowReflection = value; } }
        public bool AllowGetType { get { return _allowGetType; } set { _allowGetType = value; } }
        public int Timeout { get { return _timeout; } set { _timeout = value; } }
        public int RecursionDepth { get { return _recursionDepth; } set { _recursionDepth = value; } }
        public EditorModeWorkingDirInfo EditorModeWorkingDirInfo {
            get { return _editorModeWorkingDirInfo; }
            set { _editorModeWorkingDirInfo = value; }
        }
        public PlayerModeWorkingDirInfo PlayerModeWorkingDirInfo {
            get { return _playerModeWorkingDirInfo; }
            set { _playerModeWorkingDirInfo = value; }
        }
        #endregion

        public event Action OnPostInit;
        public event Action OnReload;

        [Tooltip("Include any assembly you'd want to access from Javascript.")] [SerializeField]
        [PlainString]
        string[] _assemblies = new[] {
            "UnityEngine.CoreModule", "UnityEngine.PhysicsModule", "UnityEngine.UIElementsModule",
            "UnityEngine.IMGUIModule", "UnityEngine.TextRenderingModule",
            "Unity.Mathematics", "OneJS"
        };


        [Tooltip("Extensions need to be explicitly added to the script engine. OneJS also provide some default ones.")]
        [SerializeField]
        [PlainString]
        string[] _extensions = new[] {
            "OneJS.Extensions.GameObjectExts",
            "OneJS.Extensions.ComponentExts",
            "OneJS.Extensions.ColorExts",
            "OneJS.Extensions.VisualElementExts",
            "UnityEngine.UIElements.PointerCaptureHelper"
        };


        [Tooltip("C# Namespace to JS Module mapping.")]
        [PairMapping("namespace", "module")]
        [SerializeField]
        NamespaceModulePair[] _namespaces = new[] {
            new NamespaceModulePair("System.Collections.Generic", "System/Collections/Generic"),
            new NamespaceModulePair("UnityEngine", "UnityEngine"),
            new NamespaceModulePair("UnityEngine.UIElements", "UnityEngine/UIElements"),
            new NamespaceModulePair("OneJS.Utils", "OneJS/Utils"),
        };

        [Tooltip("Static Class to JS Module mapping.")]
        [PairMapping("staticClass", "module")]
        [SerializeField]
        StaticClassModulePair[] _staticClasses = new[]
            { new StaticClassModulePair("Unity.Mathematics.math", "math") };

        [Tooltip(
            "Maps an Unity Object to a js module name (any string that you choose). Objects declared here will be accessible from Javascript via i.e. require(\"objname\"). You can also provide your own Type Definitions for better TS usage.")]
        [PairMapping("obj", "module")] [SerializeField]
        ObjectModulePair[] _objects = new ObjectModulePair[] { };

        [Tooltip("Scripts that you want to load before everything else")] [SerializeField]
        [PlainString]
        List<string> _preloadedScripts = new List<string>();

        [Tooltip("Scripts that you want to load after everything else")] [SerializeField]
        [PlainString]
        List<string> _postloadedScripts = new List<string>();

        [Tooltip("Include here any global USS you'd need. OneJS also provides a default one.")]
        [SerializeField]
        StyleSheet[] _styleSheets;

        [Tooltip("Screen breakpoints for responsive design.")] [SerializeField]
        int[] _breakpoints = new[] { 640, 768, 1024, 1280, 1536 };


        [Tooltip("Allows you to catch .Net error from within JS.")] [SerializeField]
        bool _catchDotNetExceptions = true;
        [Tooltip(
            "Sometimes errors may get lost in-between the interop or through anonymous functions. Use this to log these.")]
        [SerializeField]
        bool _logRedundantErrors = false;
        [Tooltip("Allow access to System.Reflection from Javascript")] [SerializeField]
        bool _allowReflection;
        [Tooltip("Allow access to .GetType() from Javascript")] [SerializeField]
        bool _allowGetType;
        [Tooltip("Memory Limit in MB. Set to 0 for no limit.")] [SerializeField] int _memoryLimit;
        [Tooltip("How long a script can execute in milliseconds. Set to 0 for no limit.")]
        [SerializeField] int _timeout;
        [Tooltip("Limit depth of calls to prevent deep recursion calls. Set to 0 for no limit.")]
        [SerializeField] int _recursionDepth;

        [PairMapping("baseDir", "relativePath", "/", "Editor WorkingDir")]
        [SerializeField] EditorModeWorkingDirInfo _editorModeWorkingDirInfo;
        [PairMapping("baseDir", "relativePath", "/", "Player WorkingDir")]
        [SerializeField] PlayerModeWorkingDirInfo _playerModeWorkingDirInfo;

        [SerializeField] int _selectedTab;

        UIDocument _uiDocument;
        Document _document;
        ModuleLoadingEngine _cjsEngine;
        Jint.Engine _engine;
        AsyncEngine.AsyncContext _asyncContext;

        List<Action> _engineReloadJSHandlers = new List<Action>();
        List<IClassStrProcessor> _classStrProcessors = new List<IClassStrProcessor>();
        List<Type> _globalFuncTypes;

        int _currentActionId;
        PriorityQueue<int, DateTime> _queuedActionIds = new PriorityQueue<int, DateTime>();
        Dictionary<int, QueuedAction> _queueLookup = new Dictionary<int, QueuedAction>();

        Assembly[] _loadedAssemblies;

        List<Action> _frameActions = new List<Action>();
        List<Action> _frameActionBuffer = new List<Action>();

        int _tick = 0;

        public void Awake() {
            _uiDocument = GetComponent<UIDocument>();
            _uiDocument.rootVisualElement.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            _uiDocument.rootVisualElement.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            _document = new Document(_uiDocument.rootVisualElement, this);
            _styleSheets.ToList().ForEach(s => _uiDocument.rootVisualElement.styleSheets.Add(s));

            _globalFuncTypes = this.GetType().Assembly.GetTypes()
                .Where(t => t.IsVisible && t.FullName.StartsWith("OneJS.Engine.JSGlobals")).ToList();
        }

        void Start() {
            InitEngine();
        }

        void LateUpdate() {
            _engine.ResetConstraints();

            _frameActionBuffer.AddRange(_frameActions);
            _frameActions.Clear();
            for (int i = 0; i < _frameActionBuffer.Count; i++) {
                _frameActionBuffer[i]();
            }
            _frameActionBuffer.Clear();

            while (_queuedActionIds.Count > 0 && _queuedActionIds.TryPeek(out int _, out DateTime time) &&
                   time <= DateTime.Now) {
                var qaid = _queuedActionIds.Dequeue();
                var qa = _queueLookup[qaid];
                if (!qa.cleared) {
                    qa.action();
                    qa = _queueLookup[qaid]; // in case the struct was changed (action cleared itself)
                    if (qa.requeue) {
                        qa.ResetDateTime();
                        _queueLookup[qa.id] = qa;
                        _queuedActionIds.Enqueue(qa.id, qa.dateTime);
                        continue;
                    }
                }
                _queueLookup.Remove(qa.id);
            }
            _tick++;
        }

        public void RunScript(string scriptPath) {
            var path = Path.Combine(WorkingDir, scriptPath);
            if (!File.Exists(path)) {
                Debug.LogError($"Script Path ({path}) doesn't exist.");
                return;
            }
            RunModule(scriptPath);
        }

        /// <summary>
        /// Engine will reload first then runs the script.
        /// Use this if you want to run the script with a brand new Engine.
        /// </summary>
        /// <param name="scriptPath">Relative to WorkingDir</param>
        public void ReloadAndRunScript(string scriptPath) {
            var path = Path.Combine(WorkingDir, scriptPath);
            if (!File.Exists(path)) {
                Debug.LogError($"File ({path}) doesn't exist.");
                return;
            }
            OnReload?.Invoke();
            CleanUp();
            InitEngine();
            RunModule(scriptPath);
        }

        public void RunScriptRaw(string scriptPath) {
            var path = Path.Combine(WorkingDir, scriptPath);
            if (!File.Exists(path)) {
                Debug.LogError($"Script Path ({path}) doesn't exist.");
                return;
            }
            _cjsEngine.RunMain(scriptPath);
        }

        public int QueueFrameAction(Action action) {
            _frameActions.Add(action);
            return _frameActions.Count - 1;
        }

        public void ClearFrameAction(int id) {
            if (_frameActions.Count > id) {
                _frameActions.RemoveAt(id);
            }
        }

        public int QueueAction(Action action, double milliseconds, bool requeue = false) {
            var id = ++_currentActionId;
            var qa = new QueuedAction(action, id, milliseconds, requeue);
            if (milliseconds == 0) { // Instant Actions will be treated as frame actions
                qa.id = QueueFrameAction(action);
                _queueLookup.Add(id, qa);
                return id;
            }
            _queuedActionIds.Enqueue(id, qa.dateTime);
            _queueLookup.Add(id, qa);
            return id;
        }

        public void ClearQueuedAction(int id) {
            if (_queueLookup.TryGetValue(id, out var queuedAction)) {
                if (queuedAction.timeout == 0) { // Instant Action was treated as frame action
                    ClearFrameAction(queuedAction.id);
                }
                queuedAction.cleared = true;
                queuedAction.requeue = false;
                _queueLookup[id] = queuedAction;
            }
        }

        /// <summary>
        /// This is a helper func for subscribing to the ScriptEngine.OnReload event.
        /// Will automatically take care of the cleaning up during engine reload. 
        /// </summary>
        public void RegisterReloadHandler(Action handler) {
            OnReload += handler;
            _engineReloadJSHandlers.Add(handler);
        }

        /// <summary>
        /// Apply all class string processors. 
        /// </summary>
        /// <param name="classString">String of class names</param>
        /// <param name="dom">The Dom that is setting the class attribute right now</param>
        public string ProcessClassStr(string classString, Dom.Dom dom) {
            foreach (var processor in _classStrProcessors) {
                classString = processor.ProcessClassStr(classString, dom);
            }
            return classString;
        }

        /// <summary>
        /// Add a processor for handling class names settings/changes
        /// </summary>
        public void RegisterClassStrProcessor(IClassStrProcessor processor) {
            _classStrProcessors.Add(processor);
        }

        void CleanUp() {
            _document.clearRuntimeStyleSheets();
            _engineReloadJSHandlers.ForEach((a) => { OnReload -= a; });
            _engineReloadJSHandlers.Clear();

            _queuedActionIds.Clear();
            _queueLookup.Clear();
            _currentActionId = 0;

            _frameActions.Clear();
            _frameActionBuffer.Clear();

            _globalFuncTypes.ForEach(t => {
                var flags = BindingFlags.Public | BindingFlags.Static;
                var mi = t.GetMethod("Reset", flags);
                if (mi == null)
                    return;
                mi.Invoke(null, new object[] { });
            });
            _loadedAssemblies = new Assembly[0];
        }

        /// <summary>
        /// Search loaded assemblies for a lowercase type name. Order of assemblies matter.
        /// First match wins.
        /// </summary>
        /// <param name="typeName">type name to search for</param>
        /// <returns>System.Type found by lowercase name.</returns>
        public System.Type FindVisualElementType(string typeName) {
            var typeNameL = typeName.ToLower();
            foreach (var assembly in _loadedAssemblies) {
                var typesToSearch = assembly.GetTypes();
                var type = typesToSearch.Where(t => t.Name.ToLower() == typeNameL)
                    .FirstOrDefault();
                if (type != null && type.IsSubclassOf(typeof(VisualElement))) {
                    return type;
                }
            }
            return null;
        }

        void InitEngine() {
            StartTime = DateTime.Now;
            _loadedAssemblies = _assemblies.Select((a) => {
#if UNITY_2022_2_OR_NEWER
                if (a == "UnityEngine.UIElementsNativeModule") {
                    return null;
                }
#endif
                try {
                    return Assembly.Load(a);
                } catch (Exception e) {
                    if (a != "Assembly-CSharp") {
                        Debug.Log(
                            $"ScriptEngine could not load assembly \"{a}\". Please check your string(s) in the INTEROP/Assemblies list.");
                    }
                    return null;
                }
            }).Where(a => a != null).ToArray();

            _asyncContext = new AsyncEngine.AsyncContext();
            _engine = new Jint.Engine(opts => {
                    opts.Interop.TrackObjectWrapperIdentity = false; // Unity too buggy with ConditionalWeakTable
                    opts.AllowClr(_loadedAssemblies);
                    _extensions.ToList().ForEach((e) => {
                        var type = AssemblyFinder.FindType(e);
                        if (type == null)
                            throw new Exception(
                                $"ScriptEngine could not load extension \"{e}\". Please check your string(s) in the `extensions` array.");
                        opts.AddExtensionMethods(type);
                    });
                    opts.AddObjectConverter(new AsyncEngine.TaskConverter(_asyncContext));
                    opts.AllowOperatorOverloading();

                    if (_catchDotNetExceptions) opts.CatchClrExceptions(ClrExceptionHandler);
                    if (_allowReflection) opts.Interop.AllowSystemReflection = true;
                    if (_allowGetType) opts.Interop.AllowGetType = true;
                    if (_memoryLimit > 0) opts.LimitMemory(_memoryLimit * 1048576);
                    if (_timeout > 0) opts.TimeoutInterval(TimeSpan.FromMilliseconds(_timeout));
                    if (_recursionDepth > 0) opts.LimitRecursion(_recursionDepth);
                }
            );
            _cjsEngine = _engine.CommonJS(WorkingDir);

            SetupGlobals();

            foreach (var nsmp in _namespaces) {
                _cjsEngine = _cjsEngine.RegisterInternalModule(nsmp.module, nsmp.module,
                    new NamespaceReference(_engine, nsmp.@namespace));
            }
            foreach (var scmp in _staticClasses) {
                var type = AssemblyFinder.FindType(scmp.staticClass);
                if (type == null)
                    throw new Exception(
                        $"ScriptEngine could not load static class \"{scmp.staticClass}\". Please check your string(s) in the `Static Classes` array.");
                _cjsEngine = _cjsEngine.RegisterInternalModule(scmp.module, type);
            }
            foreach (var omp in _objects) {
                _cjsEngine = _cjsEngine.RegisterInternalModule(omp.module, omp.obj);
            }
            _uiDocument.rootVisualElement.Clear();
            _engine.SetValue("document", _document);
            OnPostInit?.Invoke();
        }

        bool ClrExceptionHandler(Exception exception) {
            if (_logRedundantErrors && exception.GetType() != typeof(Jint.Runtime.JavaScriptException)) {
                Debug.LogError(exception);
            }
            return true;
        }

        void SetupGlobals() {
            _engine.SetValue("self", _engine.GetValue("globalThis"));
            _engine.SetValue("window", _engine.GetValue("globalThis"));

            _globalFuncTypes.ForEach(t => {
                var flags = BindingFlags.Public | BindingFlags.Static;
                var mi = t.GetMethod("Setup", flags);
                mi.Invoke(null, new object[] { this });
            });
        }

        void RunModule(string scriptPath) {
            // var preloadsPath = Path.Combine(WorkingDir, "ScriptLib/onejs/preloads");
            // if (Directory.Exists(preloadsPath)) {
            //     var files = Directory.GetFiles(preloadsPath,
            //         "*.js", SearchOption.AllDirectories).ToList();
            //     files.ForEach(f => _cjsEngine.RunMain(Path.GetRelativePath(WorkingDir, f)));
            // }
            _preloadedScripts.ForEach(p => _cjsEngine.RunMain(p));

            // var t = DateTime.Now;
            // var a = GC.GetTotalMemory(false);
            _cjsEngine.RunMain(scriptPath);
            // var b = GC.GetTotalMemory(false);
            // var c = b - a;
            // Debug.Log($"{a} {b} {c}");
            // print($"RunModule {(DateTime.Now - t).TotalMilliseconds}ms");
            _postloadedScripts.ForEach(p => _cjsEngine.RunMain(p));
        }
    }

    #region Extra
    [Serializable]
    public class EditorModeWorkingDirInfo {
        public EditorModeBaseDir baseDir;
        public string relativePath = "OneJS";

        public enum EditorModeBaseDir {
            ProjectPath,
            PersistentDataPath
        }
    }

    [Serializable]
    public class PlayerModeWorkingDirInfo {
        public PlayerModeBaseDir baseDir;
        public string relativePath = "OneJS";

        public enum PlayerModeBaseDir {
            PersistentDataPath,
            AppPath,
        }
    }

    [Serializable]
    public class NamespaceModulePair {
        public string @namespace;
        public string module;

        public NamespaceModulePair(string ns, string m) {
            this.@namespace = ns;
            this.module = m;
        }
    }

    [Serializable]
    public class StaticClassModulePair {
        public string staticClass;
        public string module;

        public StaticClassModulePair(string sc, string m) {
            this.staticClass = sc;
            this.module = m;
        }
    }

    [Serializable]
    public class ObjectModulePair {
        public UnityEngine.Object obj;
        public string module;

        public ObjectModulePair(UnityEngine.Object obj, string m) {
            this.obj = obj;
            this.module = m;
        }
    }

    public struct QueuedAction {
        public DateTime dateTime;
        public Action action;
        public int id;
        public double timeout;
        public bool requeue;
        public bool cleared;

        public QueuedAction(Action action, int id, double timeout, bool requeue = false) {
            this.dateTime = DateTime.Now.AddMilliseconds(timeout);
            this.action = action;
            this.id = id;
            this.timeout = timeout;
            this.requeue = requeue;
            cleared = false;
        }

        public void ResetDateTime() {
            this.dateTime = DateTime.Now.AddMilliseconds(timeout);
        }
    }
    #endregion
}