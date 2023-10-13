using System;
using System.Collections.Generic;
using System.Reflection;
using OneJS.Extensions;
using UnityEngine;

namespace OneJS.Editor.TSDefinitions {
    public class TsDefConverterObjectContext {
        public int UnwrapOrder { get; set; }

        public Type Type { get; set; }
        public ConstructorInfo[] Ctors { get; set; }
        public FieldInfo[] Fields { get; set; }
        public EventInfo[] Events { get; set; }
        public MethodInfo[] Methods { get; set; }
        public PropertyInfo[] Properties { get; set; }
        public FieldInfo[] StaticFields { get; set; }
        public EventInfo[] StaticEvents { get; set; }
        public MethodInfo[] StaticMethods { get; set; }
        public PropertyInfo[] StaticProperties { get; set; }


        public TsDefConverterObjectContext(Type type, TSDefConverterOptions options, int unwrapOrder) {
            UnwrapOrder = unwrapOrder;

            Type = type;

            var flags = BindingFlags.Public;

            if (options.IncludeBaseMembers) {
                flags |= BindingFlags.FlattenHierarchy;
            } else {
                flags |= BindingFlags.DeclaredOnly;
            }

            Ctors = Type.GetConstructors(flags | BindingFlags.Instance);
            Fields = Type.GetFields(flags | BindingFlags.Instance);
            Events = Type.GetEvents(flags | BindingFlags.Instance);
            Methods = Type.GetMethods(flags | BindingFlags.Instance);
            Properties = Type.GetProperties(flags | BindingFlags.Instance);
            StaticFields = Type.GetFields(flags | BindingFlags.Static);
            StaticEvents = Type.GetEvents(flags | BindingFlags.Static);
            StaticMethods = Type.GetMethods(flags | BindingFlags.Static);
            StaticProperties = Type.GetProperties(flags | BindingFlags.Static);
        }
    }

    public class TsDefConverterContext {
        public static TsDefConverterContext Instance { get; private set; }

        private int _unwrapOrder = 0;

        public TSDefConverterOptions Options { get; set; }

        public Dictionary<Type, TsDefConverterObjectContext> ObjectContexts { get; set; } = new();

        public static TsDefConverterContext NewContext(TSDefConverterOptions opts) {
            var context = new TsDefConverterContext {
                Options = opts
            };
            Instance = context;
            return context;
        }

        public bool LoadObjectContext() {
            if (Options.Type == null && (Options.TypesInNamespace == null || Options.TypesInNamespace.Length == 0)) {
                Debug.LogError("Type or TypesInNamespace must be set");
                return false;
            }

            if (Options.Type != null) {
                return LoadObjectContext(Options.Type, true);
            }

            foreach (var type in Options.TypesInNamespace) {
                LoadObjectContext(type, true);
            }

            return true;
        }

        public bool LoadObjectContext(Type type, bool isRoot = false) {
            if (ObjectContexts.ContainsKey(type)) return true;

            if (Options.ExcludeUnityBaseTypes) {
                if (type.Namespace?.StartsWith("UnityEngine") == true)
                    return false;
            }

            var ctx = new TsDefConverterObjectContext(type, Options, _unwrapOrder);
            _unwrapOrder++;

            ObjectContexts.Add(type, ctx);

            if (Options.ExtractBaseDefinitions) {
                foreach (var baseType in type.GetBaseTypes()) {
                    LoadObjectContext(baseType);
                }
            }

            return true;
        }
    }
}
