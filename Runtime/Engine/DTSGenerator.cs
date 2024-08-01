using UnityEngine;

namespace OneJS {
    [RequireComponent(typeof(ScriptEngine))] [AddComponentMenu("OneJS/DTS Generator")]
    public class DTSGenerator : MonoBehaviour {
        [PlainString]
        public string[] assemblies = new string[] {
            // "UnityEngine.CoreModule", "UnityEngine.PhysicsModule", "UnityEngine.UIElementsModule",
            // "UnityEngine.IMGUIModule", "UnityEngine.TextRenderingModule",
            // "Unity.Mathematics", "OneJS.Runtime"
        };
        [PlainString]
        public string[] namespaces = new string[] {
            // "UnityEngine", "UnityEngine.UIElements", "Unity.Mathematics", "OneJS", "OneJS.Dom", "OneJS.Utils"
        };
        [PlainString]
        public string[] blacklistedTypes = new string[] {
            // "UnityEngine.UIElements.ITransform", "UnityEngine.UIElements.ICustomStyle"
        };
        [Tooltip("Relative to the OneJS WorkingDir.")]
        public string savePath = "app.d.ts";
        [Tooltip("Check to only generate typings for the declared namespaces.")]
        public bool compact = true;
        [Tooltip("Check to also generate typings for the global objects defined on ScriptEngine.")]
        public bool includeGlobalObjects = true;
    }
}