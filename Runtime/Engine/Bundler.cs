using System.Diagnostics;
using System.IO;
using System.Linq;
using DotNet.Globbing;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using OneJS.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS {
    [DefaultExecutionOrder(50)]
    public class Bundler : MonoBehaviour {
        // [Tooltip(
        //     "These subdirectories (under WorkingDir) will be ignored during standalone app bundling/extraction. " +
        //     "One common use for them is user-provided addons. You want these directories to survive OneJS updates.")]
        // [SerializeField]
        // [PlainString]
        // string[] _subDirectoriesToIgnore = new[] { "Addons" };

        [Tooltip("This is the zip file of your bundled scripts.")]
        [SerializeField] TextAsset _scriptsBundleZip;

        [SerializeField]
        [Tooltip("The zip file that contains sample scripts. You normally don't need to touch this.")]
        TextAsset _samplesZip;

        [Tooltip("This is the zip file that OneJS uses to fill your " +
                 "ScriptLib folder if one isn't found under {ProjectDir}/OneJS. You normally don't need to touch this.")]
        [SerializeField]
        TextAsset _scriptLibZip;

        [Tooltip("Default vscode settings.json. If one isn't found under {ProjectDir}/OneJS/.vscode, " +
                 "this is the template that will be copied over. You normally don't need to touch this.")]
        [SerializeField]
        TextAsset _vscodeSettings;

        [Tooltip("Default tsconfig.json. If one isn't found under {ProjectDir}/OneJS, " +
                 "this is the template that will be copied over. You normally don't need to touch this.")]
        [SerializeField]
        TextAsset _tsconfig;

        [Tooltip("Files and folders that you don't want to be bundled with your standalone app build." +
                 "")]
        [PlainString]
        [SerializeField] string[] _ignoreList = new string[]
            { ".vscode", "tsconfig.json", "tailwind.config.js", "node_modules", "Samples", "Addons" };

        [Tooltip("Strip the TS files.")]
        [SerializeField] bool _excludeTS;

        [Tooltip("Uglify/Minify the bundled JS files.")]
        [SerializeField] bool _uglify;

        [Tooltip("Will automatically extract built-in Samples folder to your WorkingDir")]
        [SerializeField] bool _extractSamples = true;

        string _onejsVersion = "1.5.7";

        ScriptEngine _scriptEngine;

        void Awake() {
            _scriptEngine = GetComponent<ScriptEngine>();
            var versionString = PlayerPrefs.GetString("OneJSVersion", "0.0.0");
#if UNITY_EDITOR
            if (versionString != _onejsVersion) {
                ExtractScriptLib();
                if (_extractSamples)
                    ExtractSamples();
                // print($"ScriptLib and Samples extracted. This can happen when OneJS is updated (or when it's your first time running OneJS).");

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
            DeleteEverythingInPathWithIgnoredSubDirectories(_scriptEngine.WorkingDir);

            Extract(_scriptsBundleZip.bytes);
            // Debug.Log($"Scripts Bundle extracted. ({_scriptEngine.WorkingDir})");
#endif
        }

        /// <summary>
        /// WARNING: This will replace the existing ScriptLib folder with the default one
        /// </summary>
        public void ExtractScriptLib() {
            _scriptEngine = GetComponent<ScriptEngine>();
            var scriptLibPath = Path.Combine(_scriptEngine.WorkingDir, "ScriptLib");
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
            _scriptEngine = GetComponent<ScriptEngine>();
            var samplesPath = Path.Combine(_scriptEngine.WorkingDir, "Samples");
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
                    var relPath = Path.GetRelativePath(_scriptEngine.WorkingDir, dir.FullName);
                    if (IsIgnored(relPath)) {
                        continue;
                    }
                    dir.Delete(true);
                }
            }
        }

        bool IsIgnored(string path) {
            foreach (var pttrn in _ignoreList) {
                var glob = Glob.Parse(pttrn);
                var isMatch = glob.IsMatch(path);
                if (isMatch) {
                    return true;
                }
            }
            return false;
        }

        void Extract(byte[] bytes) {
            Stream inStream = new MemoryStream(bytes);
            Stream gzipStream = new GZipInputStream(inStream);

            TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream);
            tarArchive.ExtractContents(_scriptEngine.WorkingDir);
            tarArchive.Close();
            gzipStream.Close();
            inStream.Close();
        }

        void CheckAndSetScriptLibEtAl() {
            _scriptEngine = GetComponent<ScriptEngine>();
#if UNITY_EDITOR
            var indexjsPath = Path.Combine(_scriptEngine.WorkingDir, "index.js");
            var scriptLibPath = Path.Combine(_scriptEngine.WorkingDir, "ScriptLib");
            var samplesPath = Path.Combine(_scriptEngine.WorkingDir, "Samples");
            var tsconfigPath = Path.Combine(_scriptEngine.WorkingDir, "tsconfig.json");
            var gitignorePath = Path.Combine(_scriptEngine.WorkingDir, ".gitignore");
            var vscodeSettingsPath = Path.Combine(_scriptEngine.WorkingDir, ".vscode/settings.json");

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

            if (!samplesFound && _extractSamples) {
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
                var dirPath = Path.Combine(_scriptEngine.WorkingDir, ".vscode");
                if (!Directory.Exists(dirPath)) {
                    Directory.CreateDirectory(dirPath);
                }
                File.WriteAllText(vscodeSettingsPath, _vscodeSettings.text);
                Debug.Log(".vscode/settings.json wasn't found. So a default one was created.");
            }
#endif
        }

#if UNITY_EDITOR

        [ContextMenu("Package Scripts for build")]
        public void PackageScriptsForBuild() {
            CheckAndSetScriptLibEtAl();
            _scriptEngine = GetComponent<ScriptEngine>();
            var binPath = UnityEditor.AssetDatabase.GetAssetPath(_scriptsBundleZip);
            binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                binPath));
            var outStream = File.Create(binPath);
            var gzoStream = new GZipOutputStream(outStream);
            gzoStream.SetLevel(3);
            var tarOutputStream = new TarOutputStream(gzoStream);
            var tarCreator = new TarCreator(_scriptEngine.WorkingDir, _scriptEngine.WorkingDir) {
                ExcludeTS = _excludeTS, UglifyJS = _uglify, IgnoreList = _ignoreList, IncludeRoot = false
            };
            tarCreator.CreateTar(tarOutputStream);
            tarOutputStream.Close();
            Debug.Log($"[{gameObject.scene.name}][{gameObject.name}][Bundler] ScriptsBundle.zip built.");
        }

        [ContextMenu("Zero Out ScriptsBundleZip")]
        public void ZeroOutScriptsBundleZip() {
            var binPath = UnityEditor.AssetDatabase.GetAssetPath(_scriptsBundleZip);
            binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                binPath));
            var outStream = File.Create(binPath);
            outStream.Close();
        }

        public void OpenWorkingDir() {
            _scriptEngine = GetComponent<ScriptEngine>();
            Process.Start(_scriptEngine.WorkingDir);
        }

        [ContextMenu("Extract ScriptLib")]
        public void ExtractScriptLibFolder() {
            if (!UnityEditor.EditorUtility.DisplayDialog("Are you sure?",
                    "WARNING! This will overwrite the ScriptLib folder under {WorkingDir}.\n\n" +
                    "Consider backing up the existing ScriptLib folder if you need to keep any changes.",
                    "Confirm", "Cancel"))
                return;

            ExtractScriptLib();
        }

        [ContextMenu("Extract Samples")]
        public void ExtractSamplesFolder() {
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