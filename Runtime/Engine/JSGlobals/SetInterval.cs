using System;
using System.Collections;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class SetInterval {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("setInterval", new Func<JsValue, float, int>((handler, timeout) => {
                var id = engine.QueueAction(() => {
                    engine.JintEngine.Call(handler);
                    engine.JintEngine.RunAvailableContinuations();
                }, timeout, true);
                return id;
            }));
            engine.JintEngine.SetValue("clearInterval", new Action<int>((id) => { engine.ClearQueuedAction(id); }));
        }
    }
}