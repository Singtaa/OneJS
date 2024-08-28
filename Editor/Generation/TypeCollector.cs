using System;
using System.Collections.Generic;
using System.Reflection;

namespace OneJS.Editor {
    public class TypeCollector {
        public static List<Type> GetAllTypes(Assembly asm) {
            List<Type> allTypes = new List<Type>();

            foreach (Type type in asm.GetTypes()) {
                allTypes.Add(type);
                CollectNestedTypes(type, allTypes);
            }

            return allTypes;
        }

        private static void CollectNestedTypes(Type type, List<Type> typeList) {
            foreach (Type nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) {
                typeList.Add(nestedType);
                CollectNestedTypes(nestedType, typeList);
            }
        }
    }
}