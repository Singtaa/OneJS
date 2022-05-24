using System;
using Jint;
using Jint.Native;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class OnEngineReload {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("onEngineReload", new Action<JsValue>((handler) => {
                Action action = () => { handler.As<Jint.Native.Function.FunctionInstance>().Call(); };
                engine.RegisterReloadHandler(action);
            }));
        }
    }
}