using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

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
                try {
                    Directory.Delete(instanceFolder, true);

                    // Also delete .meta file if it exists
                    var metaPath = instanceFolder + ".meta";
                    if (File.Exists(metaPath)) {
                        File.Delete(metaPath);
                    }

                    AssetDatabase.Refresh();
                    Debug.Log($"[JSRunner] Deleted instance folder: {instanceFolder}");
                } catch (Exception ex) {
                    Debug.LogError($"[JSRunner] Failed to delete folder: {ex.Message}");
                    EditorUtility.DisplayDialog("Delete Failed",
                        $"Failed to delete folder:\n\n{ex.Message}", "OK");
                }
            }
        }
    }
}
