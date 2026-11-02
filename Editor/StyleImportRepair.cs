using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    /// <summary>
    /// Reimports, once per project, the stylesheets and UXML files whose import failed while OneJS
    /// shadowed the editor's own ExCSS (issue #154).
    ///
    /// Up to 3.9.14 OneJS bundled a build of Unity's CSS parser under Unity's own assembly name,
    /// <c>ExCSS.Unity</c>, and the editor loaded it in place of its own. From Unity 6.6 the editor's
    /// importer needs a newer build, so every .uss, .tss and .uxml it imported with OneJS installed
    /// failed. OneJS's parser is now its own assembly (<c>Runtime/Styling/ExCSS</c>), but a failed
    /// import stays failed until something imports the file again: the sheet stays empty and the
    /// UXML stays a plain file. This finds those and imports them again, so updating OneJS is the
    /// whole fix. Only failed ones are touched, and only once: a marker in Library records that it
    /// ran, so a sheet that fails for its own reasons is not reimported on every load.
    /// </summary>
    class StyleImportRepair : AssetPostprocessor {
        internal const string Marker = "Library/OneJS/StyleImportRepair-154";

        // After the first import following a domain reload: the editor's own ExCSS is loaded by then, and this
        // runs in a batch-mode editor too (a build or a test run), which never gets as far as a delayCall.
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) {
            if (didDomainReload && !File.Exists(Marker)) Run();
        }

        /// <summary>The failed imports it found and imported again; empty when it had already run.</summary>
        internal static List<string> Run() {
            if (File.Exists(Marker) || OldParserLoaded()) return new List<string>();
            var failed = FailedImports();
            if (failed.Count > 0) {
                AssetDatabase.StartAssetEditing();
                try {
                    foreach (var path in failed) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                } finally {
                    AssetDatabase.StopAssetEditing();
                }
                Debug.Log($"[OneJS] Imported {failed.Count} UI Toolkit file{(failed.Count == 1 ? "" : "s")} again that failed while OneJS shadowed Unity's own ExCSS (OneJS issue 154): {string.Join(", ", failed.Take(10))}{(failed.Count > 10 ? ", ..." : "")}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Marker));
            File.WriteAllText(Marker, $"Ran {DateTime.UtcNow:u}, {failed.Count} reimported.\n");
            return failed;
        }

        /// <summary>
        /// Whether this domain still holds an ExCSS.Unity from outside the editor, as it can just after the
        /// update, before a reload swaps the old assembly out. Reimporting then would fail again, so it waits.
        /// </summary>
        static bool OldParserLoaded() {
            var editor = Path.GetFullPath(EditorApplication.applicationContentsPath);
            return AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "ExCSS.Unity" && !a.IsDynamic
                && !Path.GetFullPath(a.Location).StartsWith(editor, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Every .uss and .tss that imported with errors, and every .uxml that did not import as a visual tree.</summary>
        internal static List<string> FailedImports() {
            var failed = new List<string>();
            foreach (var path in AssetDatabase.GetAllAssetPaths()) {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".uss" || ext == ".tss") {
                    var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    if (sheet == null || sheet.importedWithErrors) failed.Add(path);
                } else if (ext == ".uxml") {
                    if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(VisualTreeAsset)) failed.Add(path);
                }
            }
            return failed;
        }
    }
}
