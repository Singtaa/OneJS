using System;
using System.Collections;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using OneJS.Engine;
using OneJS.Utils;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class RequestAnimationFrame {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("requestAnimationFrame", new Func<JsValue, int>((handler) => {
                var id = engine.QueueFrameAction(() => {
                    engine.JintEngine.Call(handler, (DateTime.Now - engine.StartTime).TotalMilliseconds);
                });
                return id;
                // var id = engine.QueueAction(() => { engine.JintEngine.Call(handler); }, timeout);
                // return id;
            }));
            engine.JintEngine.SetValue("cancelAnimationFrame",
                new Action<int>((id) => { engine.ClearFrameAction(id); }));
        }
    }
}