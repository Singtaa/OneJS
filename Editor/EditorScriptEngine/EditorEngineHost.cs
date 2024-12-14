using System;
using Puerts;

namespace OneJS.Editor {
    public class EditorEngineHost : IEngineHost, IDisposable {
        public event Action onReload;
        public event Action onDispose;

        EditorScriptEngine _engine;

        JsEnv _sandboxEnv;

        public EditorEngineHost(EditorScriptEngine engine) {
            _engine = engine;
            _engine.OnReload += DoReload;
            _engine.OnDispose += Dispose;
        }

        public void Dispose() {
            _engine.OnReload -= DoReload;
            onReload = null;
            
            onDispose?.Invoke();
            _engine.OnDispose -= Dispose;
            onDispose = null;
            
            _sandboxEnv?.Dispose();
            _sandboxEnv = null;
        }

        public void DoReload() {
            onReload?.Invoke();
        }

        /// <summary>
        /// Execute the given JavaScript code using the main JS Environment
        /// </summary>
        public void Execute(string jsCode) {
            _engine.Execute(jsCode);
        }

        /// <summary>
        /// Execute the given JavaScript code using a sandboxed JS Environment
        /// </summary>
        public void SandboxExecute(string jsCode) {
            Dispose();
            _sandboxEnv = new JsEnv();
            foreach (var preload in _engine.preloads) {
                _sandboxEnv.Eval(preload.text);
            }
            var addToGlobal = _sandboxEnv.Eval<Action<string, object>>(@"__addToGlobal");
            addToGlobal("___document", _engine.document);
            addToGlobal("___workingDir", _engine.WorkingDir);
            addToGlobal("onejs", this);
            foreach (var obj in _engine.globalObjects) {
                addToGlobal(obj.name, obj.obj);
            }
            _sandboxEnv.Eval(jsCode, "sandbox_js_env");
        }

        public void testx() {
        }
    }
}