using System;
using System.Collections;
using System.IO;
using System.Linq;
using OneJS.Dom;
using Puerts;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    [RequireComponent(typeof(UIDocument))] [AddComponentMenu("OneJS/ScriptEngine")]
    public class ScriptEngine : MonoBehaviour, IScriptEngine {
        public int Tick => _tick;

        #region Public Fields
        [Tooltip("Set the OneJS Working Directory for Editor Mode.")]
        [PairMapping("baseDir", "relativePath", "/", "Editor WorkingDir")]
        public EditorWorkingDirInfo editorWorkingDirInfo;

        [Tooltip("Set the OneJS Working Directory for Standalone build.")]
        [PairMapping("baseDir", "relativePath", "/", "Player WorkingDir")]
        [SerializeField] public PlayerWorkingDirInfo playerWorkingDirInfo;

        [Tooltip("JS files that you want to preload before running anything else.")]
        public TextAsset[] preloads;

        [Tooltip("Global objects that you want to expose to the JS environment. This list accepts any UnityEngine.Object, not just MonoBehaviours. There's a little trick in picking a specific MonoBehaviour component. You right-click on the Inspector Tab of the selected GameObject and pick Properties. A standalone window will pop up for you to drag the specifc MonoBehavior from.")]
        [PairMapping("obj", "name")]
        public ObjectMappingPair[] globalObjects;

        [Tooltip("Include here any global USS you'd need. i.e. if you are working with Tailwind, make sure to include the output *.uss here.")]
        public StyleSheet[] styleSheets;
        #endregion

        #region Events
        public event Action OnReload;
        #endregion

        #region Private Fields
        JsEnv _jsEnv;
        EngineHost _engineHost;

        UIDocument _uiDocument;
        Document _document;
        Resource _resource;
        int _tick;

        Action<string, object> _addToGlobal;
        #endregion

        #region Lifecycles
        void Awake() {
            _uiDocument = GetComponent<UIDocument>();
            _resource = new Resource(this);
        }

        void OnEnable() {
            Init();
        }

        void OnDisable() {
            Shutdown();
        }

        void Update() {
            _jsEnv.Tick();
            _tick++;
        }
        #endregion

        #region Properties
        public string WorkingDir {
            get {
#if UNITY_EDITOR
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!,
                    editorWorkingDirInfo.relativePath);
                if (editorWorkingDirInfo.baseDir == EditorWorkingDirInfo.EditorBaseDir.PersistentDataPath)
                    path = Path.Combine(Application.persistentDataPath, editorWorkingDirInfo.relativePath);
                if (editorWorkingDirInfo.baseDir == EditorWorkingDirInfo.EditorBaseDir.StreamingAssetsPath)
                    path = Path.Combine(Application.streamingAssetsPath, editorWorkingDirInfo.relativePath);
                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }
                return path;
#else
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!,
                    playerWorkingDirInfo.relativePath);
                if (playerWorkingDirInfo.baseDir == PlayerWorkingDirInfo.PlayerBaseDir.PersistentDataPath)
                    path = Path.Combine(Application.persistentDataPath, playerWorkingDirInfo.relativePath);
                if (playerWorkingDirInfo.baseDir == PlayerWorkingDirInfo.PlayerBaseDir.StreamingAssetsPath)
                    path = Path.Combine(Application.streamingAssetsPath, playerWorkingDirInfo.relativePath);
                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }
                return path;
