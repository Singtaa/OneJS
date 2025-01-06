using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using OneJS.Editor.Generator.DTS;
using UnityEngine;

namespace OneJS.Editor {
    public class DTSGen : MonoBehaviour {
        /// <summary>
        /// Null or empty namespaces means all namespaces
        /// </summary>
        public static Type[] GetTypes(Assembly[] assemblies, [CanBeNull] string[] namespaces) {
            var types = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => (t.IsPublic || t.IsNestedPublic) && (t.DeclaringType == null || t.DeclaringType.IsPublic) &&
                            /*!t.IsGenericTypeDefinition && !t.IsNestedPrivate && */
                            (namespaces == null || namespaces.Length == 0 || namespaces.Contains(string.IsNullOrEmpty(t.Namespace) ? "" : t.Namespace)))
                .ToArray();
            return types;
        }

        public static Type[] GetTypes(Assembly[] assemblies) {
            return GetTypes(assemblies, null);
        }

        public static string Generate(Assembly[] assemblies, string[] namespaces) {
            return Generate(GetTypes(assemblies, namespaces));
        }

        /// <summary>
        /// Generate TypeScript definition from .Net types
        /// </summary>
        /// <param name="types">Include here all the .Net types you want to generate TS definitions for.</param>
        /// <returns>The generated type definition (.d.ts) string</returns>
        public static string Generate(Type[] types) {
            return Generate(types, false, null, null);
        }

        public static string Generate(Type[] types, bool exact) {
            return Generate(types, exact, null, null);
        }

        public static string Generate(Type[] types, Assembly[] strictAssemblies) {
            return Generate(types, false, strictAssemblies, null);
        }

        public static string Generate(Type[] types, Assembly[] strictAssemblies, string[] strictNamespaces) {
            return Generate(types, false, strictAssemblies, strictNamespaces);
        }

        // /// <summary>
        // /// Generate TypeScript definition for just the specified types
        // /// </summary>
        // public static string GenerateExact(Type[] types) {
        //     var genInfo = TypingGenInfo.FromTypes(types);
        //     var tsNamespaces = new List<TsNamespaceGenInfo>();
        //     foreach (var ns in genInfo.NamespaceInfos) {
        //         ns.Types = ns.Types.Where(typeGenInfo => types.Any(t => SameType(typeGenInfo, t))).ToArray();
        //         if (ns.Types.Length > 0) {
        //             tsNamespaces.Add(ns);
        //         }
        //     }
        //     genInfo.NamespaceInfos = tsNamespaces.ToArray();
        //     return Generate(genInfo);
        // }

        public static string Generate(Type[] types, bool exact, [CanBeNull] Assembly[] strictAssemblies, [CanBeNull] string[] strictNamespaces) {
            var genInfo = TypingGenInfo.FromTypes(types);
            var tsNamespaces = new List<TsNamespaceGenInfo>();
            foreach (var ns in genInfo.NamespaceInfos) {
                if (exact) {
                    ns.Types = ns.Types.Where(typeGenInfo => types.Contains(typeGenInfo.CSharpType)).ToArray();
                }
                if (strictAssemblies != null && strictAssemblies.Length > 0) {
                    ns.Types = ns.Types.Where(typeGenInfo => {
                        if (strictAssemblies.Contains(typeGenInfo.CSharpType.Assembly)) {
                            return true;
                        }

                        Debug.Log($"Type {typeGenInfo.FullName} is not in the specified assemblies.");
                        return false;
                    }).ToArray();
                }
                if (strictNamespaces != null && strictNamespaces.Length > 0) {
                    ns.Types = ns.Types.Where(typeGenInfo => {
                        // Treat null namespace as global namespace
                        var nsToSearch = string.IsNullOrEmpty(typeGenInfo.Namespace) ? "" : typeGenInfo.Namespace;
                        return strictNamespaces.Any(ns => nsToSearch.StartsWith(ns));
                    }).ToArray();
                }
                if (ns.Types.Length > 0) {
                    tsNamespaces.Add(ns);
                }
            }
            genInfo.NamespaceInfos = tsNamespaces.ToArray();
            return Generate(genInfo);
        }

        /// <summary>
        /// Generate TypeScript definition from TypingGenInfo
        /// </summary>
        public static string Generate(TypingGenInfo genInfo) {
            string result = "";
            using (var jsEnv = new Puerts.JsEnv()) {
                jsEnv.UsingFunc<TypingGenInfo, bool, string>();
                var typingRender = jsEnv.ExecuteModule<Func<TypingGenInfo, bool, string>>("onejs/templates/dts.tpl.mjs", "default");
                result = typingRender(genInfo, false);
            }
            return result;
        }
    }
}