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

        [MenuItem("Tools/OneJS/Open GeneratedCode Folder", false, 21)]
        static void OpenGeneratedCodeFolder() {
            var path = Path.Combine(Application.dataPath, "..", "Temp", "GeneratedCode", "OneJS");
            if (Directory.Exists(path)) {
                OpenDir(path);
            } else {
                Debug.Log($"Cannot find GeneratedCode folder at {path}. It may not have been generated yet.");
            }
        }

        static void OpenDir(string path) {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var processName = "explorer.exe";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var processName = "open";
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            var processName = "xdg-open";
#else
            var processName = "unknown";
            UnityEngine.Debug.LogWarning("Unknown platform. Cannot open folder");
#endif
            var argStr = $"\"{Path.GetFullPath(path)}\"";
            var proc = new Process() {
                StartInfo = new ProcessStartInfo() {
                    FileName = processName,
                    Arguments = argStr,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
            };
            proc.Start();
        }
    }
}