using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OneJS.Editor;
using OneJS.Attributes;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Weaver {
    [InitializeOnLoad]
    public class InspectorWatcher {
        static EditorWindow _inspectorWindow;
        static HashSet<EditorWindow> _openWindows;

        static Type _inspectorWindowType;

        static InspectorWatcher() {
            _inspectorWindowType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            _openWindows = new HashSet<EditorWindow>(Resources.FindObjectsOfTypeAll<EditorWindow>());
            // Selection.selectionChanged += Update;
            EditorApplication.update += Update;
            Update();
        }

        static void Update() {
            var inspectorWindows = Resources.FindObjectsOfTypeAll(_inspectorWindowType);
            foreach (var inspectorWindow in inspectorWindows) {
                var root = (inspectorWindow as EditorWindow).rootVisualElement;
                var target = GetInspectorTarget(inspectorWindow as EditorWindow);
                if (root != null && target != null) {
                    var hasOneJSAttribute = HasOneJSAttribute(target);
                    root.EnableInClassList("inspector-root", true);
                    root.EnableInClassList("onejs-so-selected", hasOneJSAttribute);
                    root.EnableInClassList("theme-dark", EditorGUIUtility.isProSkin);
                    root.EnableInClassList("theme-light", !EditorGUIUtility.isProSkin);
                    if (hasOneJSAttribute) {
                        var scrollView = root.Q<ScrollView>();
                        if (scrollView != null) {
                            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                        }
                        var holder = root.Q(null, "unity-inspector-editors-list");
                        // Find the child element whose name starts with selected.GetType().Name + "Editor_"
                        // var editors = holder?.Children().Where(e => e.name.StartsWith(selected.GetType().Name + "Editor_")).ToArray();
                        var editors = holder?.Children().Where(e => e.GetType().Name == "EditorElement").ToArray();
                        if (editors == null || editors.Length == 0)
                            continue;
                        foreach (var editor in editors) {
                            var inspectorElement = editor.Q<InspectorElement>();
                            var isTargetName = editor.name.StartsWith(target.GetType().Name + "Editor_");
                            var good = inspectorElement != null && isTargetName;
                            editor.EnableInClassList("inspector-editor-element", good);
                            editor.EnableInClassList("none-editor-element", !good);
                            if (inspectorElement != null) {
                                // This is a a fix for when the inspector element somehow becomes hidden 
                                // during editor domain reload
                                inspectorElement.style.display = DisplayStyle.Flex;
                            }
                        }
                    }
                }
            }
        }

        public static bool HasOneJSAttribute(UnityEngine.Object obj) {
            if (obj is ScriptableObject)
                return Attribute.IsDefined(obj.GetType(), typeof(OneJSAttribute));
            return false;
        }

        public static UnityEngine.Object GetInspectorTarget(EditorWindow inspectorWindow) {
            if (inspectorWindow == null) return null;

            if (_inspectorWindowType == null || !_inspectorWindowType.IsInstanceOfType(inspectorWindow)) return null;

            var getInspectedObjectMethod = _inspectorWindowType.GetMethod("GetInspectedObject",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (getInspectedObjectMethod != null) {
                return getInspectedObjectMethod.Invoke(inspectorWindow, null) as UnityEngine.Object;
            }

            return null;
        }

        public static T FindScriptableObject<T>(string name) where T : ScriptableObject {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name} {name}");
            if (guids.Length == 0)
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}