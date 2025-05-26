using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneJS {
    /// <summary>
    /// Executes and optionally live-reloads an entry file, while managing scene-related GameObject cleanups.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(ScriptEngine))] [AddComponentMenu("OneJS/Runner")]
    public class Runner : MonoBehaviour {
        [Tooltip("The entry file to run. Relative to the OneJS WorkingDir.")]
        public string entryFile = "@outputs/esbuild/app.js";
        public bool runOnStart = true;

        [Tooltip("Watch entry file for changes and reload.")]
        public bool liveReload = true;
        [Tooltip("How often to check for changes in the entry file in milliseconds.")]
        public int pollingInterval = 300;
        public bool clearGameObjects = true;
        public bool clearLogs = true;
        [Tooltip("Respawn the Janitor during scene loads so that it doesn't clean up your additively loaded scenes.")]
        public bool respawnJanitorOnSceneLoad;
        [Tooltip("Don't clean up on OnDisable(). (Useful for when your workflow involves disabling ScriptEngine)")]
        public bool stopCleaningOnDisable;
        [Tooltip("Enable Live Reload for Standalone build.")]
        public bool standalone;

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

        void OnEnable() {
            Respawn();
            _engine.OnReload += OnReload;
            
            var fullpath = _engine.GetFullPath(entryFile);
            if (!File.Exists(fullpath)) {
                Debug.LogError($"Entry file not found: {fullpath}");
                return;
            }
            _lastWriteTime = File.GetLastWriteTime(fullpath); // This needs to be before EvalFile in case EvalFile crashes
            if (runOnStart) {
                // _engine.EvalFile(entryFile);
                StartCoroutine(DelayEvalFile());
            }
        }

        void OnDisable() {
            _engine.OnReload -= OnReload;
        }

        void Update() {
            if (!liveReload) return;
#if !UNITY_EDITOR && (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID)
            if (!standalone) return;
#endif
            if (Time.time - _lastCheckTime < pollingInterval / 1000f) return;
            _lastCheckTime = Time.time;
            CheckForChanges();
        }
        
        public void Reload() {
            _engine.Reload();
            // _engine.EvalFile(entryFile);
            StartCoroutine(DelayEvalFile());
        }
        
        IEnumerator DelayEvalFile() {
            yield return new WaitForEndOfFrame();
            _engine.EvalFile(entryFile);
        }

        void CheckForChanges() {
            var writeTime = File.GetLastWriteTime(_engine.GetFullPath(entryFile));
            if (_lastWriteTime == writeTime) return; // No change
            _lastWriteTime = writeTime;
            Reload();
            
            // _engine.OnReload += () => {
            //     _engine.JsEnv.UsingAction<bool>();
            //     // Add more here
            // };
        }

        void Respawn() {
            if (_janitor != null) {
                Destroy(_janitor.gameObject);
            }
            _janitor = new GameObject("Janitor").AddComponent<Janitor>();
            _janitor.clearGameObjects = clearGameObjects;
            _janitor.clearLogs = clearLogs;
        }

        void OnReload() {
            // Because OnDisable() order is non-deterministic, we need to check for gameObject.activeSelf
            // instead of depending on individual components.
            if (stopCleaningOnDisable && !this.gameObject.activeSelf)
                return;
            _janitor.Clean();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (respawnJanitorOnSceneLoad) {
                Respawn();
            }
        }
    }
}