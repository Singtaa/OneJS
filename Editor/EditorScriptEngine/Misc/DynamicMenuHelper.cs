using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    [InitializeOnLoad]
    public static class DynamicMenuHelper {
        static DynamicMenuHelper() {
            // Automatically add the desired menu items when Unity loads the editor
            AddMenuItem("Tools/OneJS/Foo", () => Debug.Log("Foo menu item clicked"), 100);
            AddMenuItem("Tools/OneJS/Bar", () => Debug.Log("Bar menu item clicked"), 101);
        }

        public static void AddMenuItem(string name, Action executeAction, int priority, string shortcut = "", bool isChecked = false) {
            // Get the internal 'Menu' class from UnityEditor assembly
            Type menuType = typeof(EditorApplication).Assembly.GetType("UnityEditor.Menu");
            if (menuType == null) {
                Debug.LogError("Could not find UnityEditor.Menu type.");
                return;
            }

            // Get the 'AddMenuItem' method
            MethodInfo addMenuItemMethod = menuType.GetMethod("AddMenuItem", BindingFlags.Static | BindingFlags.NonPublic);
            if (addMenuItemMethod == null) {
                Debug.LogError("Could not find AddMenuItem method.");
                return;
            }

            // The validate function can be simplified to always return true for enabling the menu item
            Func<bool> validateFunc = () => true; // Menu item always enabled

            // Prepare the parameters for the method call
            object[] parameters = new object[] {
                name,
                shortcut,
                isChecked,
                priority,
                executeAction,
                validateFunc
            };

            // Invoke the method using reflection
            try {
                addMenuItemMethod.Invoke(null, parameters);
            } catch (Exception e) {
                Debug.LogError($"Failed to add menu item '{name}': {e.Message}");
            }
        }
    }
}