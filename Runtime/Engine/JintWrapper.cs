using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jint;
using Jint.CommonJS;
using Jint.Native;
using Jint.Runtime.Interop;
using OneJS.Utils;
using UnityEngine;

namespace OneJS.Engine {
    public class JintWrapper : IEngineWrapper {
        public Jint.Engine JintEngine => _jintEngine;

        public event Action<Options> OnInitOptions;

        ScriptEngine _onejsScriptEngine;
        Jint.Engine _jintEngine;
        ModuleLoadingEngine _cjsEngine;

        GlobalFuncsSetup _globalFuncsSetup;

        public JintWrapper(ScriptEngine onejsScriptEngine) {
            _onejsScriptEngine = onejsScriptEngine;
            _globalFuncsSetup = new GlobalFuncsSetup(_onejsScriptEngine);
        }

        public void Init() {
            _jintEngine = new Jint.Engine(opts => {
                    opts.Interop.TrackObjectWrapperIdentity = false; // Unity too buggy with ConditionalWeakTable
                    opts.SetTypeResolver(new TypeResolver {
                        MemberNameComparer = StringComparer.Ordinal
                    });
                    opts.AllowClr(_onejsScriptEngine.LoadedAssemblies);
                    _onejsScriptEngine.Extensions.ToList().ForEach((e) => {
                        var type = AssemblyFinder.FindType(e);
                        if (type == null)
                            throw new Exception(
                                $"ScriptEngine could not load extension \"{e}\". Please check your string(s) in the `extensions` array.");
                        opts.AddExtensionMethods(type);
                    });
                    opts.AllowOperatorOverloading();

                    if (_onejsScriptEngine.CatchDotNetExceptions) opts.CatchClrExceptions(ClrExceptionHandler);
                    if (_onejsScriptEngine.AllowReflection) opts.Interop.AllowSystemReflection = true;
                    if (_onejsScriptEngine.AllowGetType) opts.Interop.AllowGetType = true;
                    if (_onejsScriptEngine.MemoryLimit > 0) opts.LimitMemory(_onejsScriptEngine.MemoryLimit * 1048576);
                    if (_onejsScriptEngine.Timeout > 0) opts.TimeoutInterval(TimeSpan.FromMilliseconds(_onejsScriptEngine.Timeout));
                    if (_onejsScriptEngine.RecursionDepth > 0) opts.LimitRecursion(_onejsScriptEngine.RecursionDepth);

                    OnInitOptions?.Invoke(opts);
                }
            );
            _cjsEngine = _jintEngine.CommonJS(_onejsScriptEngine.WorkingDir, _onejsScriptEngine.PathMappings);

            _globalFuncsSetup.Init();

            this.SetValue("self", _jintEngine.GetValue("globalThis"));
            this.SetValue("window", _jintEngine.GetValue("globalThis"));

            _globalFuncsSetup.Setup();

            foreach (var nsmp in _onejsScriptEngine.Namespaces) {
                _cjsEngine = _cjsEngine.RegisterInternalModule(nsmp.module, nsmp.module,
                    new NamespaceReference(_jintEngine, nsmp.@namespace));
            }
            foreach (var scmp in _onejsScriptEngine.StaticClasses) {
                var type = AssemblyFinder.FindType(scmp.staticClass);
                if (type == null)
                    throw new Exception(
                        $"ScriptEngine could not load static class \"{scmp.staticClass}\". Please check your string(s) in the `Static Classes` array.");
                _cjsEngine = _cjsEngine.RegisterInternalModule(scmp.module, type);
            }
            foreach (var omp in _onejsScriptEngine.Objects) {
                _cjsEngine = _cjsEngine.RegisterInternalModule(omp.module, omp.obj);
            }
        }

        public void Reset() {
            _globalFuncsSetup.Reset();
        }

        public void Dispose() {
            throw new NotImplementedException();
        }

        public object GetValue(string name) {
            return _jintEngine.GetValue(name);
        }

        public void SetValue(string name, object? obj) {
            _jintEngine.SetValue(name, obj);
        }

        public void Call(object callback, object thisObj = null, params object[] arguments) {
            JsValue[] jsValues = arguments.Select(o => {
                if (o is JsValue jsv)
                    return jsv;
                else if (o is object[] objs)
                    return JsValue.FromObject(_jintEngine, objs.Select(obj => JsValue.FromObject(_jintEngine, obj)).ToArray());
                return JsValue.FromObject(_jintEngine, o);
            }).ToArray();
            _jintEngine.Call(GetJsValue(callback), GetJsValue(thisObj), jsValues);
        }

        public void Execute(string code) {
            _jintEngine.Execute(code);
        }

        public object Evaluate(string code) {
            return _jintEngine.Evaluate(code);
        }

        public void RunModule(string scriptPath) {
            _cjsEngine.RunMain(scriptPath);
        }

        public void ResetConstraints() {
            _jintEngine.ResetConstraints();
        }

        public void RunAvailableContinuations() {
            _jintEngine.RunAvailableContinuations();
        }

        JsValue GetJsValue(object obj) {
            return JsValue.FromObject(_jintEngine, obj);
        }

        bool ClrExceptionHandler(Exception exception) {
            if (_onejsScriptEngine.LogRedundantErrors && exception.GetType() != typeof(Jint.Runtime.JavaScriptException)) {
                Debug.LogError(exception);
            }
            return true;
        }
    }
}
