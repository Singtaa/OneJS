using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    public class BackendMenuInfo {
        public string name;
        public string define;
    }

    [InitializeOnLoad]
    public class OneJSMenuItems {
        static readonly BackendMenuInfo[] BackendDefines = {
            new BackendMenuInfo { name = "QuickJS", define = "ONEJS_QUICKJS" },
            new BackendMenuInfo { name = "V8", define = "ONEJS_V8" },
            new BackendMenuInfo { name = "NodeJS", define = "ONEJS_NODEJS" },
        };

        static readonly string[] TgzUrls = {
            "https://github.com/Tencent/puerts/releases/download/Unity_v2.0.5/PuerTS_Quickjs_2.0.5.tgz",
            "https://github.com/Tencent/puerts/releases/download/Unity_v2.0.5/PuerTS_V8_2.0.5.tgz",
            "https://github.com/Tencent/puerts/releases/download/Unity_v2.0.5/PuerTS_Nodejs_2.0.5.tgz"
        };

        static OneJSMenuItems() {
            EnsureDefaultBackend();
        }

        [MenuItem("Tools/OneJS/QuickJS", false, 1)]
        private static void SetQuickJS() => SetBackend(0);

        [MenuItem("Tools/OneJS/V8", false, 2)]
        private static void SetV8() => SetBackend(1);

        [MenuItem("Tools/OneJS/NodeJS", false, 3)]
        private static void SetNodeJS() => SetBackend(2);

        private static void SetBackend(int index) {
            SwitchBackend(TgzUrls[index], () => {
                var backendInfo = BackendDefines[index];
                var activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(activeBuildTargetGroup);
                PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out string[] defines);
                var list = defines.ToList();
                list.RemoveAll(d => BackendDefines.Any(bi => bi.define == d));
                list.Add(backendInfo.define);
                PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", list));
                Debug.Log($"Switched to {backendInfo.name} backend");
                AssetDatabase.Refresh();
            });
        }

        private static void EnsureDefaultBackend() {
            var activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(activeBuildTargetGroup);
            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out string[] defines);
            if (!BackendDefines.Any(d => defines.Contains(d.define))) {
                SetBackend(0);
            }
        }

        [MenuItem("Tools/OneJS/QuickJS", true, 1)]
        private static bool ValidateQuickJS() => ValidateBackend(0);

        [MenuItem("Tools/OneJS/V8", true, 2)]
        private static bool ValidateV8() => ValidateBackend(1);

        [MenuItem("Tools/OneJS/NodeJS", true, 3)]
        private static bool ValidateNodeJS() => ValidateBackend(2);

        private static bool ValidateBackend(int index) {
            var backendInfo = BackendDefines[index];
            bool isActive = IsBackendActive(backendInfo.define);
            Menu.SetChecked($"Tools/OneJS/{backendInfo.name}", isActive);
            return true;
        }

        private static bool IsBackendActive(string backendDefine) {
            var activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(activeBuildTargetGroup);
            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out string[] defines);

            return defines.Contains(backendDefine);
        }

        static void SwitchBackend(string tgzUrl, Action onComplete) {
            Debug.Log("Switching backend...");
            ObtainTgzFile(tgzUrl, (downloadedPath) => {
                string scriptPath = new StackTrace(true).GetFrame(0).GetFileName();
                var onejsPath = Directory.GetParent(Directory.GetParent(scriptPath).FullName).FullName;
                var projectPath = Directory.GetParent(Application.dataPath).FullName;
                var tmpPath = Path.Combine(projectPath, "Temp");
#if ONEJS_DEBUG
                Debug.Log(">>> Tmp location: " + tmpPath);
#endif
                // Extract tgz into tmp location
                var upmPath = Path.Combine(tmpPath, "upm");
                if (Directory.Exists(upmPath))
                    Directory.Delete(upmPath, true);
                try {
                    Extract(File.ReadAllBytes(downloadedPath), tmpPath);
                } catch (Exception e) {
                    Debug.Log("Could not extract downloaded tgz file. Please try again or manually check if the PuerTS .tgz files are corrupted in your {ProjectDir}/Temp folder.");
                    throw e;
                }
                // Make sure the 2 asmdef files stay the same by copying them over from the existing OneJS package
                File.Copy(Path.Combine(onejsPath, "Puerts/Editor/com.tencent.puerts.core.Editor.asmdef"), Path.Combine(tmpPath, "upm/Editor/com.tencent.puerts.core.Editor.asmdef"), true);
                File.Copy(Path.Combine(onejsPath, "Puerts/Runtime/com.tencent.puerts.core.asmdef"), Path.Combine(tmpPath, "upm/Runtime/com.tencent.puerts.core.asmdef"), true);
                // Move the extracted folder over to replace Puerts folder
                var puertsPath = Path.Combine(onejsPath, "Puerts");
                try {
                    if (Directory.Exists(puertsPath))
                        Directory.Delete(puertsPath, true);
                } catch (Exception e) {
                    Debug.Log("Cannot replace your current JS backend because native plugins were already loaded. You need to restart Unity and try this again.");
                    throw e;
                }
                MoveDirectory(upmPath, puertsPath);
                onComplete();
            });
        }

        public static void ObtainTgzFile(string fileUrl, Action<string> onComplete) {
            var fileName = Path.GetFileName(fileUrl);
            var projectPath = Directory.GetParent(Application.dataPath).FullName;
            var tmpPath = Path.Combine(projectPath, "Temp");
            string savePath = Path.Combine(tmpPath, fileName);

            if (File.Exists(savePath)) {
#if ONEJS_DEBUG
                Debug.Log(">>> File already exists in cache: " + savePath);
#endif
                onComplete(savePath);
                return;
            }

            DownloadFile(fileUrl, savePath, () => {
                onComplete(savePath);
            });
        }

        static IEnumerator DelayedExec(Action callback) {
            yield return new WaitForSeconds(1);
            callback();
        }

        static void DownloadFile(string fileUrl, string savePath, Action onComplete) {
            Debug.Log("Downloading " + fileUrl);
            UnityWebRequest www = UnityWebRequest.Get(fileUrl);
            www.downloadHandler = new DownloadHandlerFile(savePath);

            UnityWebRequestAsyncOperation asyncOperation = www.SendWebRequest();
            asyncOperation.completed += (asyncOp) => {
                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError) {
                    Debug.LogError(www.error);
                } else {
                    onComplete?.Invoke();
#if ONEJS_DEBUG
                    Debug.Log(">>> Temporary file successfully downloaded and saved to: " + savePath);
#endif
                }
            };
        }

        static void Extract(byte[] bytes, string path) {
            Stream inStream = new MemoryStream(bytes);
            Stream gzipStream = new GZipInputStream(inStream);

            TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream);
            tarArchive.ExtractContents(path);
            tarArchive.Close();
            gzipStream.Close();
            inStream.Close();
        }

        /// <summary>
        /// Directory.Move() alternative that works across different volumes
        /// </summary>
        /// <param name="sourceDir"></param>
        /// <param name="destDir"></param>
        /// <exception cref="DirectoryNotFoundException"></exception>
        /// <exception cref="IOException"></exception>
        public static void MoveDirectory(string sourceDir, string destDir) {
            if (!Directory.Exists(sourceDir)) {
                throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceDir}");
            }

            if (Directory.Exists(destDir)) {
                throw new IOException($"Destination directory already exists: {destDir}");
            }

            try {
                // Create the destination directory
                Directory.CreateDirectory(destDir);

                // Copy all the files
                foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
                    string destFile = file.Replace(sourceDir, destDir);
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile));
                    File.Copy(file, destFile, true);
                }

                // Copy all the subdirectories
                foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories)) {
                    string destSubDir = dir.Replace(sourceDir, destDir);
                    Directory.CreateDirectory(destSubDir);
                }

                // Delete the original directory and its contents
                Directory.Delete(sourceDir, true);
            } catch (Exception ex) {
                throw new IOException($"An error occurred while moving the directory: {ex.Message}", ex);
            }
        }

        public static void OpenDir(string path) {
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