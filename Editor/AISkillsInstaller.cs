using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    /// <summary>
    /// Copies the AI Skills that ship inside the OneJS package into the project's
    /// `.claude/skills/` folder, where Claude Code and compatible agents look for them.
    ///
    /// Unity's own AI Assistant already discovers `SKILL.md` anywhere under `Assets`,
    /// so it needs no install step. Terminal coding agents only scan the project root,
    /// which is what this bridges.
    ///
    /// Always user initiated, and never overwrites a modified skill without asking.
    /// Batch mode keeps the user's version rather than prompting, so the same code path
    /// is safe from `-executeMethod` and CI.
    /// </summary>
    public static class AISkillsInstaller {
        const string MenuPath = "Tools/OneJS/Install AI Skills";
        const string SkillsSubPath = "AI/Skills";
        const string DestSubPath = ".claude/skills";

        public struct Result {
            public List<string> Installed;
            public List<string> AlreadyCurrent;
            public List<string> Kept;
            public List<string> Failed;
            public string Destination;
            public bool Found;
        }

        [MenuItem(MenuPath)]
        public static void InstallSkillsMenu() {
            Install(interactive: true);
        }

        /// <summary>Entry point for `-executeMethod`. Never prompts.</summary>
        public static void InstallSkillsBatch() {
            var result = Install(interactive: false);
            if (!result.Found || result.Failed.Count > 0) EditorApplication.Exit(1);
        }

        /// <summary>
        /// Copies every shipped skill into the project's `.claude/skills/`.
        /// When a destination skill exists and differs, an interactive call asks before
        /// overwriting and a non-interactive one keeps what is already there.
        /// </summary>
        public static Result Install(bool interactive) {
            var result = new Result {
                Installed = new List<string>(),
                AlreadyCurrent = new List<string>(),
                Kept = new List<string>(),
                Failed = new List<string>(),
            };

            var source = FindShippedSkillsFolder();
            if (source == null) {
                var message = $"Could not locate the OneJS '{SkillsSubPath}' folder. Expected it " +
                              "inside the OneJS package. If OneJS was installed by copying only part " +
                              "of the package, reinstall it from the Package Manager or the Asset Store.";
                if (interactive) EditorUtility.DisplayDialog("OneJS AI Skills", message, "OK");
                else Debug.LogError($"[OneJS] {message}");
                return result;
            }

            result.Found = true;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) {
                Debug.LogError("[OneJS] Could not resolve the project root.");
                result.Found = false;
                return result;
            }

            var destRoot = Path.Combine(projectRoot, DestSubPath.Replace('/', Path.DirectorySeparatorChar));
            result.Destination = destRoot;

            foreach (var skillDir in Directory.GetDirectories(source)) {
                var skillName = Path.GetFileName(skillDir);
                var dest = Path.Combine(destRoot, skillName);

                if (Directory.Exists(dest)) {
                    if (FoldersMatch(skillDir, dest)) {
                        result.AlreadyCurrent.Add(skillName);
                        continue;
                    }

                    var overwrite = interactive && EditorUtility.DisplayDialog("OneJS AI Skills",
                        $"'{skillName}' already exists in {DestSubPath}/ and differs from the version " +
                        "shipped with OneJS.\n\nOverwrite it? Any local edits to that skill will be lost.",
                        "Overwrite", "Keep mine");

                    if (!overwrite) {
                        result.Kept.Add(skillName);
                        continue;
                    }
                }

                try {
                    CopySkill(skillDir, dest);
                    result.Installed.Add(skillName);
                } catch (IOException e) {
                    Debug.LogError($"[OneJS] Could not write '{skillName}' to {dest}: {e.Message}");
                    result.Failed.Add(skillName);
                } catch (System.UnauthorizedAccessException e) {
                    Debug.LogError($"[OneJS] No permission to write '{skillName}' to {dest}: {e.Message}");
                    result.Failed.Add(skillName);
                }
            }

            Report(result, interactive);
            return result;
        }

        /// <summary>
        /// Resolves the OneJS package root for every install shape: Package Manager
        /// (Packages/ or the package cache), a git clone into Assets, and the Asset Store
        /// package. Mirrors how JSRunner locates its Editor/Templates folder.
        ///
        /// Public so guards and tooling resolve the package the same way this does.
        /// A second copy of this logic is how the two quietly start disagreeing.
        /// </summary>
        public static string FindPackageRoot() {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(AISkillsInstaller).Assembly);

            if (packageInfo != null && Directory.Exists(packageInfo.resolvedPath))
                return packageInfo.resolvedPath;

            // Installed under Assets. Locate this script, then walk up out of Editor/.
            var guids = AssetDatabase.FindAssets($"{nameof(AISkillsInstaller)} t:MonoScript");
            foreach (var guid in guids) {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith($"{nameof(AISkillsInstaller)}.cs")) continue;

                var editorDir = Path.GetDirectoryName(assetPath);
                var packageRoot = Path.GetDirectoryName(editorDir);
                if (!string.IsNullOrEmpty(packageRoot)) return packageRoot;
            }

            return null;
        }

        static string FindShippedSkillsFolder() {
            var root = FindPackageRoot();
            if (string.IsNullOrEmpty(root)) return null;

            var candidate = Path.Combine(root, SkillsSubPath);
            return Directory.Exists(candidate) ? candidate : null;
        }

        static void CopySkill(string source, string dest) {
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(source)) {
                if (file.EndsWith(".meta")) continue;
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(source)) {
                CopySkill(dir, Path.Combine(dest, Path.GetFileName(dir)));
            }
        }

        /// <summary>Compares shipped content against installed content, ignoring .meta files.</summary>
        static bool FoldersMatch(string source, string dest) {
            var sourceFiles = RelativeFiles(source);
            if (!sourceFiles.SetEquals(RelativeFiles(dest))) return false;

            foreach (var rel in sourceFiles) {
                var a = File.ReadAllBytes(Path.Combine(source, rel));
                var b = File.ReadAllBytes(Path.Combine(dest, rel));
                if (!a.SequenceEqual(b)) return false;
            }

            return true;
        }

        static HashSet<string> RelativeFiles(string root) {
            return new HashSet<string>(
                Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".meta"))
                    .Select(f => f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/')
                        .Replace('\\', '/')));
        }

        static void Report(Result result, bool interactive) {
            var lines = new List<string>();
            if (result.Installed.Count > 0) lines.Add($"Installed: {string.Join(", ", result.Installed)}");
            if (result.AlreadyCurrent.Count > 0) lines.Add($"Already up to date: {string.Join(", ", result.AlreadyCurrent)}");
            if (result.Kept.Count > 0) lines.Add($"Kept your version: {string.Join(", ", result.Kept)}");
            if (result.Failed.Count > 0) lines.Add($"Failed to write: {string.Join(", ", result.Failed)}");

            if (lines.Count == 0) {
                const string none = "No skills were found to install.";
                if (interactive) EditorUtility.DisplayDialog("OneJS AI Skills", none, "OK");
                else Debug.LogWarning($"[OneJS] {none}");
                return;
            }

            var summary = string.Join("\n", lines);
            Debug.Log($"[OneJS] AI Skills -> {result.Destination}\n{summary}");

            if (!interactive) return;

            var note = result.Installed.Count > 0
                ? "\n\nStart a new agent session to pick them up."
                : "";
            EditorUtility.DisplayDialog("OneJS AI Skills",
                $"{summary}\n\nLocation: {DestSubPath}/{note}", "OK");
        }
    }
}
