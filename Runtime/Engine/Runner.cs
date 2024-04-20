using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneJS {
    /// <summary>
    /// Executes and optionally live-reloads an entry file, while managing scene-related GameObject cleanups.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(ScriptEngine))]
    public class Runner : MonoBehaviour {
        [Tooltip("The entry file to run. Relative to the OneJS WorkingDir.")]
        public string entryFile = "@outputs/esbuild/app.js";
        public bool runOnStart = true;

        [Tooltip("Watch entry file for changes and reload.")]
        public bool liveReload = true;
        [Tooltip("How often to check for changes in the entry file in milliseconds")]
        public int pollingInterval = 300;
        public bool clearGameObjects = true;
        public bool clearLogs = true;
        [Tooltip("Respawn the Janitor during scene loads so that it doesn't clean up your additively loaded scenes.")]
        public bool respawnJanitorOnSceneLoad = true;
        [Tooltip("Don't clean up on OnDisable(). (Useful for when your workflow involves disabling ScriptEngine)")]
        public bool stopCleaningOnDisable;

        ScriptEngine _engine;
        Janitor _janitor;

        float _lastCheckTime;
        DateTime _lastWriteTime;

        void Awake() {
            _engine = GetComponent<ScriptEngine>();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        
        void OnDestroy() {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Start() {
            if (!runOnStart) return;
            _engine.EvalFile(entryFile);
        }

        void Update() {
            if (!liveReload) return;
            if (Time.time - _lastCheckTime < pollingInterval / 1000f) return;
            _lastCheckTime = Time.time;
            CheckForChanges();
        }

        void CheckForChanges() {
            var writeTime = File.GetLastWriteTime(_engine.GetFullPath(entryFile));
            if (_lastWriteTime == writeTime) return; // No change
            _lastWriteTime = writeTime;
            _engine.Reload();
            _engine.EvalFile(entryFile);
        }

        void Respawn() {
            if (_janitor != null) {
                Destroy(_janitor.gameObject);
            }
            _janitor = new GameObject("Janitor").AddComponent<Janitor>();
            _janitor.clearGameObjects = clearGameObjects;
            _janitor.clearLogs = clearLogs;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (respawnJanitorOnSceneLoad) {
                Respawn();
            }
        }
    }
}