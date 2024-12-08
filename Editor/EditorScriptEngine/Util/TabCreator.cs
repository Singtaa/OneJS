using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    public class TabCreator {
        public EditorWindow CreateNewTab() {
            var lastFocusedWindow = EditorWindow.focusedWindow;
            if (lastFocusedWindow == null) {
                Debug.LogError("No focused window found");
                return null;
            }
            var parentField = typeof(EditorWindow).GetField("m_Parent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (parentField == null) {
                Debug.LogError("Could not find m_Parent field");
                return null;
            }
            var dockArea = parentField.GetValue(lastFocusedWindow);
            if (dockArea == null) {
                Debug.LogError("Dock area is null");
                return null;
            }
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            var dockAreaType = dockArea.GetType();
            var addTabMethod = dockAreaType.GetMethod("AddTab",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(EditorWindow), typeof(bool) },
                null);

            if (addTabMethod == null) {
                Debug.LogError($"Could not find AddTab method in type {dockAreaType.FullName}");
                var methods = dockAreaType.GetMethods(BindingFlags.Instance |
                                                      BindingFlags.NonPublic | BindingFlags.Public);
                Debug.Log("Available methods:");
                foreach (var method in methods) {
                    Debug.Log($"Method: {method.Name}");
                    Debug.Log($"Parameters: {string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}");
                }
                return null;
            }

            try {
                addTabMethod.Invoke(dockArea, new object[] { window, true });
            } catch (Exception e) {
                Debug.LogError($"Error invoking AddTab: {e}");
                return null;
            }

            window.titleContent = new GUIContent("New Tab");
            window.Focus();
            window.rootVisualElement.Add(new Label("Hello, World!"));

            return window;
        }
    }
}