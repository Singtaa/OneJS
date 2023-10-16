using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;

namespace OneJS.Engine {
    public class GlobalFuncsSetup {
        ScriptEngine _onejsScriptEngine;
        List<Type> _globalFuncs;

        public GlobalFuncsSetup(ScriptEngine onejsScriptEngine) {
            _onejsScriptEngine = onejsScriptEngine;
        }

        public void Init() {
            _globalFuncs = _onejsScriptEngine.LoadedAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(t => t.IsVisible && t.FullName.StartsWith("OneJS.Engine.JSGlobals"))
                .ToList();
        }

        public void Setup() {
            _globalFuncs.ForEach(t => {
                var flags = BindingFlags.Public | BindingFlags.Static;
                var mi = t.GetMethod("Setup", flags);
                mi.Invoke(null, new object[] { _onejsScriptEngine });
            });
        }

        public void Reset() {
            _globalFuncs.ForEach(t => {
                var flags = BindingFlags.Public | BindingFlags.Static;
                var mi = t.GetMethod("Reset", flags);
                if (mi == null)
                    return;
                mi.Invoke(null, new object[] { });
            });
        }
    }
}
