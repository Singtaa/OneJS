using System;
using System.IO;
using OneJS.Dom;
using Puerts;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    public class EditorScriptEngine : IScriptEngine {
        public static EditorScriptEngine Instance;
        static readonly Destructor Finalise = new Destructor();

        static EditorScriptEngine() {
            Instance = new EditorScriptEngine();
        }

        sealed class Destructor {
            ~Destructor() {
                if (Instance != null) {
                    Instance.Dispose();
                }
            }
        }

        JsEnv _jsEnv;
        Document _document;
        VisualElement _root;

        Action<string, object> _addToGlobal;

        public EditorScriptEngine() {
            _root = new VisualElement();
            _document = new Document(_root, null);
            _jsEnv = new JsEnv();
            _addToGlobal = _jsEnv.Eval<Action<string, object>>(@"function __addToGlobal(name, obj) {
    const parts = name.split('.');
    let current = globalThis;
    for (let i = 0; i < parts.length - 1; i++) {
        current[parts[i]] = current[parts[i]] || {};
        current = current[parts[i]];
    }
    current[parts[parts.length - 1]] = obj;
};
__addToGlobal;");
            _addToGlobal("document", _document);
        }

        public void Dispose() {
            _jsEnv?.Dispose();
            _jsEnv = null;
        }

        #region Properties
        public string WorkingDir {
            get {
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "App");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }
        #endregion

        public VisualElement RunFileAndGetVisualElement(string filePath, object target) {
            var code = File.ReadAllText(Path.Combine(WorkingDir, filePath));
            var func = _jsEnv.Eval<Func<object, Dom.Dom>>(code);
            
            var dom = func(target);
            return dom.ve;
        }
    }
}