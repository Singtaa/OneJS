using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OneJS;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneJS.Editor {
    /// <summary>
    /// Build processor that automatically creates TextAsset bundles for JSRunner components.
    /// Scans all enabled scenes in Build Settings and generates TextAssets for each JSRunner.
    ///
    /// Directory structure:
    ///   {SceneDir}/{SceneName}/{GameObjectName}/app.js.txt
    ///   {SceneDir}/{SceneName}/{GameObjectName}/app.js.txt.map (optional)
    /// </summary>
    public class JSRunnerBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
        public int callbackOrder => 0;

        static List<string> _createdAssets = new List<string>();
        static HashSet<string> _processedRunners = new HashSet<string>();
        static int _copiedAssetCount = 0;

        /// <summary>
        /// Every app's asset folder, in the order the build met them, gathered
        /// during the scene walk and committed once afterwards.
        ///
        /// Gathered rather than copied on the spot because the destination is
        /// shared: copying per runner meant the delete that cleared stale files
        /// ran per runner too, so each app erased the one before it and only the
        /// last app's assets reached the player. Committing once also means a
        /// collision is found BEFORE anything is deleted, so a failed build leaves
        /// the previous build's folder intact instead of a half written one.
        /// </summary>
        /// <summary>
        /// Runners met in the scene walk, however they turned out.
        ///
        /// Counted separately from _processedRunners, which only gains an entry
        /// when a bundle is actually created: a runner with a pre-assigned bundle
        /// never lands there, so gating on it meant a build where every runner had
        /// one and none shipped assets skipped the commit entirely, which is the
        /// exact case the commit exists to cover.
        /// </summary>
        static int _runnersSeen = 0;

        static List<(string srcDir, string runnerName)> _assetSources =
            new List<(string srcDir, string runnerName)>();

        public void OnPreprocessBuild(BuildReport report) {
            _createdAssets.Clear();
            _processedRunners.Clear();
            _copiedAssetCount = 0;
            _assetSources.Clear();
            _runnersSeen = 0;

            Debug.Log("[JSRunner] Processing JSRunner components in build scenes...");

            var originalScenePath = SceneManager.GetActiveScene().path;
            var buildScenes = EditorBuildSettings.scenes;

            // If no enabled scenes in build settings, process current scene
            if (buildScenes.Length == 0 || !Array.Exists(buildScenes, s => s.enabled)) {
                ProcessScene(SceneManager.GetActiveScene());
            } else {
                // Process each enabled scene
                foreach (var buildScene in buildScenes) {
                    if (!buildScene.enabled) continue;

                    var scene = EditorSceneManager.OpenScene(buildScene.path);
                    ProcessScene(scene);

                    if (scene.isDirty) {
                        EditorSceneManager.SaveScene(scene);
                    }
                }
            }

            // Every scene has been walked, so the full set of apps is known and
            // the shared asset folder can be resolved in one pass.
            CommitAssets(_runnersSeen > 0);

            // Refresh asset database to pick up new TextAssets
            if (_createdAssets.Count > 0) {
                AssetDatabase.Refresh();
            }

            // Restore original scene
            if (!string.IsNullOrWhiteSpace(originalScenePath)) {
                EditorSceneManager.OpenScene(originalScenePath);
            }

            var assetMsg = _copiedAssetCount > 0 ? $", copied {_copiedAssetCount} asset file(s) to StreamingAssets" : "";
            Debug.Log($"[JSRunner] Build preprocessing complete. Processed {_processedRunners.Count} runner(s), created {_createdAssets.Count} asset(s){assetMsg}.");
        }

        void ProcessScene(Scene scene) {
            foreach (var rootObj in scene.GetRootGameObjects()) {
                var runners = rootObj.GetComponentsInChildren<JSRunner>(true);
                foreach (var runner in runners) {
                    // Only process enabled runners on active GameObjects. Said out
                    // loud, because the result of skipping one is a build whose app
                    // has no bundle and no assets, which is indistinguishable at
                    // runtime from the app being broken.
                    if (!runner.enabled || !runner.gameObject.activeInHierarchy) {
                        Debug.Log($"[JSRunner] Skipped {runner.gameObject.name} in {scene.name}: " +
                            $"{(runner.enabled ? "its GameObject is inactive" : "the component is disabled")}. " +
                            $"No bundle or assets will be built for it.");
                        continue;
                    }

                    _runnersSeen++;
                    ProcessJSRunner(runner);
                    ExtractCartridges(runner);
                    CopyAssets(runner);
                }
            }
        }

        bool ProcessJSRunner(JSRunner runner) {
            // Skip if already has a bundle asset assigned
            if (runner.BundleAsset != null) {
                Debug.Log($"[JSRunner] Bundle already assigned for {runner.gameObject.name}");
                return false;
            }

            var entryFilePath = runner.EntryFileFullPath;
            var instanceFolder = runner.InstanceFolder;
            var bundleDir = !string.IsNullOrEmpty(instanceFolder) ? instanceFolder : Path.GetDirectoryName(entryFilePath);
            var instanceFolderAssetPath = runner.InstanceFolderAssetPath;

            // Normalize to forward slashes for AssetDatabase consistency
            if (instanceFolderAssetPath != null)
                instanceFolderAssetPath = instanceFolderAssetPath.Replace('\\', '/');

            var bundleAssetPathUnity = instanceFolderAssetPath != null ? instanceFolderAssetPath + "/app.js.txt" : null;
            var sourceMapAssetPathUnity = instanceFolderAssetPath != null ? instanceFolderAssetPath + "/app.js.map.txt" : null;

            if (string.IsNullOrEmpty(entryFilePath) || string.IsNullOrEmpty(bundleAssetPathUnity)) {
                Debug.LogWarning($"[JSRunner] Invalid paths for {runner.gameObject.name}. " +
                    $"InstanceFolderAssetPath={instanceFolderAssetPath ?? "null"}, " +
                    $"InstanceFolder={instanceFolder ?? "null"}, EntryFile={entryFilePath ?? "null"}");
                return false;
            }

            if (!File.Exists(entryFilePath)) {
                Debug.LogWarning($"[JSRunner] Entry file not found for {runner.gameObject.name}: {entryFilePath}");
                return false;
            }

            if (_processedRunners.Contains(bundleAssetPathUnity)) {
                // The FILE is written once per app, but the reference to it is
                // serialized on each COMPONENT, so this runner still needs its own
                // assignment. Returning here without one shipped it with a null
                // bundle, and a runner with a null bundle runs OneJS's default
                // placeholder app in the player: the app looks built and is not.
                //
                // Two runners land here whenever they resolve to one app folder,
                // which is keyed off the PanelSettings asset. The everyday way in
                // is one prefab dropped into several build scenes, since every
                // instance carries the same PanelSettings; assigning one
                // PanelSettings to two runners by hand does it too. The first
                // runner reached got a bundle and the rest did not, so which scene
                // worked depended on Build Settings order.
                Debug.Log($"[JSRunner] Bundle already written, assigning it to " +
                    $"{runner.gameObject.name}: {bundleAssetPathUnity}");
                return AssignBundle(runner, bundleAssetPathUnity, sourceMapAssetPathUnity);
            }
            _processedRunners.Add(bundleAssetPathUnity);

            if (!string.IsNullOrEmpty(bundleDir) && !Directory.Exists(bundleDir)) {
                Directory.CreateDirectory(bundleDir);
            }

            var bundleContent = File.ReadAllText(entryFilePath);
            var bundleFullPath = Path.Combine(bundleDir ?? "", "app.js.txt");
            File.WriteAllText(bundleFullPath, bundleContent);
            _createdAssets.Add(bundleFullPath);
            Debug.Log($"[JSRunner] Created bundle: {bundleAssetPathUnity}");

            if (runner.IncludeSourceMap) {
                var sourceMapFilePath = runner.SourceMapFilePath;
                if (!string.IsNullOrEmpty(sourceMapFilePath) && File.Exists(sourceMapFilePath)) {
                    var sourceMapContent = File.ReadAllText(sourceMapFilePath);
                    var sourceMapFullPath = Path.Combine(bundleDir ?? "", "app.js.map.txt");
                    File.WriteAllText(sourceMapFullPath, sourceMapContent);
                    _createdAssets.Add(sourceMapFullPath);
                    Debug.Log($"[JSRunner] Created source map: {sourceMapAssetPathUnity}");
                }
            }

            // Imported here, where the file was just written, rather than inside
            // AssignBundle: the import is per FILE and belongs with the write, and
            // re-running it per runner reloads the asset, handing each runner a
            // different managed wrapper for the same file.
            // ImportAsset rather than Refresh, which can be asynchronous on Windows.
            AssetDatabase.ImportAsset(bundleAssetPathUnity, ImportAssetOptions.ForceSynchronousImport);
            if (runner.IncludeSourceMap && !string.IsNullOrEmpty(sourceMapAssetPathUnity))
                AssetDatabase.ImportAsset(sourceMapAssetPathUnity, ImportAssetOptions.ForceSynchronousImport);

            return AssignBundle(runner, bundleAssetPathUnity, sourceMapAssetPathUnity);
        }

        /// <summary>
        /// Points one runner at the bundle TextAsset on disk.
        ///
        /// Split out from writing the file because the two do not happen the same
        /// number of times: the file is written once per app, the reference is
        /// serialized once per runner. Every path that reaches a runner with no
        /// bundle has to end here, including the one where another runner already
        /// wrote and imported the file.
        /// </summary>
        bool AssignBundle(JSRunner runner, string bundleAssetPathUnity, string sourceMapAssetPathUnity) {
            var bundleAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(bundleAssetPathUnity);
            if (bundleAsset == null) {
                // Fallback: try a full Refresh and retry once
                Debug.LogWarning($"[JSRunner] Bundle is not in the asset database, retrying with full Refresh: {bundleAssetPathUnity}");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                bundleAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(bundleAssetPathUnity);
            }

            if (bundleAsset == null) {
                Debug.LogError($"[JSRunner] Failed to load bundle asset after retry: {bundleAssetPathUnity}. " +
                    $"File exists on disk: {File.Exists(runner.EntryFileFullPath ?? "")}");
                return false;
            }

            runner.SetBundleAsset(bundleAsset);

            if (runner.IncludeSourceMap && !string.IsNullOrEmpty(sourceMapAssetPathUnity)) {
                var sourceMapAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(sourceMapAssetPathUnity);
                if (sourceMapAsset != null) {
                    runner.SetSourceMapAsset(sourceMapAsset);
                }
            }

            return true;
        }

        void ExtractCartridges(JSRunner runner) {
            var cartridges = runner.Cartridges;
            if (cartridges == null || cartridges.Count == 0) return;

            var created = CartridgeUtils.ExtractCartridges(
                runner.WorkingDirFullPath, cartridges, overwriteExisting: true, "[JSRunner]");

            foreach (var path in created) {
                _createdAssets.Add(path);
            }
        }

        /// <summary>Records an app's asset folder. The copying happens in CommitAssets.</summary>
        void CopyAssets(JSRunner runner) {
            var workingDir = runner.WorkingDirFullPath;
            if (string.IsNullOrEmpty(workingDir)) return;

            var assetsDir = Path.Combine(workingDir, "assets");
            if (!Directory.Exists(assetsDir)) return;

            // The same folder twice is one app reached twice, not two apps: two
            // JSRunners can share a project, and a runner can sit in more than one
            // build scene. Recording it once keeps it out of the collision check.
            foreach (var known in _assetSources) {
                if (PathsEqual(known.srcDir, assetsDir)) return;
            }
            _assetSources.Add((assetsDir, runner.gameObject.name));
        }

        /// <summary>
        /// Builds StreamingAssets/onejs/assets from every app in the build, once.
        ///
        /// Order matters and is the whole point: resolve what the folder should
        /// contain and reject collisions first, stage it beside the destination
        /// second, and only then replace the destination. Nothing is deleted until
        /// the new folder is complete on disk, so a collision or a failed copy
        /// leaves the previous contents where they were.
        ///
        /// Runs even when no app ships assets, as long as the build had runners:
        /// that is what removes files belonging to an app that has since been
        /// deleted, which a merge on its own would leave in every future build.
        ///
        /// StreamingAssets/onejs/assets belongs wholly to this processor and is
        /// rebuilt from the apps on every build, so anything hand placed there is
        /// removed. That is deliberate and is what the old code did too, one
        /// runner at a time. The alternative, keeping files it did not write,
        /// needs a manifest of what it wrote last time and still cannot tell a
        /// stale asset from a deliberate one. Hand placed files belong anywhere
        /// else under StreamingAssets, which this never touches.
        ///
        /// In practice what is found in there is the previous build's output
        /// rather than anything a person put there, which is the point: those
        /// leftovers are what used to ship in every later build.
        /// </summary>
        void CommitAssets(bool hadRunners) {
            if (!hadRunners) return;
            CommitAssetsTo(Path.Combine(Application.dataPath, "StreamingAssets", "onejs", "assets"));
        }

        /// <summary>
        /// CommitAssets against a given destination. Split out so the tests can
        /// drive the real resolve, collide, stage and swap against a temp folder
        /// rather than the project's own StreamingAssets.
        /// </summary>
        void CommitAssetsTo(string destDir) {

            // Cleared before the plan is built, not inside the try below, because
            // a collision throws while planning and would leave a stale staging
            // folder from an earlier failure untouched. It sits under Assets, so
            // Unity imports it and a player built before anything clears it would
            // carry a duplicate of those files.
            var staging = destDir + ".staging";
            try { DeleteTree(staging); } catch { }

            // relative path (lowercased) -> what to copy and who owns it.
            var plan = new Dictionary<string, (string file, string relative, string runner, string src)>();

            foreach (var (srcDir, runnerName) in _assetSources) {
                foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories)) {
                    if (file.EndsWith(".meta")) continue;

                    var relative = file.Substring(srcDir.Length)
                        .TrimStart(Path.DirectorySeparatorChar, '/')
                        .Replace('\\', '/');
                    // Case folded, because Windows and macOS both resolve
                    // Logo.png and logo.png to one file. Detecting this only on
                    // Linux would mean a build that passes CI and ships one app's
                    // texture under the other app's name on every developer machine.
                    // FormC before the fold: a composed and a decomposed accent
                    // are different keys but the same file on APFS, so two apps
                    // could pass on Windows and collide on a colleague's Mac.
                    var key = relative.Normalize(NormalizationForm.FormC).ToLowerInvariant();

                    if (plan.TryGetValue(key, out var owner)) {
                        // The same folder twice is one app reached twice, not two
                        // apps disagreeing. CopyAssets already skips a repeat, but
                        // this step does not rely on that: a caller that forgets
                        // would otherwise fail a perfectly good build.
                        if (PathsEqual(owner.src, srcDir)) continue;

                        // Identical bytes are refused too, deliberately. Sharing one
                        // copy happens to work today and silently stops working the
                        // day the two files diverge, and the build would then start
                        // failing somewhere unrelated to the change that broke it.
                        // The fix is the same either way: rename, or give each app
                        // a folder of its own.
                        var same = string.Equals(owner.relative, relative, StringComparison.Ordinal)
                            ? ""
                            : $" (as \"{owner.relative}\" and \"{relative}\", which differ only in case)";
                        throw new BuildFailedException(
                            $"[JSRunner] Asset path collision: \"{relative}\" is shipped by both " +
                            $"\"{owner.runner}\" and \"{runnerName}\"{same}. Every OneJS app in a build copies into " +
                            $"the same StreamingAssets/onejs/assets folder, which is the one place the runtime " +
                            $"resolves assets from, so two apps cannot ship the same relative path. Rename one of " +
                            $"them, or put each app's files under a folder of their own.");
                    }
                    plan[key] = (file, relative, runnerName, srcDir);
                }
            }

            try {
                Directory.CreateDirectory(staging);

                foreach (var entry in plan.Values) {
                    var destFile = Path.Combine(staging, entry.relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile));
                    File.Copy(entry.file, destFile, true);
                    // File.Copy keeps the read only attribute, and Windows will
                    // not delete a read only file, so a read only source asset
                    // (Perforce, a restored archive) would make the NEXT build's
                    // delete throw. Cleared here so the destination this build
                    // writes is always one the next build can replace.
                    File.SetAttributes(destFile, FileAttributes.Normal);
                }

                DeleteTree(destDir);
                Directory.CreateDirectory(Path.GetDirectoryName(destDir));
                Directory.Move(staging, destDir);
            } catch (Exception e) {
                // Catches everything, deliberately. IOException alone let an
                // UnauthorizedAccessException out of the cleanup REPLACE the
                // BuildFailedException below, and Unity does not abort for that,
                // so the build went green with the previous build's assets and a
                // staging folder left under Assets. The two are siblings, not
                // parent and child.
                try { DeleteTree(staging); } catch { }
                // Rethrown as BuildFailedException because Unity only ABORTS a
                // build for that type: anything else out of OnPreprocessBuild is
                // an error line and the build carries on, which here meant
                // shipping the previous build's assets under a green result. A
                // read only file in the destination (Perforce, a restored
                // archive, or a read only source asset, since File.Copy keeps the
                // attribute) is enough to reach this.
                if (e is BuildFailedException) throw;
                throw new BuildFailedException(
                    $"[JSRunner] Could not rebuild StreamingAssets/onejs/assets: {e.Message} " +
                    $"The destination may be partly deleted, so delete it and build again. " +
                    $"A read only file in it will do this, and File.Copy keeps the read only " +
                    $"attribute, so a read only source asset causes it on the NEXT build.");
            }

            // Counted off the destination rather than off the copies performed,
            // because those two disagreed in exactly the case this fix is about:
            // the old code logged "Copied 1 ... Copied 1 ... copied 2 asset
            // file(s)" for a build that shipped one file.
            var present = Directory.Exists(destDir)
                ? Directory.GetFiles(destDir, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".meta")).ToArray()
                : new string[0];
            _copiedAssetCount = present.Length;

            if (present.Length > 0) {
                var list = string.Join("\n  ", present
                    .Select(f => f.Substring(destDir.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/'))
                    .OrderBy(x => x, StringComparer.Ordinal));
                Debug.Log($"[JSRunner] StreamingAssets/onejs/assets now holds {present.Length} file(s) " +
                    $"from {_assetSources.Count} app(s):\n  {list}");
            } else if (_assetSources.Count == 0) {
                Debug.Log("[JSRunner] No app in this build ships an assets folder; " +
                    "StreamingAssets/onejs/assets is empty.");
            }

            // The destination is under Assets, so Unity has to be told: without
            // this the new files have no .meta and the old ones' metas linger.
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Deletes a tree, clearing read only as it goes.
        ///
        /// Windows refuses to delete a read only file, and POSIX does not care
        /// because it ties the right to delete to the DIRECTORY. So this is a
        /// Windows failure that cannot be reproduced on a Mac, which is how it
        /// reached a release: a Perforce workspace, or a restored archive, leaves
        /// exactly this state.
        /// </summary>
        static void DeleteTree(string dir) {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(dir, true);
        }

        /// <summary>Same folder on disk, whatever the separators and case say.</summary>
        static bool PathsEqual(string a, string b) {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, '/').Replace('\\', '/'),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, '/').Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        int CopyDirectoryRecursive(string src, string dest) {
            int count = 0;

            if (!Directory.Exists(dest)) {
                Directory.CreateDirectory(dest);
            }

            foreach (var file in Directory.GetFiles(src)) {
                if (file.EndsWith(".meta")) continue;
                var destFile = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, destFile, true);
                count++;
            }

            foreach (var dir in Directory.GetDirectories(src)) {
                var destSubDir = Path.Combine(dest, Path.GetFileName(dir));
                count += CopyDirectoryRecursive(dir, destSubDir);
            }

            return count;
        }

        public void OnPostprocessBuild(BuildReport report) {
            if (_createdAssets.Count > 0) {
                Debug.Log($"[JSRunner] Build complete. {_createdAssets.Count} asset(s) created/updated.");
            }
        }
    }
}
