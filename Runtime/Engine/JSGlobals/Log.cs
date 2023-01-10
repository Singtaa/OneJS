using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Engine.JSGlobals {
    public class Log {
        public static void Setup(ScriptEngine engine) {
            LogTime.Clear();
            engine.JintEngine.SetValue("log", new Action<object>(Debug.Log));
            engine.JintEngine.SetValue("error", new Action<object>(Debug.LogError));
            engine.JintEngine.SetValue("warn", new Action<object>(Debug.LogWarning));
            engine.JintEngine.SetValue("logTime", new Action<object>(time));
            engine.JintEngine.SetValue("logTimeEnd", new Action<object>(timeEnd));
            engine.JintEngine.Evaluate(@"var console = {
                log: log,
                error: error,
                warn: warn,
                time: logTime,
                timeEnd: logTimeEnd
            }");
        }

        static Dictionary<string, Performance> LogTime = new Dictionary<string, Performance>();

        static void time(object label) {
            string lb = string.IsNullOrEmpty(label as string) ? "default" : $"{label}";
            if (LogTime.ContainsKey(lb)) {
                Debug.LogWarning($"Label '{lb}' already exists for console.time()");
                return;
            }
            LogTime.Add(lb, new Performance(DateTime.Now));
        }

        static void timeEnd(object label) {
            string lb = string.IsNullOrEmpty(label as string) ? "default" : $"{label}";
            if (LogTime.TryGetValue(lb, out Performance value)) {
                Debug.Log($"{lb}: {value.now()}ms");
                LogTime.Remove(lb);
                return;
            }
            Debug.LogWarning($"No such label '{lb}' for console.timeEnd()");
        }
    }
}