// using System;
// using System.Collections.Generic;
// using UnityEditor;
// using UnityEditor.UIElements;
// using UnityEngine;
// using UnityEngine.UIElements;
//
// namespace OneJS.Editor {
//     [InitializeOnLoad]
//     [CustomEditor(typeof(Component), true)]
//     [CanEditMultipleObjects]
//     public class BaseEditor : UnityEditor.Editor {
//         #region Statics
//         public static BaseEditor Instance;
//         static Dictionary<Type, Action<VisualElement, object, SerializedObject>> renderers = new();
//
//         static BaseEditor() {
//             Clear();
//         }
//
//         public static void Register(Type type, Action<VisualElement, object, SerializedObject> renderer) {
//             renderers[type] = renderer;
//         }
//
//         public static void Clear() {
//             renderers.Clear();
//             Instance = null;
//         }
//         #endregion
//
//         VisualElement _root;
//
//         public override VisualElement CreateInspectorGUI() {
//             Instance = null;
//             if (renderers.TryGetValue(target.GetType(), out var veMaker)) {
//                 _root = new VisualElement();
//                 veMaker(_root, target, serializedObject);
//                 Instance = this;
//                 return _root;
//             }
//             return base.CreateInspectorGUI();
//         }
//
//         public void Refresh() {
//             if (_root != null) {
//                 _root.Clear();
//                 renderers[target.GetType()](_root, target, serializedObject);
//             }
//         }
//     }
// }