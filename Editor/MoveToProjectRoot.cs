using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OneJS.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    /// <summary>
    /// Move to Project Root (Specs/ROOT_CARTS.md, 5.9): a cart that was taken into
    /// <c>Assets/&lt;Name&gt;/~</c> as a JSRunner app becomes a root cart at <c>&lt;Name&gt;/</c>,
    /// and the runners that ran it as an app run it as a cart.
    ///
    /// In order: the watcher stops (on Windows its handles block the move); the folder moves,
    /// with <c>git mv</c> when git tracks it or it is a submodule, so history follows; the
    /// runners link to the cart by its oj.json name and the app's bundle beside the Panel
    /// Settings is deleted; then the cart's ojplay takes out what <c>ojplay init --unity</c>
    /// wrote (<c>ojplay init --from-unity</c>, which leaves and lists a file somebody changed)
    /// and Set Up installs it. The Panel Settings and the prefab stay where they are, now only
    /// settings and a runner.
    ///
    /// Nothing here runs until somebody presses the button. An app that is not moved, cart or
    /// not, is untouched.
    /// </summary>
    public static class MoveToProjectRoot {
        /// <summary>
        /// The ojplay that runs <c>init --from-unity</c>, the first half of the cleanup. Run
        /// through <c>npm exec --package</c> rather than the cart's own copy, which is the one
        /// init --unity installed and older than the flag; ojplay 0.12.0 is the first with it.
        /// </summary>
        public const string Ojplay = "ojplay@latest";

        /// <summary>What a move would do, or why it cannot.</summary>
        public sealed class Plan {
            /// <summary>The cart's oj.json name, which the runners link by.</summary>
            public string Name;
            /// <summary>The app's working directory, the ~ that moves.</summary>
            public string From;
            /// <summary>Where it goes: the project root and the app folder's name.</summary>
            public string To;
            /// <summary>The folder's name at the root.</summary>
            public string Folder;
            /// <summary>The app folder as an asset path, Assets/&lt;Name&gt;.</summary>
            public string AssetFolder;
            /// <summary>Why it cannot move, as a sentence, or null.</summary>
            public string Problem;
            /// <summary>The ~ as the inspector shows it.</summary>
            public string Shown => AssetFolder + "/~";
        }

        /// <summary>
        /// The move a runner's app could make, or null when the app is not a cart: no oj.json
        /// in its ~, or the runner is a cart runner already, or it is not a valid app.
        /// </summary>
        public static Plan PlanFor(JSRunner runner) {
            if (runner == null || runner.IsCart) return null;
            var from = runner.WorkingDirFullPath;
            var assetFolder = runner.InstanceFolderAssetPath?.Replace('\\', '/');
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(assetFolder) || !File.Exists(Path.Combine(from, "oj.json"))) return null;

            var folder = Path.GetFileName(assetFolder);
            var plan = new Plan {
                From = from,
                Folder = folder,
                AssetFolder = assetFolder,
                To = Path.Combine(runner.ProjectRoot, folder),
            };
            var cart = RootCarts.ReadAt(from, plan.Shown);
            plan.Name = cart.Name;
            if (cart.Name != null) plan.Problem = ProblemOf(plan);
            else if (cart.Problem.Contains("has no \"name\"")) plan.Problem = $"{plan.Shown}/oj.json has no \"name\", and a runner links to a cart by it. Add one, such as \"name\": \"{folder}\".";
            else plan.Problem = cart.Problem;
            return plan;
        }

        static string ProblemOf(Plan plan) {
            if (plan.Folder.StartsWith(".") || RootCarts.IsSkipped(plan.Folder))
                return $"{plan.Folder}/ at the project root is a name Unity or npm keeps for itself. Rename {plan.AssetFolder} first.";
            if (Directory.Exists(plan.To) || File.Exists(plan.To))
                return $"{plan.Folder}/ is already at the project root. Move or rename it, then try again.";
            var same = RootCarts.All.FirstOrDefault(c => c.Usable && RootCarts.SameName(c.Name, plan.Name));
            if (same != null)
                return $"{same.OjJson} is already named \"{same.Name}\", and a runner links by name. Rename one of them in its oj.json.";
            return null;
        }

        /// <summary>
        /// Moves the runner's app to the project root and links the runners to it. The move,
        /// the links and the bundle's deletion happen before this returns; the cleanup and the
        /// install run after, and `onDone` gets whether they succeeded. Answers false, having
        /// changed nothing, when the move could not start.
        /// </summary>
        public static bool Move(JSRunner runner, Action<bool> onDone = null) {
            var plan = PlanFor(runner);
            if (plan == null) {
                Debug.LogError("[JSRunner] This runner's app has no oj.json in its ~, so it is not a cart and has nothing to move.");
                return false;
            }
            if (plan.Problem != null) {
                Debug.LogError($"[JSRunner] {plan.Name ?? plan.Folder} cannot move to the project root: {plan.Problem}");
                return false;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                Debug.LogError($"[JSRunner] Leave Play mode to move {plan.Name} to the project root.");
                return false;
            }

            NodeWatcherManager.StopWatcher(plan.From);
            string how;
            try {
                how = MoveFolder(runner.ProjectRoot, plan.From, plan.To);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is Win32Exception) {
                // Expected failure: a handle still open in the folder, or git refusing the move.
                Debug.LogError($"[JSRunner] Could not move {plan.Shown} to {plan.Folder}/, so nothing was changed: {e.Message}");
                return false;
            }

            var linked = Link(runner, plan);
            var deleted = DeleteBundle(plan.AssetFolder);
            RootCarts.Invalidate();
            var resolution = RootCarts.Resolve(plan.Name);
            Debug.Log($"[JSRunner] Moved {plan.Name} from {plan.Shown} to {plan.Folder}/ ({how}). Linked by name: {string.Join(", ", linked)}." +
                (deleted.Count > 0 ? $" Deleted {string.Join(", ", deleted)}." : "") +
                " A runner in a scene that is not open still points at the app: pick the cart in its Cart field.");
            if (resolution.Outcome != RootCarts.Outcome.Found) {
                Debug.LogError($"[JSRunner] {plan.Folder}/ moved, but the scan does not find one cart named {plan.Name} there: {resolution.Problem}");
                onDone?.Invoke(false);
                return true;
            }
            CartRunners.SetUpMoved(resolution.Cart, ok => {
                if (ok) Debug.Log($"[JSRunner] {plan.Name} is a root cart at {plan.Folder}/ and builds with its own ojplay.");
                onDone?.Invoke(ok);
            });
            return true;
        }

        /// <summary>
        /// Moves `from` to `to`: <c>git mv</c> when the project's repository tracks it or
        /// holds it as a submodule, else a plain move. Retried for a few seconds, because a
        /// watcher's handles can outlive its process by a moment on Windows. Answers how.
        /// </summary>
        internal static string MoveFolder(string projectRoot, string from, string to) {
            var top = Git(projectRoot, "rev-parse --show-toplevel", out var topOut) == 0 ? Path.GetFullPath(topOut.Trim()) : null;
            string relFrom = null, relTo = null;
            var submodule = false;
            var tracked = false;
            if (top != null && IsUnder(from, top) && IsUnder(to, top)) {
                relFrom = Path.GetRelativePath(top, from).Replace('\\', '/');
                relTo = Path.GetRelativePath(top, to).Replace('\\', '/');
                submodule = Git(top, $"ls-files --stage -- \"{relFrom}\"", out var stage) == 0 && stage.StartsWith("160000");
                tracked = submodule || (Git(top, $"ls-files -- \"{relFrom}\"", out var files) == 0 && files.Trim().Length > 0);
            }
            for (var attempt = 1; ; attempt++) {
                try {
                    if (tracked) {
                        if (Git(top, $"mv \"{relFrom}\" \"{relTo}\"", out var output) != 0) throw new IOException($"git mv {relFrom} {relTo} failed: {output.Trim()}");
                        return submodule ? "git mv, as a submodule" : "git mv";
                    }
                    Directory.Move(from, to);
                    return "moved";
                } catch (IOException) when (attempt < 10) {
                    Thread.Sleep(300);
                } catch (UnauthorizedAccessException) when (attempt < 10) {
                    Thread.Sleep(300);
                }
            }
        }

        static bool IsUnder(string path, string root) {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(top, Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        /// <summary>git in `dir`; the exit code, with stdout and stderr together in `output`. -1 when git cannot start.</summary>
        static int Git(string dir, string arguments, out string output) {
            try {
                using var process = new Process {
                    StartInfo = new ProcessStartInfo("git", arguments) {
                        WorkingDirectory = dir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                };
                process.Start();
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                output = stdout.Result + stderr;
                return process.ExitCode;
            } catch (Exception e) when (e is Win32Exception || e is InvalidOperationException) {
                // Expected failure: no git on this machine. The folder is then moved plainly.
                output = e.Message;
                return -1;
            }
        }

        /// <summary>
        /// Links every runner of the app to the cart: the runners in prefabs in the app's
        /// folder first, so their instances follow, then any in the open scenes that still run
        /// the app. Answers what it linked.
        /// </summary>
        static List<string> Link(JSRunner runner, Plan plan) {
            var panelSettings = runner.PanelSettingsAsset;
            var linked = new List<string>();
            // Found before the prefabs change, while they all still run the app.
            var inScenes = FindCompat.FindObjectsByTypeIncludingInactive<JSRunner>()
                .Where(r => r != null && !EditorUtility.IsPersistent(r) && !r.IsCart && r.PanelSettingsAsset == panelSettings)
                .ToList();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { plan.AssetFolder })) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try {
                    var changed = false;
                    foreach (var inPrefab in root.GetComponentsInChildren<JSRunner>(true)) {
                        if (inPrefab.IsCart || inPrefab.PanelSettingsAsset != panelSettings) continue;
                        var so = new SerializedObject(inPrefab);
                        so.FindProperty("_cart").stringValue = plan.Name;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                    }
                    if (changed) {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        linked.Add(path);
                    }
                } finally {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            foreach (var inScene in inScenes) {
                if (inScene == null) continue;
                if (inScene.IsCart) {
                    // An instance of a prefab linked above: it follows the prefab, and its
                    // preview starts again from the cart.
                    inScene.RestartEditModePreview();
                    continue;
                }
                inScene.SetCart(plan.Name);
                EditorSceneManager.MarkSceneDirty(inScene.gameObject.scene);
                linked.Add($"{inScene.name} in {inScene.gameObject.scene.name} (save the scene to keep it)");
            }
            if (linked.Count == 0) linked.Add("no runner");
            return linked;
        }

        /// <summary>The app's bundle beside its Panel Settings: a cart's editor build is in Library.</summary>
        static List<string> DeleteBundle(string assetFolder) {
            var deleted = new List<string>();
            foreach (var name in new[] { "app.js.txt", "app.js.map.txt", "app.sl.json" }) {
                var path = assetFolder + "/" + name;
                if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)) {
                    if (AssetDatabase.DeleteAsset(path)) deleted.Add(path);
                }
            }
            return deleted;
        }
    }
}
