using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using OneJS.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS {
    /// <summary>
    /// Sets up OneJS for first time use. It creates essential files in the WorkingDir if they are missing.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(ScriptEngine))]
    public class Initializer : MonoBehaviour {
        public TextAsset defaultGitIgnore;
        public TextAsset defaultTsconfig;
        public TextAsset defaultEsbuild;
        public TextAsset defaultIndex;
        public TextAsset defaultAppDts;
        public TextAsset tailwindConfig;
        public TextAsset postcssConfig;
        public TextAsset readme;
        public TextAsset onejsCoreZip;

        [Tooltip("The packaged @outputs folder")]
        public TextAsset outputsZip;

        [Tooltip("For deployment, outputs.tgz will be extracted based on a version string. So it will override any existing @outputs folder if there's a version mismatch.")]
        public string version = "1.0";
        [Tooltip("Force extracting outputs.tgz on every game start, irregardless of version.")]
        public bool forceExtract;
        [Tooltip("Files and folders that you don't want to be bundled into outputs.tgz")] [PlainString]
        public string[] ignoreList = new string[] { "tsc", "editor" };

        ScriptEngine _engine;
        string _onejsVersion = "2.0.2";

        void Awake() {
            _engine = GetComponent<ScriptEngine>();
            var versionString = PlayerPrefs.GetString("OneJSVersion", "0.0.0");
#if !UNITY_EDITOR && (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID)
            ExtractOutputsForStandalone();
#else
            if (versionString != _onejsVersion) {
                DeleteEverythingInPath(Path.Combine(_engine.WorkingDir, "onejs-core"));
                // if (_extractSamples)
                //     ExtractSamples();

                PlayerPrefs.SetString("OneJSVersion", _onejsVersion);
            }
            CreateGitIgnoreFileIfNotFound();
            CreateTsconfigFileIfNotFound();
            CreateEsbuildFileIfNotFound();
            CreateIndexFileIfNotFound();
            CreateAppDtsFileIfNotFound();
            CreateTailwindConfigFileIfNotFound();
            CreatePostcssConfigFileIfNotFound();
            CreateReadMeFileIfNotFound();

            ExtractOnejsCoreIfNotFound();
            ExtractOutputsIfNotFound();
#endif
        }

        public void CreateGitIgnoreFileIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, ".gitignore");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, defaultGitIgnore.text);
            Debug.Log($"'.gitignore' wasn't found. A new one was created ({path})");
        }

        public void CreateTsconfigFileIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, "tsconfig.json");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, defaultTsconfig.text);
            Debug.Log($"'tsconfig.json' wasn't found. A new one was created ({path})");
        }

        public void CreateEsbuildFileIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, "esbuild.mjs");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, defaultEsbuild.text);
            Debug.Log($"'esbuild.mjs' wasn't found. A new one was created ({path})");
        }

        public void CreateTailwindConfigFileIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, "tailwind.config.js");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, tailwindConfig.text);
            Debug.Log($"'tailwind.config.js' wasn't found. A new one was created ({path})");
        }

        public void CreatePostcssConfigFileIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, "postcss.config.js");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, postcssConfig.text);
            Debug.Log($"'postcss.config.js' wasn't found. A new one was created ({path})");
        }

        public void CreateIndexFileIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, "index.tsx");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, defaultIndex.text);
            Debug.Log($"'index.tsx' wasn't found. A new one was created ({path})");
        }
        
        public void CreateAppDtsFileIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, "app.d.ts");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, defaultAppDts.text);
            Debug.Log($"'app.d.ts' wasn't found. A new one was created ({path})");
        }

        public void CreateReadMeFileIfNotFound() {
            var path = Path.Combine(_engine.WorkingDir, "README.md");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, readme.text);
            Debug.Log($"'README.md' wasn't found. A new one was created ({path})");
        }

        public void ExtractOnejsCoreIfNotFound() {
            _engine = GetComponent<ScriptEngine>();
            var path = Path.Combine(_engine.WorkingDir, "onejs-core");
            if (Directory.Exists(path))
                return;

            Extract(onejsCoreZip.bytes);
            Debug.Log($"An existing 'onejs-core' directory wasn't found. A new one was created ({path})");
        }

        public void ExtractOutputsIfNotFound() {
            _engine = GetComponent<ScriptEngine>();
            var path = Path.Combine(_engine.WorkingDir, "@outputs");
            if (Directory.Exists(path))
                return;

            Extract(outputsZip.bytes);
            Debug.Log($"An existing 'outputs' directory wasn't found. An example one was created ({path})");
        }

        public void ExtractOutputsForStandalone() {
            var outputsVersion = PlayerPrefs.GetString("OutputsVersion", "0.0");
            var path = Path.Combine(_engine.WorkingDir, "@outputs");
            if (forceExtract || outputsVersion != version) {
                if (Directory.Exists(path))
                    DeleteEverythingInPath(path);
                Extract(outputsZip.bytes);
                PlayerPrefs.SetString("OutputsVersion", version);
                Debug.Log($"outputs.tgz was extracted. Version: {version}");
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

        [ContextMenu("Package onejs-core.tgz")]
        void PackageOnejsCoreZip() {
            _engine = GetComponent<ScriptEngine>();
            var t = DateTime.Now;
            var path = Path.Combine(_engine.WorkingDir, "onejs-core");

            if (onejsCoreZip == null) {
                UnityEditor.EditorUtility.DisplayDialog("onejs-core.tgz is null",
                    "Please make sure you have a onejs-core.tgz (Text Asset) set", "Okay");
                return;
            }
            if (UnityEditor.EditorUtility.DisplayDialog("Are you sure?",
                    "This will package up your onejs-core folder under ScriptEngine.WorkingDir into a tgz file " +
                    "and override your existing onejs-core.tgz file.",
                    "Confirm", "Cancel")) {
                var binPath = UnityEditor.AssetDatabase.GetAssetPath(onejsCoreZip);
                binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar, binPath));
                var outStream = File.Create(binPath);
                var gzoStream = new GZipOutputStream(outStream);
                gzoStream.SetLevel(3);
                var tarOutputStream = new TarOutputStream(gzoStream);
                var tarCreator = new TarCreator(path, _engine.WorkingDir) { };
                tarCreator.CreateTar(tarOutputStream);

                Debug.Log($"onejs-core.tgz.bytes file updated. {tarOutputStream.Length} bytes {(DateTime.Now - t).TotalMilliseconds}ms");
                tarOutputStream.Close();
            }
        }

        [ContextMenu("Package outputs.tgz")]
        public void PackageOutputsZipWithPrompt() {
            if (outputsZip == null) {
                UnityEditor.EditorUtility.DisplayDialog("outputs.tgz is null",
                    "Please make sure you have an outputs.tgz (Text Asset) set", "Okay");
                return;
            }
            if (UnityEditor.EditorUtility.DisplayDialog("Are you sure?",
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
            var binPath = UnityEditor.AssetDatabase.GetAssetPath(outputsZip);
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
        public void ZeroOutOutputsZip() {
            var binPath = UnityEditor.AssetDatabase.GetAssetPath(outputsZip);
            binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                binPath));
            var outStream = File.Create(binPath);
            outStream.Close();
        }

#endif
    }
}