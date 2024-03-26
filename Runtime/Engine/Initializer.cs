using System;
using System.IO;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using OneJS.Utils;
using UnityEngine;

namespace OneJS {
    [DefaultExecutionOrder(-50)]
    public class Initializer : MonoBehaviour {
        public TextAsset defaultTsconfig;
        public TextAsset defaultEsbuild;
        public TextAsset defaultIndex;
        public TextAsset tailwindConfig;
        public TextAsset postcssConfig;
        public TextAsset readme;
        public TextAsset onejsCoreZip;
        public TextAsset outputsZip;

        ScriptEngine _engine;

        void Awake() {
            _engine = GetComponent<ScriptEngine>();
            CreateTsconfigFileIfNotFound();
            CreateReadMeFileIfNotFound();
            CreateEsbuildFileIfNotFound();
            CreateTailwindConfigFileIfNotFound();
            CreatePostcssConfigFileIfNotFound();
            CreateIndexFileIfNotFound();

            ExtractOnejsCoreIfNotFound();
            ExtractOutputsIfNotFound();
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

        void Extract(byte[] bytes) {
            Stream inStream = new MemoryStream(bytes);
            Stream gzipStream = new GZipInputStream(inStream);

            TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream);
            tarArchive.ExtractContents(_engine.WorkingDir);
            tarArchive.Close();
            gzipStream.Close();
            inStream.Close();
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
                binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                    binPath));
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
        void PackageOutputsZip() {
            _engine = GetComponent<ScriptEngine>();
            var t = DateTime.Now;
            var path = Path.Combine(_engine.WorkingDir, "@outputs");

            if (outputsZip == null) {
                UnityEditor.EditorUtility.DisplayDialog("outputs.tgz is null",
                    "Please make sure you have an outputs.tgz (Text Asset) set", "Okay");
                return;
            }
            if (UnityEditor.EditorUtility.DisplayDialog("Are you sure?",
                    "This will package up your outputs folder under ScriptEngine.WorkingDir into a tgz file " +
                    "and override your existing outputs.tgz file.",
                    "Confirm", "Cancel")) {
                var binPath = UnityEditor.AssetDatabase.GetAssetPath(outputsZip);
                binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                    binPath));
                var outStream = File.Create(binPath);
                var gzoStream = new GZipOutputStream(outStream);
                gzoStream.SetLevel(3);
                var tarOutputStream = new TarOutputStream(gzoStream);
                var tarCreator = new TarCreator(path, _engine.WorkingDir) { };
                tarCreator.CreateTar(tarOutputStream);

                Debug.Log($"outputs.tgz.bytes file updated. {tarOutputStream.Length} bytes {(DateTime.Now - t).TotalMilliseconds}ms");
                tarOutputStream.Close();
            }
        }

#endif
    }
}