using System.Diagnostics;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using NaughtyAttributes;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS {
    [DefaultExecutionOrder(50)]
    public class Bundler : MonoBehaviour {
        [InfoBox("This component takes care of scripts bundling for both editor and runtime. OneJS keeps everything " +
                 "under `{ProjectDir}/OneJS`. For standalone app builds, scripts will be extracted into " +
                 "`{persistentDataPath}/OneJS` automatically.")]
        [Tooltip(
            "Reset ScriptLib folder on every Game Start. Useful for updating the ScriptLib folder after upgrading" +
            " OneJS, or updating a stale ScriptLib folder on your mobile device.")]
        [SerializeField] [Label("Extract ScriptLib on Start")] bool _extractScriptLibOnStart = false;

        [Tooltip(
            "These subdirectories (directly under OneJS) will be ignored during standalone app bundling/extration. " +
            "One common use for them is user-provided addons. You want these directories to survive OneJS updates.")]
        [SerializeField]
        [Label("Sub-Directories to Ignore")] string[] _subDirectoriesToIgnore = new[] { "Addons", "Modules" };

        [Foldout("ASSETS")]
        [Tooltip("This is the zip file of your bundled scripts.")]
        [SerializeField] TextAsset _scriptsBundleZip;

        [Foldout("ASSETS")] [SerializeField]
        [Tooltip("The zip file that contains sample scripts. You normally don't need to touch this.")]
        TextAsset _samplesZip;

        [Foldout("ASSETS")]
        [Tooltip("This is the zip file that OneJS uses to fill your " +
                 "ScriptLib folder if one isn't found under {ProjectDir}/OneJS. You normally don't need to touch this.")]
        [Label("ScriptLib Zip")]
        [SerializeField]
        TextAsset _scriptLibZip;

        [Foldout("ASSETS")]
        [Tooltip("Default vscode settings.json. If one isn't found under {ProjectDir}/OneJS/.vscode, " +
                 "this is the template that will be copied over. You normally don't need to touch this.")]
        [Label("VSCode Settings")]
        [SerializeField]
        TextAsset _vscodeSettings;

        [Foldout("ASSETS")]
        [Tooltip("Default tsconfig.json. If one isn't found under {ProjectDir}/OneJS, " +
                 "this is the template that will be copied over. You normally don't need to touch this.")]
        [Label("TS Config")]
        [SerializeField]
        TextAsset _tsconfig;

        string _onejsVersion = "1.4.3";

        void Awake() {
#if UNITY_EDITOR
            var versionString = PlayerPrefs.GetString("OneJSVersion", "0.0.0");
            if (_extractScriptLibOnStart || versionString != _onejsVersion) {
                ExtractScriptLib();
                PlayerPrefs.SetString("OneJSVersion", _onejsVersion);
            }
            CheckAndSetScriptLibEtAl();
#else
            ExtractScriptBundle();
#endif
        }

        /// <summary>
        /// This should be only done for Standalone apps.
        /// </summary>
        public void ExtractScriptBundle() {
#if UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_WEBGL
            DeleteEverythingInPathWithIgnoredSubDirectories(ScriptEngine.WorkingDir);

            Extract(_scriptsBundleZip.bytes);
            Debug.Log($"Scripts Bundle extracted. ({ScriptEngine.WorkingDir})");
#endif
        }

        /// <summary>
        /// WARNING: This will replace the existing ScriptLib folder with the default one
        /// </summary>
        public void ExtractScriptLib() {
            var scriptLibPath = Path.Combine(ScriptEngine.WorkingDir, "ScriptLib");
            var dotGitPath = Path.Combine(scriptLibPath, ".git");
            if (Directory.Exists(dotGitPath)) {
                Debug.Log($".git folder detected in ScriptLib, aborting extraction.");
                return;
            }
            DeleteEverythingInPath(scriptLibPath);

            Extract(_scriptLibZip.bytes);
            Debug.Log($"ScriptLib Zip extracted. ({scriptLibPath})");
        }

        /// <summary>
        /// WARNING: This will replace the existing Samples folder with the default one
        /// </summary>
        public void ExtractSamples() {
            var samplesPath = Path.Combine(ScriptEngine.WorkingDir, "Samples");
            var dotGitPath = Path.Combine(samplesPath, ".git");
            if (Directory.Exists(dotGitPath)) {
                Debug.Log($".git folder detected in Samples, aborting extraction.");
                return;
            }
            DeleteEverythingInPath(samplesPath);

            Extract(_samplesZip.bytes);
            Debug.Log($"Samples Zip extracted. ({samplesPath})");
        }

        /// <summary>
        /// Root folder at path still remains
        /// </summary>
        void DeleteEverythingInPath(string path) {
            if (Directory.Exists(path)) {
                var di = new DirectoryInfo(path);
                foreach (FileInfo file in di.EnumerateFiles()) {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.EnumerateDirectories()) {
                    dir.Delete(true);
                }
            }
        }

        /// <summary>
        /// Root folder at path still remains
        /// </summary>
        void DeleteEverythingInPathWithIgnoredSubDirectories(string path) {
            if (Directory.Exists(path)) {
                var di = new DirectoryInfo(path);
                foreach (FileInfo file in di.EnumerateFiles()) {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.EnumerateDirectories()) {
                    if (_subDirectoriesToIgnore.ToList().Contains(dir.Name)) {
                        continue;
                    }
                    dir.Delete(true);
                }
            }
        }

        void Extract(byte[] bytes) {
            Stream inStream = new MemoryStream(bytes);
            Stream gzipStream = new GZipInputStream(inStream);

            TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream);
            tarArchive.ExtractContents(ScriptEngine.WorkingDir);
            tarArchive.Close();
            gzipStream.Close();
            inStream.Close();
        }

        void CheckAndSetScriptLibEtAl() {
#if UNITY_EDITOR
            var indexjsPath = Path.Combine(ScriptEngine.WorkingDir, "index.js");
            var scriptLibPath = Path.Combine(ScriptEngine.WorkingDir, "ScriptLib");
            var samplesPath = Path.Combine(ScriptEngine.WorkingDir, "Samples");
            var tsconfigPath = Path.Combine(ScriptEngine.WorkingDir, "tsconfig.json");
            var gitignorePath = Path.Combine(ScriptEngine.WorkingDir, ".gitignore");
            var vscodeSettingsPath = Path.Combine(ScriptEngine.WorkingDir, ".vscode/settings.json");

            var indexjsFound = File.Exists(indexjsPath);
            var scriptLibFound = Directory.Exists(scriptLibPath);
            var samplesFound = Directory.Exists(samplesPath);
            var tsconfigFound = File.Exists(tsconfigPath);
            var gitignoreFound = File.Exists(gitignorePath);
            var vscodeSettingsFound = File.Exists(vscodeSettingsPath);

            if (!indexjsFound) {
                File.WriteAllText(indexjsPath, "log(\"[index.js]: OneJS is good to go.\")");
                Debug.Log("index.js wasn't found. So a default one was created.");
            }

            if (!scriptLibFound) {
                Extract(_scriptLibZip.bytes);
                Debug.Log("ScriptLib Folder wasn't found. So a default one was created (from ScriptLib Zip).");
            }

            if (!samplesFound) {
                Extract(_samplesZip.bytes);
                Debug.Log("Samples Folder Extracted.");
            }

            if (!tsconfigFound) {
                File.WriteAllText(tsconfigPath, _tsconfig.text);
                Debug.Log("tsconfig.json wasn't found. So a default one was created.");
            }

            if (!gitignoreFound) {
                File.WriteAllText(gitignorePath, "ScriptLib");
            }

            if (!vscodeSettingsFound) {
                var dirPath = Path.Combine(ScriptEngine.WorkingDir, ".vscode");
                if (!Directory.Exists(dirPath)) {
                    Directory.CreateDirectory(dirPath);
                }
                File.WriteAllText(vscodeSettingsPath, _vscodeSettings.text);
                Debug.Log(".vscode/settings.json wasn't found. So a default one was created.");
            }
#endif
        }

#if UNITY_EDITOR

        [Button()]
        void OpenWorkingDir() {
            Process.Start(ScriptEngine.WorkingDir);
        }

        [ContextMenu("Extract ScriptLib")]
        void ExtractScriptLibFolder() {
            if (!UnityEditor.EditorUtility.DisplayDialog("Are you sure?",
                    "WARNING! This will overwrite the ScriptLib folder under {WorkingDir}.\n\n" +
                    "Consider backing up the existing ScriptLib folder if you need to keep any changes.",
                    "Confirm", "Cancel"))
                return;

            ExtractScriptLib();
        }

        [ContextMenu("Extract Samples")]
        void ExtractSamplesFolder() {
            if (!UnityEditor.EditorUtility.DisplayDialog("Are you sure?",
                    "WARNING! This will overwrite the Samples folder under WorkingDir.\n\n" +
                    "Consider backing up the existing Samples folder if you need to keep any changes.",
                    "Confirm", "Cancel"))
                return;

            ExtractSamples();
        }

#endif
    }
}