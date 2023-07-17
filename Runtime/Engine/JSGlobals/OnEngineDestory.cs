using System;
using Jint;
using Jint.Native;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class OnEngineDestory {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("onEngineDestroy",
                new Action<JsValue>((handler) => {
                    engine.OnEngineDestroy += () => { engine.JintEngine.Call(handler); };
                }));
        }
    }
}