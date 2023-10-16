using System;
using Jint;
using Jint.Native;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class OnEngineDestroy {
        public static void Setup(ScriptEngine engine) {
            engine.CoreEngine.SetValue("onEngineDestroy",
                new Action<JsValue>((handler) => {
                    engine.RegisterDestroyHandler(handler.As<Jint.Native.Function.FunctionInstance>());
                }));
            engine.CoreEngine.SetValue("unregisterOnEngineDestroy",
                new Action<JsValue>((handler) => {
                    engine.UnregisterDestroyHandler(handler.As<Jint.Native.Function.FunctionInstance>());
                }));
        }
    }
}
