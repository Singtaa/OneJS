using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    public class OneJSMenuItems {
        private const string V8_SYMBOL_STRING = "ONEJS_V8";
        private const string EnableV8MenuItemPath = "Tools/OneJS/Enable V8";

        [MenuItem(EnableV8MenuItemPath, false, priority: 0)]
        private static void ToggleDefine() {
            var activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            var activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(activeBuildTargetGroup);
            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out string[] defines);

            if (defines.Contains(V8_SYMBOL_STRING)) {
                // currentDefines = currentDefines.Replace(V8_SYMBOL_STRING + ";", "").Replace(V8_SYMBOL_STRING, "");
                var newDefines = defines.Where(d => d != V8_SYMBOL_STRING).ToArray();
                PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, newDefines);
            } else {
                // currentDefines += ";" + V8_SYMBOL_STRING;
                var newDefines = defines.ToList();
                newDefines.Add(V8_SYMBOL_STRING);
                PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, newDefines.ToArray());
            }
        }

        [MenuItem(EnableV8MenuItemPath, true)]
        private static bool ValidateToggleDefine() {
            Menu.SetChecked(EnableV8MenuItemPath, IsDefineEnabled());
            return true;
        }

        private static bool IsDefineEnabled() {
            var activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            var activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(activeBuildTargetGroup);
            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out string[] defines);
            return defines.Contains(V8_SYMBOL_STRING);
        }

        [MenuItem("Tools/OneJS/Open GeneratedCode Folder")]
        static void OpenGeneratedCodeFolder() {
            var path = Path.Combine(Application.dataPath, "..", "Temp", "GeneratedCode", "OneJS");
            if (Directory.Exists(path)) {
                OpenDir(path);
            } else {
                Debug.Log($"Cannot find GeneratedCode folder at {path}. It may not have been generated yet.");
            }
        }

        static void OpenDir(string path) {
#if UNITY_STANDALONE_WIN
            var processName = "explorer.exe";
#elif UNITY_STANDALONE_OSX
            var processName = "open";
#elif UNITY_STANDALONE_LINUX
            var processName = "xdg-open";
#else
            var processName = "unknown";
            Debug.LogWarning("Unknown platform. Cannot open folder");
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
