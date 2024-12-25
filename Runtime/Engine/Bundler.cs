using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using Newtonsoft.Json;
using OneJS.Utils;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS {
    /// <summary>
    /// Sets up OneJS for first time use. It creates essential files in the WorkingDir if they are missing.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(ScriptEngine))] [AddComponentMenu("OneJS/Bundler")]
    public class Bundler : MonoBehaviour {
        [PairMapping("path", "textAsset", ":")]
        public DefaultFileMapping[] defaultFiles;

        [Tooltip("Directories to package for production. Must be names for root directories under WorkingDir.")] [PlainString]
        public string[] directoriesToPackage = new string[] { "assets" };
        // [Tooltip("The packaged onejs-core folder (as a tarball).")]
        // public TextAsset onejsCoreZip;
        [Tooltip("The packaged @outputs folder (as a tarball).")]
        public TextAsset outputsZip;

        [Tooltip("Deployment version for the Standalone Player. The @outputs folder and all packaged directories will be overridden if there's a version mismatch or \"Force Extract\" is on.")]
        public string version = "1.0";
        [Tooltip("Force extract on every game start, irregardless of version.")]
        public bool forceExtract;
        [Tooltip("Files and folders that you don't want to be packaged. Can use glob patterns.")] [PlainString]
        public string[] ignoreList = new string[] { "tsc", "editor" };

        ScriptEngine _engine;
        string _onejsVersion = "2.1.8";

        void Awake() {
            _engine = GetComponent<ScriptEngine>();
            var versionString = PlayerPrefs.GetString("ONEJS_VERSION", "0.0.0");
#if !UNITY_EDITOR && (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID)
            ExtractForStandalone();
#else
            if (versionString != _onejsVersion) {
                // DeleteEverythingInPath(Path.Combine(_engine.WorkingDir, "onejs-core"));
                // if (_extractSamples)
                //     ExtractSamples();

                PlayerPrefs.SetString("ONEJS_VERSION", _onejsVersion);
            }

            foreach (var mapping in defaultFiles) {
                CreateIfNotFound(mapping);
            }
            CreateVSCodeSettingsJsonIfNotFound();

            // WriteToPackageJson();
            // ExtractOnejsCoreIfNotFound();
            ExtractOutputsIfNotFound();
#endif
        }

        void CreateIfNotFound(DefaultFileMapping mapping) {
            var path = Path.Combine(_engine.WorkingDir, mapping.path);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(path)) {
                File.WriteAllText(path, mapping.textAsset.text);
                Debug.Log($"'{mapping.path}' wasn't found. A new one was created.");
            }
        }

        /// <summary>
        /// Not used anymore because of VCS issues when different users have different paths.
        /// </summary>
        void WriteToPackageJson() {
            string scriptPath = new StackTrace(true).GetFrame(0).GetFileName();
            var onejsPath = ParentFolder(ParentFolder(ParentFolder(scriptPath)));
            // var escapedOnejsPath = onejsPath.Replace(@"\", @"\\").Replace("\"", "\\\"");
            var packageJsonPath = Path.Combine(_engine.WorkingDir, "package.json");
            string jsonString = File.ReadAllText(packageJsonPath);
            Dictionary<string, object> jsonDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);

            jsonDict["onejs"] = new Dictionary<string, object> {
                { "unity-package-path", onejsPath }
            };

            string updatedJsonString = JsonConvert.SerializeObject(jsonDict, Formatting.Indented);
            File.WriteAllText(packageJsonPath, updatedJsonString);
        }

        void CreateVSCodeSettingsJsonIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, ".vscode/settings.json");
            // Create if path doesn't exist
            if (!File.Exists(path)) {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var jsonDict = JsonConvert.DeserializeObject<Dictionary<string, object>>("{}");

                jsonDict["window.title"] = Application.productName;

                string updatedJsonString = JsonConvert.SerializeObject(jsonDict, Formatting.Indented);
                File.WriteAllText(path, updatedJsonString);
            }
        }

        string ParentFolder(string path) {
            return Directory.GetParent(path)?.FullName;
        }

        // public void ExtractOnejsCoreIfNotFound() {
        //     _engine = GetComponent<ScriptEngine>();
        //     var path = Path.Combine(_engine.WorkingDir, "onejs-core");
        //     if (Directory.Exists(path))
        //         return;
        //
        //     Extract(onejsCoreZip.bytes);
        //     Debug.Log($"An existing 'onejs-core' directory wasn't found. A new one was created ({path})");
        // }

        public void ExtractOutputsIfNotFound() {
            _engine = GetComponent<ScriptEngine>();
            var path = Path.Combine(_engine.WorkingDir, "@outputs");
            if (Directory.Exists(path))
                return;

            Extract(outputsZip.bytes);
            Debug.Log($"An existing 'outputs' directory wasn't found. An example one was created ({path})");
        }

        public void ExtractForStandalone() {
            var deployVersion = PlayerPrefs.GetString("ONEJS_APP_DEPLOYMENT_VERSION", "0.0");
            var outputPath = Path.Combine(_engine.WorkingDir, "@outputs");
            if (forceExtract || deployVersion != version) {
                Debug.Log($"Extracting for Standalone Player. Deployment Version: {version}");
                if (Directory.Exists(outputPath))
                    DeleteEverythingInPath(outputPath);
                Extract(outputsZip.bytes);
                Debug.Log($"@outputs folder extracted.");

                foreach (var dir in directoriesToPackage) {
                    var dirname = StringUtil.SanitizeFilename(dir);
                    var dirPath = Path.Combine(_engine.WorkingDir, dirname);
                    var tgz = Resources.Load<TextAsset>($"OneJS/Tarballs/{dirname}.tgz");
                    if (tgz == null)
                        continue;
                    if (Directory.Exists(dirPath))
                        DeleteEverythingInPath(dirPath);
                    Extract(tgz.bytes);
                    Debug.Log($"{dirname} directory extracted.");
                }

                PlayerPrefs.SetString("ONEJS_APP_DEPLOYMENT_VERSION", version);
            }
        }

        void Extract(byte[] bytes) {
            Stream inStream = new MemoryStream(bytes);
            Stream gzipStream = new GZipInputStream(inStream);

            TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream);
            tarArchive.ExtractContents(_engine.WorkingDir);
            tarArchive.Close();
            gzipStream.Close();
            inStream.Close();
        }

        /// <summary>
        /// Root folder at path still remains
        /// </summary>
        void DeleteEverythingInPath(string path) {
            var dotGitPath = Path.Combine(path, ".git");
            if (Directory.Exists(dotGitPath)) {
                Debug.Log($".git folder detected at {path}, aborting extraction.");
                return;
            }
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

#if UNITY_EDITOR

        // [ContextMenu("Package onejs-core.tgz")]
        // void PackageOnejsCoreZip() {
        //     _engine = GetComponent<ScriptEngine>();
        //     var t = DateTime.Now;
        //     var path = Path.Combine(_engine.WorkingDir, "onejs-core");
        //
        //     if (onejsCoreZip == null) {
        //         EditorUtility.DisplayDialog("onejs-core.tgz is null",
        //             "Please make sure you have a onejs-core.tgz (Text Asset) set", "Okay");
        //         return;
        //     }
        //     if (EditorUtility.DisplayDialog("Are you sure?",
        //             "This will package up your onejs-core folder under ScriptEngine.WorkingDir into a tgz file " +
        //             "and override your existing onejs-core.tgz file.",
        //             "Confirm", "Cancel")) {
        //         var binPath = AssetDatabase.GetAssetPath(onejsCoreZip);
        //         binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar, binPath));
        //         var outStream = File.Create(binPath);
        //         var gzoStream = new GZipOutputStream(outStream);
        //         gzoStream.SetLevel(3);
        //         var tarOutputStream = new TarOutputStream(gzoStream);
        //         var tarCreator = new TarCreator(path, _engine.WorkingDir) {
        //             IgnoreList = new[] { "**/node_modules", "**/.prettierrc", "**/jsr.json", "**/package.json", "**/tsconfig.json", "**/README.md" }
        //         };
        //         tarCreator.CreateTar(tarOutputStream);
        //
        //         Debug.Log($"onejs-core.tgz.bytes file updated. {tarOutputStream.Length} bytes {(DateTime.Now - t).TotalMilliseconds}ms");
        //         tarOutputStream.Close();
        //     }
        // }

        [ContextMenu("Package outputs.tgz")]
        public void PackageOutputsZipWithPrompt() {
            if (outputsZip == null) {
                EditorUtility.DisplayDialog("outputs.tgz is null",
                    "Please make sure you have an outputs.tgz (Text Asset) set", "Okay");
                return;
            }
            if (EditorUtility.DisplayDialog("Are you sure?",
                    "This will package up your outputs folder under ScriptEngine.WorkingDir into a tgz file " +
                    "and override your existing outputs.tgz file.",
                    "Confirm", "Cancel")) {
                PackageOutputsZip();
            }
        }

        public void PackageOutputsZip() {
            _engine = GetComponent<ScriptEngine>();
            var t = DateTime.Now;
            var path = Path.Combine(_engine.WorkingDir, "@outputs");
            var binPath = AssetDatabase.GetAssetPath(outputsZip);
            binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                binPath));
            var outStream = File.Create(binPath);
            var gzoStream = new GZipOutputStream(outStream);
            gzoStream.SetLevel(3);
            var tarOutputStream = new TarOutputStream(gzoStream);
            var tarCreator = new TarCreator(path, _engine.WorkingDir) { IgnoreList = ignoreList };
            tarCreator.CreateTar(tarOutputStream);

            Debug.Log($"outputs.tgz.bytes file updated. {tarOutputStream.Length} bytes {(DateTime.Now - t).TotalMilliseconds}ms");
            tarOutputStream.Close();
        }

        [ContextMenu("Zero Out outputs.tgz")]
        public void ZeroOutOutputsZipWithPrompt() {
            if (outputsZip == null) {
                EditorUtility.DisplayDialog("outputs.tgz is null",
                    "Please make sure you have an outputs.tgz (Text Asset) set", "Okay");
                return;
            }
            if (EditorUtility.DisplayDialog("Are you sure?",
                    "This will zero out your outputs.tgz file. This is useful when you want to make a clean build.",
                    "Confirm", "Cancel")) {
                ZeroOutOutputsZip();
            }
        }

        public void ZeroOutOutputsZip() {
            var binPath = AssetDatabase.GetAssetPath(outputsZip);
            binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                binPath));
            var outStream = File.Create(binPath);
            outStream.Close();
        }

        [ContextMenu("Package Directories")]
        public void PackageDirectoriesWithPrompt() {
            if (EditorUtility.DisplayDialog("Are you sure?",
                    "This will package up the directories specified in the 'Directories to Package' field into tgz files in the Resources folder.",
                    "Confirm", "Cancel")) {
                PackageDirectories();
            }
        }

        /// <summary>
        /// Packages the specified directories as tarballs. These will be kept in the Resources folder.
        /// </summary>
        public void PackageDirectories() {
            _engine = GetComponent<ScriptEngine>();
            var t = DateTime.Now;
            foreach (var dir in directoriesToPackage) {
                var dirname = StringUtil.SanitizeFilename(dir);
                var dirPath = Path.Combine(_engine.WorkingDir, dirname);
                if (!Directory.Exists(dirPath)) {
                    Debug.Log($"{dirPath} does not exist. Skipping.");
                    continue;
                }
                var resourcesPath = Path.Combine(Application.dataPath, @"Resources/OneJS/Tarballs");
                if (!Directory.Exists(resourcesPath)) {
                    Directory.CreateDirectory(resourcesPath);
                }
                var binPath = Path.Combine(resourcesPath, $"{dirname}.tgz.bytes");
                var outStream = File.Create(binPath);
                var gzoStream = new GZipOutputStream(outStream);
                gzoStream.SetLevel(3);
                var tarOutputStream = new TarOutputStream(gzoStream);
                var tarCreator = new TarCreator(dirPath, _engine.WorkingDir);
                tarCreator.CreateTar(tarOutputStream);

                Debug.Log($"{dirname}.tgz.bytes file updated. {tarOutputStream.Length} bytes {(DateTime.Now - t).TotalMilliseconds}ms");
                tarOutputStream.Close();
            }
            AssetDatabase.Refresh();
        }

#endif
    }

    [Serializable]
    public class DefaultFileMapping {
        public string path;
        public TextAsset textAsset;
    }
}