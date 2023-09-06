using System;
using Jint;
using Jint.Native;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class OnEngineDestroy {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("onEngineDestroy",
                new Action<JsValue>((handler) => {
                    engine.RegisterDestroyHandler(handler.As<Jint.Native.Function.FunctionInstance>());
                }));
            engine.JintEngine.SetValue("unregisterOnEngineDestroy",
                new Action<JsValue>((handler) => {
                    engine.UnregisterDestroyHandler(handler.As<Jint.Native.Function.FunctionInstance>());
                }));
        }
    }
}
