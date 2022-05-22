using System;
using System.Linq;
using System.Reflection;
using Esprima.Ast;
using UnityEngine;
using UnityEngine.Scripting;

namespace OneJS.Extensions {
    public static class GameObjectExts {
        public static T AddComponent<T>(this GameObject go, T toAdd) where T : Component {
            return go.AddComponent<T>().GetCopyOf(toAdd) as T;
        }

        public static bool HasComp<T>(this GameObject go) where T : Component {
            return go.TryGetComponent<T>(out T res);
        }

        public static T GetAddComp<T>(this GameObject go) where T : Component {
            T res;
            if (!go.TryGetComponent<T>(out res))
                res = go.AddComponent<T>();
            return res;
        }

        public static bool TryAddComp(this GameObject go, string componentName) {
            return TryAddComp(go, componentName, out Component comp);
        }

        public static bool TryAddComp(this GameObject go, string componentName, out Component comp) {
            var type = FindType(componentName);
            if (!go.TryGetComponent(type, out comp)) {
                comp = go.AddComponent(type);
                return true;
            }
            return false;
        }

        public static Component AddComp(this GameObject go, string componentName) {
            var type = FindType(componentName);
            return go.AddComponent(type);
        }

        public static Component AddComp(this GameObject go, Type componentType) {
            return go.AddComponent(componentType);
        }

        public static bool TryGetComp(this GameObject go, string componentName, out Component comp) {
            var type = FindType(componentName);
            return go.TryGetComponent(type, out comp);
        }
        
        public static bool TryGetComp(this GameObject go, Type componentType, out Component comp) {
            return go.TryGetComponent(componentType, out comp);
        }

        public static Component GetComp(this GameObject go, string componentName) {
            var type = FindType(componentName);
            return go.GetComponent(type);
        }

        public static Component GetComp(this GameObject go, Type componentType) {
            return go.GetComponent(componentType);
        }

        private static Type FindType(string name) {
            // var asmNames = new[] { "AssetMakerStage", "UnityEngine.CoreModule", "UnityEngine.CoreModule", "Obi" };
            // var type = AppDomain.CurrentDomain.GetAssemblies().Where(a => {
            //     foreach (var asmName in asmNames) {
            //         if (a.FullName.Contains(asmName)) {
            //             return true;
            //         }
            //     }
            //     return false;
            // }).Select(a => a.GetTypes().Where(t => t.Name == name).FirstOrDefault()).FirstOrDefault();

            var type = FindTypeInAssembly(name, typeof(GameObject).Assembly);
            if (type == null)
                type = FindTypeInAssembly(name, typeof(MeshCollider).Assembly);
            if (type == null)
                throw new Exception("[GameObjectExtensions] Cannot Find type: " + name);
            return type;
        }

        private static Type FindTypeInAssembly(string name, Assembly asm) {
            var res = asm.GetTypes().Where(t => t.Name == name).FirstOrDefault();
            return res;
        }
    }
}