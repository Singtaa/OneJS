using System.IO;
using UnityEditor;

namespace OneJS.Editor {
    /// <summary>
    /// Where the OneJS package is, for editor code that reads files it ships.
    /// </summary>
    public static class OneJSPackage {
        /// <summary>
        /// The OneJS package root for every install shape: Package Manager
        /// (Packages/ or the package cache), a git clone into any folder under
        /// Assets, and the Asset Store package. Absolute for a Package Manager
        /// install, relative to the project for one under Assets.
        ///
        /// The one lookup. A copy of it is how two of them quietly start
        /// disagreeing: the shader generator kept its own, two fixed folders,
        /// and a clone into Assets/OneJS, the one the README gives, generated
        /// no shader at all.
        /// </summary>
        public static string Root() {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OneJSPackage).Assembly);

            if (packageInfo != null && Directory.Exists(packageInfo.resolvedPath))
                return packageInfo.resolvedPath;

            // Installed under Assets. Locate this script, then walk up out of Editor/.
            foreach (var guid in AssetDatabase.FindAssets($"{nameof(OneJSPackage)} t:MonoScript")) {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(assetPath) != $"{nameof(OneJSPackage)}.cs") continue;

                var editorDir = Path.GetDirectoryName(assetPath);
                var packageRoot = Path.GetDirectoryName(editorDir);
                if (!string.IsNullOrEmpty(packageRoot)) return packageRoot;
            }

            return null;
        }
    }
}