#endif
            }
        }

        public JsEnv JsEnv => _jsEnv;
        #endregion

        #region Public Methods
        public string GetFullPath(string filepath) {
            return Path.Combine(WorkingDir, filepath);
        }

        public void Shutdown() {
            if (_jsEnv != null) {
                _jsEnv.Dispose();
                _engineHost.InvokeOnDestroy();
            }
            if (_uiDocument.rootVisualElement != null) {
                _uiDocument.rootVisualElement.Clear();
                _uiDocument.rootVisualElement.styleSheets.Clear();
            }
        }

        public void Reload() {
            OnReload?.Invoke();
            _engineHost.InvokeOnReload();
            Init();
#if UNITY_EDITOR
            StartCoroutine(RefreshStyleSheets());
#endif
        }

        void Init() {
            if (_jsEnv != null) {
                _jsEnv.Dispose();
            }
            _jsEnv = new JsEnv();
            _engineHost = new EngineHost(this);
            _jsEnv.UsingAction<Action>();

            if (_uiDocument.rootVisualElement != null) {
                _uiDocument.rootVisualElement.Clear();
                _uiDocument.rootVisualElement.styleSheets.Clear();
            }

            foreach (var preload in preloads) {
                _jsEnv.Eval(preload.text);
            }
            styleSheets.ToList().ForEach(s => _uiDocument.rootVisualElement.styleSheets.Add(s));
            _document = new Document(_uiDocument.rootVisualElement, this);
            _addToGlobal = _jsEnv.Eval<System.Action<string, object>>(@"__addToGlobal");
            _addToGlobal("___document", _document);
            _addToGlobal("resource", _resource);
            _addToGlobal("onejs", _engineHost);
            foreach (var obj in globalObjects) {
                _addToGlobal(obj.name, obj.obj);
            }
        }

        /// <summary>
        /// Evaluate a script file at the given path.
        /// </summary>
        /// <param name="filepath">Relative to the WorkingDir</param>
        public void EvalFile(string filepath) {
            var fullpath = GetFullPath(filepath);
            if (!File.Exists(fullpath)) {
                Debug.LogError($"Entry file not found: {fullpath}");
                return;
            }
            var code = File.ReadAllText(fullpath);
            _jsEnv.Eval(code);
        }

        /// <summary>
        /// Evaluate a code string.
        /// </summary>
        /// <param name="code">The code string</param>
        public void Eval(string code) {
            _jsEnv.Eval(code);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// This is for convenience for Live-Reload. Stylesheets need explicit refreshing
        /// when Unity Editor doesn't have focus. Otherwise, stylesheet changes won't be
        /// reflected in the Editor until it gains focus.
        /// </summary>
        IEnumerator RefreshStyleSheets() {
#if UNITY_EDITOR
            yield return new WaitForSeconds(0.1f);
            foreach (var ss in styleSheets) {
                if (ss != null) {
                    string assetPath = UnityEditor.AssetDatabase.GetAssetPath(ss);
                    if (!string.IsNullOrEmpty(assetPath)) {
                        UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                    }
                }
            }
#endif
        }
        #endregion

        #region ContextMenus
#if UNITY_EDITOR
        [ContextMenu("Generate Globals Definitions")]
        public void GenerateGlobalsDefinitions() {
            var filename = OneJS.Editor.EditorInputDialog.Show("Enter the file name", "", "globals.d.ts");
            if (string.IsNullOrEmpty(filename))
                return;
            var definitionContents = "";
            foreach (var obj in globalObjects) {
                // var objType = obj.obj.GetType();
                // if (string.IsNullOrEmpty(objType.Namespace))
                //     continue;
                definitionContents += $"declare const {obj.name}: any;\n";
            }
            File.WriteAllText(Path.Combine(Application.dataPath, $"Gen/Typing/csharp/{filename}"), definitionContents);
        }
#endif
        #endregion
    }

    #region Extras
    [Serializable]
    public class ObjectMappingPair {
        public UnityEngine.Object obj;
        public string name;

        public ObjectMappingPair(UnityEngine.Object obj, string m) {
            this.obj = obj;
            this.name = m;
        }
    }

    [Serializable]
    public class EditorWorkingDirInfo {
        public EditorBaseDir baseDir;
        public string relativePath = "App";

        public enum EditorBaseDir {
            ProjectPath,
            PersistentDataPath,
            StreamingAssetsPath
        }

        public override string ToString() {
            var basePath = baseDir switch {
                EditorBaseDir.ProjectPath => Path.GetDirectoryName(Application.dataPath),
                EditorBaseDir.PersistentDataPath => Application.persistentDataPath,
                _ => throw new ArgumentOutOfRangeException()
            };
            return Path.Combine(basePath, relativePath);
        }
    }

    [Serializable]
    public class PlayerWorkingDirInfo {
        public PlayerBaseDir baseDir;
        public string relativePath = "App";

        public enum PlayerBaseDir {
            PersistentDataPath,
            StreamingAssetsPath,
            AppPath,
        }

        public override string ToString() {
            var basePath = baseDir switch {
                PlayerBaseDir.PersistentDataPath => Application.persistentDataPath,
                PlayerBaseDir.AppPath => Path.GetDirectoryName(Application.dataPath),
                _ => throw new ArgumentOutOfRangeException()
            };
            return Path.Combine(basePath, relativePath);
        }
    }
    #endregion
}