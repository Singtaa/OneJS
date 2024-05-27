using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Generate TypeScript definition from .Net types
        /// </summary>
        /// <param name="types">Include here all the .Net types you want to generate TS definitions for.</param>
        /// <returns>The generated type definition (.d.ts) string</returns>
        public static string Generate(Type[] types) {
            return Generate(TypingGenInfo.FromTypes(types));
        }

        /// <summary>
        /// Generate TypeScript definition for just the specified types
        /// </summary>
        public static string GenerateExact(Type[] types) {
            var genInfo = TypingGenInfo.FromTypes(types);
            var tsNamespaces = new List<TsNamespaceGenInfo>();
            foreach (var ns in genInfo.NamespaceInfos) {
                ns.Types = ns.Types.Where(typeGenInfo => types.Where(t => SameType(typeGenInfo, t)).Count() > 0).ToArray();
                if (ns.Types.Length > 0) {
                    tsNamespaces.Add(ns);
                }
            }
            genInfo.NamespaceInfos = tsNamespaces.ToArray();
            string result = "";
            return Generate(genInfo);
        }

        /// <summary>
        /// Generate TypeScript definition from TypingGenInfo
        /// </summary>
        public static string Generate(TypingGenInfo genInfo) {
            string result = "";
            using (var jsEnv = new JsEnv()) {
                jsEnv.UsingFunc<TypingGenInfo, bool, string>();
                var typingRender = jsEnv.ExecuteModule<Func<TypingGenInfo, bool, string>>("onejs/templates/dts.tpl.mjs", "default");
                result = typingRender(genInfo, false);
            }
            return result;
        }

        static bool SameType(TsTypeGenInfo typeGenInfo, Type type) {
            return typeGenInfo.Name == type.Name.Replace('`', '$') && typeGenInfo.Namespace == type.Namespace;
        }
    }
}