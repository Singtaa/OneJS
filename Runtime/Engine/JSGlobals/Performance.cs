using System;

namespace OneJS.Engine.JSGlobals {
    public class Performance {
        public static void Setup(ScriptEngine engine) {
            var performance = new Performance(engine.StartTime);
            engine.JintEngine.SetValue("performance", performance);
        }

        DateTime _start;

        public Performance(DateTime start) {
            _start = start;
        }

        public double now() {
            var span = DateTime.Now - _start;
            return span.TotalMilliseconds;
        }
    }
}