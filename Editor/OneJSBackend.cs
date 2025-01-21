using System;
using System.IO;
using Puerts;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    public class OneJSBackend {
        public static string GetCurrentBackend() {
            int i = PuertsDLL.GetLibBackend((IntPtr)0);
            switch (i) {
                case 0:
                    return "V8";
                case 1:
                    return "NodeJS";
                case 2:
                    return "QuickJS";
                default:
                    return "Unknown";
            }
        }

        public static void SwitchBackend(string backend) {
            if (backend == GetCurrentBackend()) {
                Debug.Log("You are already using " + backend + " backend. No need to switch.");
                return;
            }
            if (EditorUtility.DisplayDialog("Unity Editor needs to be closed",
                    "We can't reliably unload the native plugin while the Unity Editor is running. Please close the Unity Editor and use `npm run switch " + backend.ToLower() + "` in the terminal to switch the backend.",
                    "Okay", "Cancel")) {
            }
            // EditorApplication.OpenProject(Directory.GetCurrentDirectory());
        }
    }
}