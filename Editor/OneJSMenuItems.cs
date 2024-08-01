using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Puerts.Editor.Generator;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    public class OneJSMenuItems {
        [MenuItem("Tools/OneJS/Generate StaticWrappers", false, 1)]
        static void GenerateStaticWrappers() {
            UnityMenu.ClearAll();
            UnityMenu.GenerateCode();
            UnityMenu.GenRegisterInfo();
        }
    }
}