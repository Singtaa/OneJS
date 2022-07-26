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
        static int ID = 0;
        static Dictionary<int, IEnumerator> ID_DICT = new Dictionary<int, IEnumerator>();

        public static void Reset() {
            foreach (var kvp in ID_DICT) {
                CoroutineUtil.Stop(kvp.Value);
            }
            ID_DICT.Clear();
        }

        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("requestAnimationFrame", new Func<JsValue, int>((handler) => {
                ID++;
                var routine =
                    CoroutineUtil.EndOfFrame(() => {
                        handler.As<Jint.Native.Function.FunctionInstance>().Call(null, Time.timeSinceLevelLoad * 1000f);
                    });
                ID_DICT.Add(ID, routine);
                CoroutineUtil.Start(routine);
                return ID;
            }));
            engine.JintEngine.SetValue("cancelAnimationFrame", new Action<int>((id) => {
                if (ID_DICT.ContainsKey(id)) {
                    CoroutineUtil.Stop(ID_DICT[id]);
                    ID_DICT.Remove(id);
                }
            }));
        }
    }
}