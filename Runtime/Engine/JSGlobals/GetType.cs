using System;

namespace OneJS.Engine.JSGlobals {
    public class GetType {
        public static void Setup(ScriptEngine engine) {
            engine.JintEngine.SetValue("getType", new Func<object, Type>((obj) => {
                if (obj == null)
                    return null;
                if (obj is Type)
                    return obj as Type;
                return obj.GetType();
            }));
        }
    }
}