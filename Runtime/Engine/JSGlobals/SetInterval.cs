using System;
using System.Collections;
using System.Collections.Generic;
// using Jint;
// using Jint.Native;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class SetInterval {
        public static void Setup(ScriptEngine engine) {
            engine.CoreEngine.SetValue("setInterval", new Func<object, float, int>((handler, timeout) => {
                var id = engine.QueueAction(() => {
                    engine.CoreEngine.Call(handler);
                }, timeout, true);
                return id;
            }));
            engine.CoreEngine.SetValue("clearInterval", new Action<int>((id) => { engine.ClearQueuedAction(id); }));
        }
    }
}
