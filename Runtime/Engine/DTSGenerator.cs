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
        public string[] whitelistedTypes = new string[] { };
        [PlainString]
        public string[] blacklistedTypes = new string[] {
            // "UnityEngine.UIElements.ITransform", "UnityEngine.UIElements.ICustomStyle"
        };
        [Tooltip("Relative to the OneJS WorkingDir.")]
        public string savePath = "app.d.ts";
        [Tooltip("Check to only generate typings for the declared Assemblies.")]
        public bool strictAssemblies = false;
        [Tooltip("Check to only generate typings for the declared namespaces.")]
        public bool strictNamespaces = false;
        [Tooltip("Check to only generate exact typings (no supporting types will be generated).")]
        public bool exact = false;
        [Tooltip("Check to only generate typings for whitelisted types (supporting types will still be generated unless 'Exact' is checked).")]
        public bool whitelistOnly = false;
        [Tooltip("Check to also generate typings for the global objects defined on ScriptEngine.")]
        public bool includeGlobalObjects = true;
    }
}