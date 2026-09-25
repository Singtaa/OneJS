using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
using UnityEngine;

namespace OneJS.Editor {
    /// <summary>
    /// Hands the linker OneJS's <c>Plugins/link.xml</c> when OneJS is installed
    /// as a package. Unity reads a link.xml only from under Assets/, so from a
    /// Package Manager install the file never reached the linker, and IL2CPP
    /// stripped the UI Toolkit internals OneJS reaches by reflection: a WebGL
    /// build's JSRunner then failed at start with a TypeLoadException naming
    /// StyleSheetBuilder. Installed under Assets/, Unity reads the file itself
    /// and this adds nothing.
    /// </summary>
    public class OneJSLinkXmlProcessor : IUnityLinkerProcessor {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data) {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OneJSLinkXmlProcessor).Assembly);
            if (package == null) return null;
            var path = Path.Combine(package.resolvedPath, "Plugins", "link.xml");
            if (File.Exists(path)) return path;
            Debug.LogWarning($"[OneJS] {path} is missing, so a player build may strip types OneJS needs.");
            return null;
        }
    }
}
