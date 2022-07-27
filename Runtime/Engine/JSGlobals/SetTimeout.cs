using System;
using System.Collections;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using OneJS.Engine;
using OneJS.Utils;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class SetTimeout {
        static int ID = 0;
        static Dictionary<int, IEnumerator> ID_DICT = new Dictionary<int, IEnumerator>();

        public static void Reset() {
            foreach (var kvp in ID_DICT) {
                CoroutineUtil.Stop(kvp.Value);
            }
            ID_DICT.Clear();
        }

        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("setTimeout", new Func<JsValue, float, int>((handler, timeout) => {
                var id = ++ID;
                var routine =
                    CoroutineUtil.WaitForSeconds(timeout / 1000f,
                        () => {
                            handler.As<Jint.Native.Function.FunctionInstance>().Call();
                            ID_DICT.Remove(id);
                        });

                ID_DICT.Add(id, routine);
                CoroutineUtil.Start(routine);
                return id;
            }));
            engine.JintEngine.SetValue("clearTimeout", new Action<int>((id) => {
                if (ID_DICT.ContainsKey(id)) {
                    CoroutineUtil.Stop(ID_DICT[id]);
                    ID_DICT.Remove(id);
                }
            }));
        }
    }
}