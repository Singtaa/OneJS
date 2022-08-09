using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS {
    public class Service {
        static Dictionary<Type, object> dict = new Dictionary<Type, object>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            dict.Clear();
        }

        public static T Get<T>() where T : class {
            return Get(typeof(T)) as T;
        }

        public static object Get(Type type) {
            if (!dict.ContainsKey(type)) {
                throw new Exception($"Cannot find Service {type.Name}");
            }
            return dict[type];
        }

        public static void Set<T>(T obj) where T : class {
            Set(obj, typeof(T));
        }

        public static void Set(object obj, Type type) {
            if (dict.ContainsKey(type)) {
                Debug.LogError($"Cannot Service.Set {type.Name}. It already exists.");
                return;
            }
            dict.Add(type, obj);
        }
    }
}