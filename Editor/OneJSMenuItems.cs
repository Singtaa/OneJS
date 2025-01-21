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
        const string MenuPathQuickJS = "Tools/OneJS/Backend/QuickJS";
        const string MenuPathV8 = "Tools/OneJS/Backend/V8";
        const string MenuPathNodeJS = "Tools/OneJS/Backend/NodeJS";

        [MenuItem(MenuPathQuickJS)]
        static void SelectQuickJS() {
            SwitchBackend("QuickJS");
        }

        [MenuItem(MenuPathV8)]
        static void SelectV8() {
            SwitchBackend("V8");
        }

        [MenuItem(MenuPathNodeJS)]
        static void SelectNodeJS() {
            SwitchBackend("NodeJS");
        }

        [MenuItem(MenuPathQuickJS, true)]
        [MenuItem(MenuPathV8, true)]
        [MenuItem(MenuPathNodeJS, true)]
        static bool ValidateMenu() {
            UpdateMenuCheckmarks();
            return true;
        }

        static void SwitchBackend(string backend) {
            OneJSBackend.SwitchBackend(backend); // Call OneJSBackend to switch backend
            UpdateMenuCheckmarks();
        }

        static void UpdateMenuCheckmarks() {
            string selectedBackend = OneJSBackend.GetCurrentBackend();

            Menu.SetChecked(MenuPathQuickJS, selectedBackend == "QuickJS");
            Menu.SetChecked(MenuPathV8, selectedBackend == "V8");
            Menu.SetChecked(MenuPathNodeJS, selectedBackend == "NodeJS");
        }

        [MenuItem("Tools/OneJS/Generate StaticWrappers", false)]
        static void GenerateStaticWrappers() {
            UnityMenu.ClearAll();
            UnityMenu.GenerateCode();
            UnityMenu.GenRegisterInfo();
        }

        [MenuItem("Tools/OneJS/Open GeneratedCode Folder", false)]
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