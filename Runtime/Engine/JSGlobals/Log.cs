using System;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class Log {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("log", new Action<object>(Debug.Log));
        }
    }
}