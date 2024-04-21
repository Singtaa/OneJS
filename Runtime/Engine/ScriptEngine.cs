using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OneJS.Dom;
using OneJS.Editor;
using Puerts;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace OneJS {
    [RequireComponent(typeof(UIDocument))]
    public class ScriptEngine : MonoBehaviour, IScriptEngine {
        public int Tick => _tick;

        #region Public Fields
        [PairMapping("baseDir", "relativePath", "/", "Editor WorkingDir")]
        public EditorModeWorkingDirInfo editorModeWorkingDirInfo;

        [PairMapping("baseDir", "relativePath", "/", "Player WorkingDir")]
        [SerializeField] public PlayerModeWorkingDirInfo playerModeWorkingDirInfo;

        public TextAsset[] preloads;

        [FormerlySerializedAs("globalObjs")]
        [PairMapping("obj", "name")]
        public ObjectMappingPair[] globalObjects;

        public StyleSheet[] styleSheets;
        #endregion

        #region Events
        public event Action OnReload;
        public event Action OnPostInit;
        #endregion

        #region Private Fields
        JsEnv _jsEnv;

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
            Reset();
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
                    editorModeWorkingDirInfo.relativePath);
                if (editorModeWorkingDirInfo.baseDir == EditorModeWorkingDirInfo.EditorModeBaseDir.PersistentDataPath)
                    path = Path.Combine(Application.persistentDataPath, editorModeWorkingDirInfo.relativePath);
                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }
                return path;
#else
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!,
                    playerModeWorkingDirInfo.relativePath);
                if (playerModeWorkingDirInfo.baseDir == PlayerModeWorkingDirInfo.PlayerModeBaseDir.PersistentDataPath)
                    path = Path.Combine(Application.persistentDataPath, playerModeWorkingDirInfo.relativePath);
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
            }
            if (_uiDocument.rootVisualElement != null) {
                _uiDocument.rootVisualElement.Clear();
                _uiDocument.rootVisualElement.styleSheets.Clear();
            }
        }
        
        public void Reload() {
            Reset();
            OnReload?.Invoke();
        }

        public void Reset() {
            if (_jsEnv != null) {
                _jsEnv.Dispose();
            }
            _jsEnv = new JsEnv();
            // _jsEnv.UsingAction<Painter2D, Color>();
            // _jsEnv.UsingAction<Painter2D, Painter2D>();            
            // _jsEnv.UsingAction<Color, Color>();
            // _jsEnv.UsingAction<Color>();
            // _jsEnv.UsingFunc<Color>();
            // _jsEnv.UsingFunc<string>();

            foreach (var preload in preloads) {
                _jsEnv.Eval(preload.text);
            }
            _uiDocument.rootVisualElement.Clear();
            _uiDocument.rootVisualElement.styleSheets.Clear();
            styleSheets.ToList().ForEach(s => _uiDocument.rootVisualElement.styleSheets.Add(s));
            _document = new Document(_uiDocument.rootVisualElement, this);
            _addToGlobal = _jsEnv.Eval<System.Action<string, object>>(@"__addToGlobal");
            _addToGlobal("document", _document);
            _addToGlobal("resource", _resource);
            foreach (var obj in globalObjects) {
                _addToGlobal(obj.name, obj.obj);
            }
            OnPostInit?.Invoke();
        }
        
        /// <summary>
        /// Evaluate a script file at the given path.
        /// </summary>
        /// <param name="filepath">Relative to the WorkingDir</param>
        public void EvalFile(string filepath) {
            var fullpath = Path.Combine(WorkingDir, filepath);
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

        #region ContextMenus
        [ContextMenu("Generate Globals Definitions")]
        public void GenerateGlobalsDefinitions() {
            var filename = EditorInputDialog.Show("Enter the file name", "", "globals.d.ts");
            var definitionContents = "";
            foreach (var obj in globalObjects) {
                var objType = obj.obj.GetType();
                definitionContents += $"declare const {obj.name}: any;\n";
                print($"declare const {obj.name}: any;");
            }
            File.WriteAllText(Path.Combine(Application.dataPath, $"Gen/Typing/csharp/{filename}"), definitionContents);
        }
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
    public class EditorModeWorkingDirInfo {
        public EditorModeBaseDir baseDir;
        public string relativePath = "App";

        public enum EditorModeBaseDir {
            ProjectPath,
            PersistentDataPath
        }

        public override string ToString() {
            var basePath = baseDir switch {
                EditorModeBaseDir.ProjectPath => Path.GetDirectoryName(Application.dataPath),
                EditorModeBaseDir.PersistentDataPath => Application.persistentDataPath,
                _ => throw new ArgumentOutOfRangeException()
            };
            return Path.Combine(basePath, relativePath);
        }
    }

    [Serializable]
    public class PlayerModeWorkingDirInfo {
        public PlayerModeBaseDir baseDir;
        public string relativePath = "App";

        public enum PlayerModeBaseDir {
            PersistentDataPath,
            AppPath,
        }

        public override string ToString() {
            var basePath = baseDir switch {
                PlayerModeBaseDir.PersistentDataPath => Application.persistentDataPath,
                PlayerModeBaseDir.AppPath => Path.GetDirectoryName(Application.dataPath),
                _ => throw new ArgumentOutOfRangeException()
            };
            return Path.Combine(basePath, relativePath);
        }
    }
    #endregion
}