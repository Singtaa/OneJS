using System;
using System.Collections.Generic;
using System.Linq;
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

        static InspectorWatcher() {
            _openWindows = new HashSet<EditorWindow>(Resources.FindObjectsOfTypeAll<EditorWindow>());
            // Selection.selectionChanged += Update;
            EditorApplication.update += Update;
            Update();
        }

        static void Update() {
            var selected = Selection.activeObject;
            var inspectorWindowType = typeof(Editor).Assembly.GetType("UnityEditor.PropertyEditor");
            var inspectorWindows = Resources.FindObjectsOfTypeAll(inspectorWindowType);
            var hasOneJSAttribute = HasOneJSAttribute(selected);
            foreach (var inspectorWindow in inspectorWindows) {
                var targetEle = (inspectorWindow as EditorWindow).rootVisualElement;
                if (targetEle != null) {
                    targetEle.EnableInClassList("inspector-root", true);
                    targetEle.EnableInClassList("onejs-so-selected", hasOneJSAttribute);
                    targetEle.EnableInClassList("theme-dark", EditorGUIUtility.isProSkin);
                    targetEle.EnableInClassList("theme-light", !EditorGUIUtility.isProSkin);
                    if (hasOneJSAttribute) {
                        var scrollView = targetEle.Q<ScrollView>();
                        if (scrollView != null) {
                            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                        }
                        var holder = targetEle.Q(null, "unity-inspector-editors-list");
                        // Find the child element whose name starts with selected.GetType().Name + "Editor_"
                        // var editors = holder?.Children().Where(e => e.name.StartsWith(selected.GetType().Name + "Editor_")).ToArray();
                        var editors = holder?.Children().Where(e => e.GetType().Name == "EditorElement").ToArray();
                        if (editors == null || editors.Length == 0)
                            continue;
                        foreach (var editor in editors) {
                            var hasInspectorElement = editor.Q<InspectorElement>() != null;
                            var isTargetName = editor.name.StartsWith(selected.GetType().Name + "Editor_");
                            var good = hasInspectorElement && isTargetName;
                            editor.EnableInClassList("inspector-editor-element", good);
                            editor.EnableInClassList("none-editor-element", !good);
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

        public static T FindScriptableObject<T>(string name) where T : ScriptableObject {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name} {name}");
            if (guids.Length == 0)
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}