using System;
using System.IO;
using DotNet.Globbing;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using NaughtyAttributes;
using OneJS.Utils;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OneJS.Editor {
    [CreateAssetMenu(fileName = "OneJSBuildProcessor", menuName = "OneJS/BuildProcessor", order = 1)]
    public class OneJSBuildProcessor : ScriptableObject, IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        [Tooltip("Files and folders that you don't want to be bundled with your standalone app build." +
                 "")]
        [ResizableTextArea] [SerializeField] string _ignoreList = ".vscode\ntsconfig.json\ntailwind.config.js\nnode_modules\nSamples";
        
        [SerializeField] bool _uglify = true;

        [Tooltip("This is the zip file of your bundled scripts. Use the `Package Scripts` button below to package " +
                 "everything up in your `{ProjectDir}/OneJS` folder. This step is automatically done for you during Building.")]
        [SerializeField]
        TextAsset _scriptsBundleZip;

        public void OnPreprocessBuild(BuildReport report) {
            Debug.Log("OneJSBuildProcessor: Bundling Scripts...");
            PackageScripts();
        }

        [Button()]
        void PackageScripts() {
            var binPath = UnityEditor.AssetDatabase.GetAssetPath(_scriptsBundleZip);
            binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                binPath));
            var outStream = File.Create(binPath);
            var gzoStream = new GZipOutputStream(outStream);
            gzoStream.SetLevel(3);
            var tarOutputStream = new TarOutputStream(gzoStream);
            var tarCreator = new TarCreator(ScriptEngine.WorkingDir)
                { ExcludeTS = true, UglifyJS = _uglify, IgnoreList = _ignoreList, IncludeRoot = false };
            tarCreator.CreateTar(tarOutputStream);
            tarOutputStream.Close();
            Debug.Log("Scripts Bundled Up.");
        }
        
        [Button()]
        void ZeroOutScriptsBundleZip() {
            var binPath = UnityEditor.AssetDatabase.GetAssetPath(_scriptsBundleZip);
            binPath = Path.GetFullPath(Path.Combine(Application.dataPath, @".." + Path.DirectorySeparatorChar,
                binPath));
            var outStream = File.Create(binPath);
            outStream.Close();
        }
        
    }
}