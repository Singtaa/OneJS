using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    /// <summary>
    /// Tracks JSRunner components and cleans up their instance folders when removed in Edit mode.
    /// Also detects GameObject renames and offers to rename the instance folders accordingly.
    /// Uses ObjectChangeEvents API for reliable destruction detection without false positives
    /// from play mode transitions or domain reloads.
    /// </summary>
    [InitializeOnLoad]
    static class JSRunnerCleanup {
        // Track JSRunner instances by GlobalObjectId (survives domain reload), storing their folder path
        static Dictionary<string, string> _trackedFolders = new Dictionary<string, string>();

        // Track folder paths where user declined rename (old path -> new path they declined)
        // This prevents repeatedly prompting for the same rename
        static Dictionary<string, string> _declinedRenames = new Dictionary<string, string>();

        // Track folder paths with pending rename prompts to prevent duplicate dialogs
        static HashSet<string> _pendingRenamePrompts = new HashSet<string>();

        static JSRunnerCleanup() {
            ObjectChangeEvents.changesPublished += OnObjectChanged;
            EditorApplication.hierarchyChanged += RefreshTrackedRunners;

            // Initial scan
            RefreshTrackedRunners();
        }

        static string GetStableId(JSRunner runner) {
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(runner);
            return globalId.ToString();
        }

        static void RefreshTrackedRunners() {
            // Don't track during play mode
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Find all JSRunner components in loaded scenes and update tracking
            // NOTE: We only ADD/UPDATE here, never remove. Removal is handled by CheckForRemovedRunners()
            // which prompts the user before deleting folders.
            // IMPORTANT: Include inactive objects - disabled GameObjects still have valid JSRunners
            var runners = UnityEngine.Object.FindObjectsByType<JSRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var runner in runners) {
                var stableId = GetStableId(runner);

                // Get the expected folder path based on current GameObject name
                var expectedFolder = runner.InstanceFolder;
                if (string.IsNullOrEmpty(expectedFolder)) continue;

                // Check if we have a previously tracked folder
                if (_trackedFolders.TryGetValue(stableId, out var trackedFolder)) {
                    // Normalize paths for comparison
                    var normalizedExpected = Path.GetFullPath(expectedFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var normalizedTracked = Path.GetFullPath(trackedFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // Check if name changed (expected path differs from tracked path)
                    if (!string.Equals(normalizedExpected, normalizedTracked, StringComparison.OrdinalIgnoreCase)) {
                        // Name changed - check if old folder exists on disk and new doesn't
                        if (Directory.Exists(trackedFolder) && !Directory.Exists(expectedFolder)) {
                            // Check if user already declined this rename
                            if (_declinedRenames.TryGetValue(trackedFolder, out var declinedNewPath) &&
                                string.Equals(Path.GetFullPath(declinedNewPath), normalizedExpected, StringComparison.OrdinalIgnoreCase)) {
                                // User declined this exact rename before - don't prompt again
                                // Keep tracking the old folder path
                                continue;
                            }

                            // Check if there's already a pending prompt for this folder
                            if (_pendingRenamePrompts.Contains(trackedFolder)) {
                                continue;
                            }

                            // Schedule rename prompt (don't do it synchronously during hierarchy change)
                            var oldFolder = trackedFolder;
                            var newFolder = expectedFolder;
                            var gameObjectName = runner.gameObject.name;
                            var capturedStableId = stableId;

                            _pendingRenamePrompts.Add(oldFolder);
                            EditorApplication.delayCall += () => {
                                _pendingRenamePrompts.Remove(oldFolder);
                                var renamed = PromptRenameFolder(oldFolder, newFolder, gameObjectName);
                                if (renamed) {
                                    // Update tracking to new path
                                    _trackedFolders[capturedStableId] = newFolder;
                                    // Clear any declined rename for the old folder
                                    _declinedRenames.Remove(oldFolder);
                                } else {
                                    // User declined - record it so we don't prompt again
                                    _declinedRenames[oldFolder] = newFolder;
                                    // Keep tracking old folder path (it still exists on disk)
                                }
                            };
                            // Don't update tracking yet - wait for user response
                        } else if (Directory.Exists(expectedFolder)) {
                            // New folder already exists (maybe renamed manually) - just update tracking
                            _trackedFolders[stableId] = expectedFolder;
                            // Clear any declined rename for the old folder
                            _declinedRenames.Remove(trackedFolder);
                        }
                        // If neither exists, keep tracking expected path for when it gets created
                        else {
                            _trackedFolders[stableId] = expectedFolder;
                        }
                    }
                } else {
                    // New runner - track it
                    _trackedFolders[stableId] = expectedFolder;
                }
            }
        }

        static void OnObjectChanged(ref ObjectChangeEventStream stream) {
            // Don't process during play mode
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            for (int i = 0; i < stream.length; i++) {
                var eventType = stream.GetEventType(i);

                switch (eventType) {
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        // Schedule a check on the next frame to see if any tracked runners are gone
                        ScheduleCleanupCheck();
                        break;
                }
            }
        }

        static bool _cleanupScheduled;

        static void ScheduleCleanupCheck() {
            if (_cleanupScheduled) return;
            _cleanupScheduled = true;

            EditorApplication.delayCall += () => {
                _cleanupScheduled = false;
                CheckForRemovedRunners();
            };
        }

        static void CheckForRemovedRunners() {
            // Don't process during play mode
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Get current set of JSRunner IDs
            // IMPORTANT: Include inactive objects - disabled GameObjects still have valid JSRunners
            var runners = UnityEngine.Object.FindObjectsByType<JSRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var currentIds = new HashSet<string>();
            foreach (var runner in runners) {
                currentIds.Add(GetStableId(runner));
            }

            // Find tracked runners that no longer exist
            var removedFolders = new List<string>();
            var toRemove = new List<string>();

            foreach (var kvp in _trackedFolders) {
                if (!currentIds.Contains(kvp.Key)) {
                    toRemove.Add(kvp.Key);
                    if (!string.IsNullOrEmpty(kvp.Value) && Directory.Exists(kvp.Value)) {
                        removedFolders.Add(kvp.Value);
                    }
                }
            }

            // Clean up tracking
            foreach (var id in toRemove) {
                _trackedFolders.Remove(id);
            }

            // Prompt for each removed folder
            foreach (var folder in removedFolders) {
                PromptCleanupFolder(folder);
            }
        }

        static void PromptCleanupFolder(string instanceFolder) {
            if (string.IsNullOrEmpty(instanceFolder)) return;
            if (!Directory.Exists(instanceFolder)) return;

            // Don't prompt during play mode transitions
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Count files in the folder
            int fileCount = 0;
            try {
                fileCount = Directory.GetFiles(instanceFolder, "*", SearchOption.AllDirectories).Length;
            } catch { }

            var result = EditorUtility.DisplayDialogComplex(
                "Delete JSRunner Folder?",
                $"A JSRunner component was removed.\n\n" +
                $"Delete its instance folder?\n{instanceFolder}\n\n" +
                $"Files: {fileCount}",
                "Delete",
                "Keep",
                "");

            if (result == 0) { // Delete
                DeleteFolderRobust(instanceFolder);
            }
        }

        /// <summary>
        /// Prompts the user to rename a JSRunner's instance folder after a GameObject rename.
        /// Handles renaming both the instance folder and the working directory inside it.
        /// </summary>
        /// <returns>True if the rename was performed, false if user declined or it failed.</returns>
        static bool PromptRenameFolder(string oldFolder, string newFolder, string newName) {
            if (string.IsNullOrEmpty(oldFolder) || string.IsNullOrEmpty(newFolder)) return false;
            if (!Directory.Exists(oldFolder)) return false;

            // Don't prompt during play mode transitions
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return false;

            // Extract old name from folder path: "{oldName}_{instanceId}"
            var oldFolderName = Path.GetFileName(oldFolder);
            var underscoreIndex = oldFolderName.LastIndexOf('_');
            var oldName = underscoreIndex > 0 ? oldFolderName.Substring(0, underscoreIndex) : oldFolderName;

            var result = EditorUtility.DisplayDialogComplex(
                "Rename JSRunner Folder?",
                $"The GameObject was renamed from \"{oldName}\" to \"{newName}\".\n\n" +
                $"Rename the instance folder to match?\n\n" +
                $"From: {oldFolder}\n" +
                $"To: {newFolder}",
                "Rename",
                "Keep Old Name",
                "");

            if (result == 0) { // Rename
                return RenameFolderRobust(oldFolder, newFolder, oldName, newName);
            }

            return false;
        }

        /// <summary>
        /// Robustly rename a JSRunner instance folder, including its working directory.
        /// </summary>
        /// <returns>True if the rename succeeded, false otherwise.</returns>
        static bool RenameFolderRobust(string oldFolder, string newFolder, string oldName, string newName) {
            // First, try to kill any esbuild processes that might be locking files
            KillEsbuildProcesses(oldFolder);

            // Try rename with retries
            const int maxRetries = 3;
            const int retryDelayMs = 500;

            for (int i = 0; i < maxRetries; i++) {
                try {
                    // Step 1: Rename the inner working directory first (if it exists)
                    // Working dir format: {oldName}~ inside the instance folder
                    var oldWorkingDir = Path.Combine(oldFolder, $"{oldName}~");
                    var newWorkingDir = Path.Combine(oldFolder, $"{newName}~");

                    if (Directory.Exists(oldWorkingDir) && !string.Equals(oldWorkingDir, newWorkingDir, StringComparison.OrdinalIgnoreCase)) {
                        Directory.Move(oldWorkingDir, newWorkingDir);
                        Debug.Log($"[JSRunner] Renamed working directory: {oldName}~ → {newName}~");
                    }

                    // Step 2: Rename the instance folder
                    Directory.Move(oldFolder, newFolder);

                    // Step 3: Handle .meta files
                    var oldMetaPath = oldFolder + ".meta";
                    var newMetaPath = newFolder + ".meta";
                    if (File.Exists(oldMetaPath)) {
                        if (File.Exists(newMetaPath)) {
                            File.Delete(oldMetaPath);
                        } else {
                            File.Move(oldMetaPath, newMetaPath);
                        }
                    }

                    AssetDatabase.Refresh();
                    Debug.Log($"[JSRunner] Renamed instance folder: {Path.GetFileName(oldFolder)} → {Path.GetFileName(newFolder)}");
                    return true;
                } catch (Exception) when (i < maxRetries - 1) {
                    // Wait and retry
                    System.Threading.Thread.Sleep(retryDelayMs);
                    // Try killing processes again
                    KillEsbuildProcesses(oldFolder);
                }
            }

            // All retries failed
            Debug.LogError($"[JSRunner] Failed to rename folder. Files may be locked by another process.");
            EditorUtility.DisplayDialog("Rename Failed",
                $"Failed to rename folder. Some files may be locked by another process.\n\n" +
                $"Try closing any terminals or processes that might be using files in:\n{oldFolder}\n\n" +
                $"Then manually rename the folder to:\n{newFolder}",
                "OK");
            return false;
        }

        /// <summary>
        /// Robustly delete a folder, handling locked files (common on Windows with esbuild.exe).
        /// </summary>
        static void DeleteFolderRobust(string folderPath) {
            // First, try to kill any esbuild processes that might be locking files
            KillEsbuildProcesses(folderPath);

            // Try deletion with retries
            const int maxRetries = 3;
            const int retryDelayMs = 500;

            for (int i = 0; i < maxRetries; i++) {
                try {
                    Directory.Delete(folderPath, true);

                    // Also delete .meta file if it exists
                    var metaPath = folderPath + ".meta";
                    if (File.Exists(metaPath)) {
                        File.Delete(metaPath);
                    }

                    AssetDatabase.Refresh();
                    Debug.Log($"[JSRunner] Deleted instance folder: {folderPath}");
                    return;
                } catch (Exception) when (i < maxRetries - 1) {
                    // Wait and retry
                    System.Threading.Thread.Sleep(retryDelayMs);
                    // Try killing processes again
                    KillEsbuildProcesses(folderPath);
                }
            }

            // All retries failed - try to move to pending cleanup folder
            try {
                var pendingFolder = GetPendingCleanupFolder();
                var destFolder = Path.Combine(pendingFolder, Path.GetFileName(folderPath) + "_" + DateTime.Now.Ticks);
                Directory.Move(folderPath, destFolder);

                // Delete .meta file if it exists
                var metaPath = folderPath + ".meta";
                if (File.Exists(metaPath)) {
                    File.Delete(metaPath);
                }

                AssetDatabase.Refresh();
                Debug.Log($"[JSRunner] Moved locked folder to pending cleanup: {destFolder}");

                // Schedule cleanup of pending folders
                EditorApplication.delayCall += CleanupPendingFolders;
            } catch (Exception ex) {
                Debug.LogError($"[JSRunner] Failed to delete folder (files may be locked): {ex.Message}");
                EditorUtility.DisplayDialog("Delete Failed",
                    $"Failed to delete folder. Some files may be locked by another process.\n\n" +
                    $"Try closing any terminals or processes that might be using esbuild, then manually delete:\n{folderPath}",
                    "OK");
            }
        }

        /// <summary>
        /// Kill any esbuild processes that might be running from the given folder.
        /// </summary>
        static void KillEsbuildProcesses(string folderPath) {
#if UNITY_EDITOR_WIN
            try {
                // Normalize the folder path for comparison
                var normalizedFolder = Path.GetFullPath(folderPath).ToLowerInvariant();

                foreach (var process in Process.GetProcessesByName("esbuild")) {
                    try {
                        // Check if process is from our folder
                        var processPath = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath)) {
                            var normalizedProcessPath = Path.GetFullPath(processPath).ToLowerInvariant();
                            if (normalizedProcessPath.StartsWith(normalizedFolder)) {
                                process.Kill();
                                process.WaitForExit(1000);
                            }
                        }
                    } catch {
                        // Ignore errors for individual processes (access denied, already exited, etc.)
                    }
                }
            } catch {
                // Ignore errors - best effort cleanup
            }
#endif
        }

        /// <summary>
        /// Get or create a folder for pending cleanup operations.
        /// </summary>
        static string GetPendingCleanupFolder() {
            var folder = Path.Combine(Application.temporaryCachePath, "OneJS_PendingCleanup");
            if (!Directory.Exists(folder)) {
                Directory.CreateDirectory(folder);
            }
            return folder;
        }

        /// <summary>
        /// Try to clean up any previously failed deletions.
        /// </summary>
        static void CleanupPendingFolders() {
            var pendingFolder = GetPendingCleanupFolder();
            if (!Directory.Exists(pendingFolder)) return;

            try {
                foreach (var dir in Directory.GetDirectories(pendingFolder)) {
                    try {
                        Directory.Delete(dir, true);
                        Debug.Log($"[JSRunner] Cleaned up pending folder: {dir}");
                    } catch {
                        // Still locked, will try again later
                    }
                }

                // If folder is empty, delete it
                if (Directory.GetDirectories(pendingFolder).Length == 0 &&
                    Directory.GetFiles(pendingFolder).Length == 0) {
                    Directory.Delete(pendingFolder);
                }
            } catch {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Called on editor startup to clean up any pending folders from previous sessions.
        /// </summary>
        [InitializeOnLoadMethod]
        static void CleanupOnEditorStart() {
            EditorApplication.delayCall += CleanupPendingFolders;
        }
    }
}
