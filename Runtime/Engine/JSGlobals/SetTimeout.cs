using System;
// using Jint;
// using Jint.Native;

namespace OneJS.Engine.JSGlobals {
    public class SetTimeout {
        // public delegate int SetTimeoutDelegate(object handler, float timeout = 0);

        public static void Setup(ScriptEngine engine) {
            int _setTimeOut(object handler, float timeout = 0) {
                var id = engine.QueueAction(() => {
                    engine.CoreEngine.Call(handler);
                }, timeout);
                return id;
            }

            engine.CoreEngine.SetValue("setTimeout", new Func<object, float, int>(_setTimeOut));
            engine.CoreEngine.SetValue("clearTimeout", new Action<int>((id) => { engine.ClearQueuedAction(id); }));
        }


    }
}
