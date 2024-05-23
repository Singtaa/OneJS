using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Puerts;
using Puerts.Editor.Generator.DTS;

namespace OneJS.Editor {
    public class DTSGenerator {
        public static Type[] GetTypes(Assembly[] assemblies, string[] namespaces) {
            var types = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => !t.IsGenericTypeDefinition && !t.IsNestedPrivate && t.IsPublic && namespaces.Contains(t.Namespace))
                .ToArray();
            return types;
        }
        
        public static string Generate(Assembly[] assemblies, string[] namespaces) {
            var types = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => !t.IsGenericTypeDefinition && !t.IsNestedPrivate && t.IsPublic && namespaces.Contains(t.Namespace))
                .ToArray();
            return Generate(types);
        }

        public static string Generate(Type[] types) {
            string result = "";
            using (var jsEnv = new JsEnv()) {
                jsEnv.UsingFunc<TypingGenInfo, bool, string>();
                var typingRender = jsEnv.ExecuteModule<Func<TypingGenInfo, bool, string>>("puerts/templates/dts.tpl.mjs", "default");
                result = typingRender(TypingGenInfo.FromTypes(types), false);
            }
            return result;
        }
    }
}