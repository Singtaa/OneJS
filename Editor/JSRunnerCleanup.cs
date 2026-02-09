using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    /// <summary>
    /// Tracks JSRunner components by their instance folder path. When a JSRunner is removed,
    /// tracking is updated only; the folder is left on disk (no prompt or auto-delete).
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
            // NOTE: We only ADD/UPDATE here, never remove. Removal is handled by CheckForRemovedRunners().
            // Folder path comes from ProjectConfig (runner.InstanceFolder).
            var runners = UnityEngine.Object.FindObjectsByType<JSRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var runner in runners) {
                var stableId = GetStableId(runner);
                var folder = runner.InstanceFolder;
                if (string.IsNullOrEmpty(folder)) continue;
                _trackedFolders[stableId] = folder;
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

            // Find tracked runners that no longer exist and stop tracking them (folder is left on disk)
            var toRemove = new List<string>();
            foreach (var kvp in _trackedFolders) {
                if (!currentIds.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove) {
                _trackedFolders.Remove(id);
            }
        }
    }
}
