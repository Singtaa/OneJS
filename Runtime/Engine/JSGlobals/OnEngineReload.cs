using System;
using Jint;
using Jint.Native;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class OnEngineReload {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("onEngineReload", new Action<JsValue>((handler) => {
                engine.RegisterReloadHandler(handler.As<Jint.Native.Function.FunctionInstance>());
            }));
            engine.JintEngine.SetValue("unregisterOnEngineReload", new Action<JsValue>((handler) => {
                engine.UnregisterReloadHandler(handler.As<Jint.Native.Function.FunctionInstance>());
            }));
        }
    }
}