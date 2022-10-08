using System;
using Jint;
using Jint.Native;

namespace OneJS.Engine.JSGlobals {
    public class SetTimeout {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("setTimeout", new Func<JsValue, float, int>((handler, timeout) => {
                var id = engine.QueueAction(() => {
                    engine.JintEngine.Call(handler);
                    engine.JintEngine.RunAvailableContinuations();
                }, timeout);
                return id;
            }));
            engine.JintEngine.SetValue("clearTimeout", new Action<int>((id) => { engine.ClearQueuedAction(id); }));
        }
    }
}