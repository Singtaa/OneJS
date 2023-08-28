using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    public class OneJSMenuItems {
        [MenuItem("OneJS/Open GeneratedCode Folder")]
        static void OpenGeneratedCodeFolder() {
            OpenDir(Path.Combine(Application.dataPath, "..", "Temp", "GeneratedCode", "OneJS"));
        }

        static void OpenDir(string path) {
#if UNITY_STANDALONE_WIN
            var processName = "explorer.exe";
#elif UNITY_STANDALONE_OSX
            var processName = "open";
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
