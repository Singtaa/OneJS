using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using Jint;
using Jint.CommonJS;
using Jint.Runtime.Interop;
using NaughtyAttributes;
using OneJS.Dom;
using OneJS.Engine;
using OneJS.Engine.JSGlobals;
using OneJS.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OneJS {
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
        public UnityEngine.MonoBehaviour obj;
        public string module;

        public ObjectModulePair(UnityEngine.MonoBehaviour obj, string m) {
            this.obj = obj;
            this.module = m;
        }
    }

    [RequireComponent(typeof(UIDocument), typeof(CoroutineUtil))]
    public class ScriptEngine : MonoBehaviour {
        public static string WorkingDir {
            get {
#if UNITY_EDITOR
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "OneJS");
                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }
                return path;
#else
                return Path.Combine(Application.persistentDataPath, "OneJS");
#endif
            }
        }

        public Jint.Engine JintEngine => _engine;
        public ModuleLoadingEngine ModuleEngine => _cjsEngine;
        public Dom.Document Document => _document;
        public Dom.Dom DocumentBody => _document.body;
        public int[] Breakpoints => _breakpoints;

        public event Action OnPostInit;
        public event Action OnReload;

        [Foldout("INTEROP")] [Tooltip("Include any assembly you'd want to access from Javascript.")] [SerializeField]
        string[] _assemblies = new[] {
            "UnityEngine.CoreModule", "UnityEngine.PhysicsModule", "UnityEngine.UIElementsModule",
            "UnityEngine.IMGUIModule", "UnityEngine.TextRenderingModule",
            "Unity.Mathematics", "OneJS"
        };

        [Foldout("INTEROP")]
        [Tooltip("Extensions need to be explicitly added to the script engine. OneJS also provide some default ones.")]
        [SerializeField]
        string[] _extensions = new[] {
            "OneJS.Extensions.GameObjectExts",
            "OneJS.Extensions.ComponentExts",
            "OneJS.Extensions.ColorExts",
            "OneJS.Extensions.VisualElementExts",
            "UnityEngine.UIElements.PointerCaptureHelper"
        };

        [Foldout("INTEROP")]
        [Tooltip("C# Namespace to JS Module mapping.")]
        [PairMapping("namespace", "module")]
        [SerializeField]
        NamespaceModulePair[] _namespaces = new[] {
            new NamespaceModulePair("System.Collections.Generic", "System/Collections/Generic"),
            new NamespaceModulePair("UnityEngine", "UnityEngine"),
            new NamespaceModulePair("UnityEngine.UIElements", "UnityEngine/UIElements"),
            new NamespaceModulePair("OneJS.Utils", "OneJS/Utils"),
        };

        [Foldout("INTEROP")]
        [Tooltip("Static Class to JS Module mapping.")]
        [PairMapping("staticClass", "module")]
        [SerializeField]
        StaticClassModulePair[] _staticClasses = new[]
            { new StaticClassModulePair("Unity.Mathematics.math", "math") };

        [Foldout("INTEROP")] [Tooltip("Object to JS Module mapping.")] [PairMapping("obj", "module")] [SerializeField]
        ObjectModulePair[] _objects = new ObjectModulePair[]
            { };

        [Foldout("INTEROP")] [Tooltip("Scripts that you want to load before everything else")] [SerializeField]
        List<string> _preloadedScripts = new List<string>();

        [Foldout("STYLING")]
        [Tooltip("Inculde here any global USS you'd need. OneJS also provides a default one.")]
        [SerializeField] StyleSheet[] _styleSheets;

        [Foldout("STYLING")]
        [Tooltip("Screen breakpoints for responsive design.")]
        [SerializeField] int[] _breakpoints = new[] { 640, 768, 1024, 1280, 1536 };

        [Foldout("SECURITY")] [Tooltip("Allow access to System.Reflection from Javascript")] [SerializeField]
        bool _allowReflection;
        [Foldout("SECURITY")] [Tooltip("Allow access to .GetType() from Javascript")] [SerializeField]
        bool _allowGetType;
        [Foldout("SECURITY")] [Tooltip("Memory Limit in MB. Set to 0 for no limit.")] [SerializeField] int _memoryLimit;
        [Foldout("SECURITY")]
        [Tooltip("How long a script can execute in milliseconds. Set to 0 for no limit.")]
        [SerializeField]
        int _timeout;
        [Foldout("SECURITY")]
        [Tooltip("Limit depth of calls to prevent deep recursion calls. Set to 0 for no limit.")]
        [SerializeField]
        int _recursionDepth;

        UIDocument _uiDocument;
        Document _document;
        ModuleLoadingEngine _cjsEngine;
        Jint.Engine _engine;

        List<Action> _engineReloadJSHandlers = new List<Action>();
        List<IClassStrProcessor> _classStrProcessors = new List<IClassStrProcessor>();

        public void Awake() {
            _uiDocument = GetComponent<UIDocument>();
            _uiDocument.rootVisualElement.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            _uiDocument.rootVisualElement.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            _document = new Document(_uiDocument.rootVisualElement, this);
            _styleSheets.ToList().ForEach(s => _uiDocument.rootVisualElement.styleSheets.Add(s));
        }

        void Start() {
            InitEngine();
        }

        public void RunScript(string scriptPath) {
            var path = Path.Combine(ScriptEngine.WorkingDir, scriptPath);
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
                Debug.LogError($"Script Path ({path}) doesn't exist.");
                return;
            }
            OnReload?.Invoke();
            CleanUp();
            InitEngine();
            RunModule(scriptPath);
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
            _engineReloadJSHandlers.ForEach((a) => { OnReload -= a; });
            _engineReloadJSHandlers.Clear();

            SetTimeout.Reset();
            RequestAnimationFrame.Reset();
        }

        void InitEngine() {
            _engine = new Jint.Engine(opts => {
                    opts.AllowClr(_assemblies.Select((a) => {
                        try {
                            return Assembly.Load(a);
                        } catch (Exception e) {
                            throw new Exception(
                                $"ScriptEngine could not load assembly \"{a}\". Please check your string(s) in the `assemblies` array.");
                        }
                    }).ToArray());
                    _extensions.ToList().ForEach((e) => {
                        var type = AssemblyFinder.FindType(e);
                        if (type == null)
                            throw new Exception(
                                $"ScriptEngine could not load extension \"{e}\". Please check your string(s) in the `extensions` array.");
                        opts.AddExtensionMethods(type);
                    });

                    opts.AllowOperatorOverloading();
                    if (_allowReflection) opts.Interop.AllowSystemReflection = true;
                    if (_allowGetType) opts.Interop.AllowGetType = true;
                    if (_memoryLimit > 0) opts.LimitMemory(_memoryLimit * 1000000);
                    if (_timeout > 0) opts.TimeoutInterval(TimeSpan.FromMilliseconds(_timeout));
                    if (_recursionDepth > 0) opts.LimitRecursion(_recursionDepth);
                }
            );
            _cjsEngine = _engine.CommonJS();

            SetupGlobals();

            foreach (var nsmp in _namespaces) {
                _cjsEngine = _cjsEngine.RegisterInternalModule(nsmp.module,
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

        void SetupGlobals() {
            _engine.SetValue("self", _engine.GetValue("globalThis"));
            _engine.SetValue("window", _engine.GetValue("globalThis"));

            var globalFuncTypes = this.GetType().Assembly.GetTypes()
                .Where(t => t.IsVisible && t.FullName.StartsWith("OneJS.Engine.JSGlobals")).ToList();
            globalFuncTypes.ForEach(t => {
                var flags = BindingFlags.Public | BindingFlags.Static;
                var mi = t.GetMethod("Setup", flags);
                mi.Invoke(null, new object[] { this });
            });
        }

        void RunModule(string scriptPath) {
            var preloadsPath = Path.Combine(WorkingDir, "ScriptLib/onejs/preloads");
            if (Directory.Exists(preloadsPath)) {
                var files = Directory.GetFiles(preloadsPath,
                    "*.js", SearchOption.AllDirectories).ToList();
                files.ForEach(f => _cjsEngine.RunMain(Path.GetRelativePath(WorkingDir, f)));
                _preloadedScripts.ForEach(p => _cjsEngine.RunMain(p));
            }
            _cjsEngine.RunMain(scriptPath);
        }
    }
}