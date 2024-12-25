using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    [InitializeOnLoad]
    public static class DynamicMenuHelper {
        // Dictionary to keep track of menu items and their removal actions
        private static readonly Dictionary<string, Action> menuItemCleanupActions = new Dictionary<string, Action>();

        static DynamicMenuHelper() {
            // AddMenuItem("Tools/OneJS/Foo", () => Debug.Log("Foo menu item clicked"), 100);
            // AddMenuItem("Tools/OneJS/Bar", () => Debug.Log("Bar menu item clicked"), 101);
        }

        public static void AddMenuItem(string name, Action executeAction, int priority = 0, string shortcut = "", bool isChecked = false) {
            // Remove existing menu item if it exists
            RemoveMenuItem(name);

            // Get the internal 'Menu' class from UnityEditor assembly
            Type menuType = typeof(EditorApplication).Assembly.GetType("UnityEditor.Menu");
            if (menuType == null) {
                Debug.LogError("Could not find UnityEditor.Menu type.");
                return;
            }

            // Get both AddMenuItem and RemoveMenuItem methods
            MethodInfo addMenuItemMethod = menuType.GetMethod("AddMenuItem", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo removeMenuItemMethod = menuType.GetMethod("RemoveMenuItem", BindingFlags.Static | BindingFlags.NonPublic);

            if (addMenuItemMethod == null || removeMenuItemMethod == null) {
                Debug.LogError("Could not find required menu methods.");
                return;
            }

            // The validate function can be simplified to always return true for enabling the menu item
            Func<bool> validateFunc = () => true;

            // Prepare the parameters for the method call
            object[] parameters = new object[] {
                name,
                shortcut,
                isChecked,
                priority,
                executeAction,
                validateFunc
            };

            try {
                // Add the new menu item
                addMenuItemMethod.Invoke(null, parameters);

                // Store the cleanup action
                menuItemCleanupActions[name] = () => {
                    try {
                        removeMenuItemMethod.Invoke(null, new object[] { name });
                    } catch (Exception e) {
                        Debug.LogError($"Failed to remove menu item '{name}': {e.Message}");
                    }
                };
            } catch (Exception e) {
                Debug.LogError($"Failed to add menu item '{name}': {e.Message}");
            }
        }

        private static void RemoveMenuItem(string name) {
            if (menuItemCleanupActions.TryGetValue(name, out Action cleanupAction)) {
                cleanupAction?.Invoke();
                menuItemCleanupActions.Remove(name);
            }
        }

        // Optional: Method to clear all dynamic menu items
        public static void ClearAllMenuItems() {
            foreach (var cleanupAction in menuItemCleanupActions.Values) {
                cleanupAction?.Invoke();
            }
            menuItemCleanupActions.Clear();
        }
    }
}