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
    /// Uses ObjectChangeEvents API for reliable destruction detection without false positives
    /// from play mode transitions or domain reloads.
    /// </summary>
    [InitializeOnLoad]
    static class JSRunnerCleanup {
        // Track JSRunner instances by GlobalObjectId (survives domain reload), storing their folder path
        static Dictionary<string, string> _trackedFolders = new Dictionary<string, string>();

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

                // Update tracked folder path (in case scene was saved and paths are now valid)
                var folder = runner.InstanceFolder;
                if (!string.IsNullOrEmpty(folder)) {
                    _trackedFolders[stableId] = folder;
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
                } catch (Exception ex) when (i < maxRetries - 1) {
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
