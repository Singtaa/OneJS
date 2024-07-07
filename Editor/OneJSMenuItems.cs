using Puerts.Editor.Generator;
using UnityEditor;

namespace OneJS.Editor {
    public class OneJSMenuItems {
        [MenuItem("Tools/OneJS/Generate StaticWrappers", false, 1)]
        private static void GenerateStaticWrappers() {
            UnityMenu.ClearAll();
            UnityMenu.GenerateCode();
            UnityMenu.GenRegisterInfo();
        }
    }
}