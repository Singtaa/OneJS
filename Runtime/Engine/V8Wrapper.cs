#if ONEJS_V8
using System;
using System.IO;
using System.Linq;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using OneJS.Utils;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace OneJS.Engine {
    public class V8Wrapper : IEngineWrapper {
        V8ScriptEngine _engine;
        ScriptEngine _onejsScriptEngine;
        GlobalFuncsSetup _globalFuncsSetup;

        public V8Wrapper(ScriptEngine onejsScriptEngine) {
            _onejsScriptEngine = onejsScriptEngine;
            _globalFuncsSetup = new GlobalFuncsSetup(_onejsScriptEngine);
        }

        public void Init() {
            _engine = new V8ScriptEngine();
            _engine.DocumentSettings.Loader.DiscardCachedDocuments(); // Note: the cached documents are only auto-cleared during domain reload
            _engine.DocumentSettings.Loader = new OneJSDocumentLoader(_onejsScriptEngine);
            _engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            _engine.DocumentSettings.SearchPath = _onejsScriptEngine.WorkingDir;
            _engine.DocumentSettings.LoadCallback = (ref DocumentInfo info) => { info.Category = ModuleCategory.CommonJS; };

            _engine.AddHostObject("clr", new HostTypeCollection(_onejsScriptEngine.LoadedAssemblies));
            _engine.AddHostType("Debug", typeof(Debug));
            // _engine.AddHostObject("getTypeName", new Action(MyFunction));

            foreach (var scmp in _onejsScriptEngine.StaticClasses) {
                var type = AssemblyFinder.FindType(scmp.staticClass);
                if (type == null)
                    throw new Exception(
                        $"ScriptEngine could not load static class \"{scmp.staticClass}\". Please check your string(s) in the `Static Classes` array.");
                _engine.AddHostType(scmp.module, type);
            }

            foreach (var extension in _onejsScriptEngine.Extensions) {
                var type = AssemblyFinder.FindType(extension);
                if (type == null)
                    throw new Exception(
                        $"ScriptEngine could not load extension \"{extension}\". Please check your string(s) in the `extensions` array.");
                _engine.AddHostType(extension, type);
            }

            _engine.AddHostType("Slider", typeof(Slider));

            _globalFuncsSetup.Init();
            _globalFuncsSetup.Setup();
        }

        public void Reset() {
            _globalFuncsSetup.Reset();
        }

        public void Dispose() {
            _engine.Dispose();
        }

        public object GetValue(string name) {
            return _engine.Script[name];
        }

        public void SetValue(string name, object obj) {
            _engine.AddHostObject(name, obj);
        }

        public void Call(object callback, object thisObj = null, params object[] arguments) {
            if (callback is Delegate clrDelegate) {
                // Handle CLR delegate
                clrDelegate.DynamicInvoke(arguments);
            } else {
                try {
                    _engine.Script.callWithThisObj(callback, thisObj, arguments);
                } catch (ScriptEngineException ex) {
                    Debug.LogError(ex.ErrorDetails);
                }
            }
        }

        public void Execute(string code) {
            _engine.Execute(code);
        }

        public object Evaluate(string code) {
            return _engine.Evaluate(code);
        }

        public void RunModule(string scriptPath) {
            var fullpath = Path.Combine(_onejsScriptEngine.WorkingDir, scriptPath);

            try {
                _engine.ExecuteDocument(fullpath, ModuleCategory.CommonJS);
            } catch (ScriptEngineException ex) {
                Debug.Log(ex.ErrorDetails);
            }
        }
    }
}
#endif
