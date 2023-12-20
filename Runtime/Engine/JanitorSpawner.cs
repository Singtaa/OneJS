using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace OneJS.Engine {
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(ScriptEngine))]
    public class JanitorSpawner : MonoBehaviour {
        public Janitor Janitor => _janitor;

        [Tooltip("Clean up spawned GameObjects on every ScriptEngine reload.")]
        [SerializeField] bool _clearGameObjects = true;
        [Tooltip("Clear console logs on every ScriptEngine reload.")]
        [SerializeField] bool _clearLogs = true;
        [Tooltip("Respawn the Janitor during scene loads so that it doesn't clean up your additively loaded scenes.")]
        [SerializeField] bool _respawnOnSceneLoad = true;
        [Tooltip("Don't clean up on OnDisable().")]
        [SerializeField] bool _stopCleaningOnDisable;

        ScriptEngine _scriptEngine;
        Janitor _janitor;

        void Awake() {
            _scriptEngine = GetComponent<ScriptEngine>();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy() {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnEnable() {
            Respawn();
            _scriptEngine.OnReload += OnReload;
        }

        void OnDisable() {
            _scriptEngine.OnReload -= OnReload;
        }

        public void Respawn() {
            if (_janitor != null) {
                Destroy(_janitor.gameObject);
            }
            _janitor = new GameObject("Janitor").AddComponent<Janitor>();
            _janitor.clearGameObjects = _clearGameObjects;
            _janitor.clearLogs = _clearLogs;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (_respawnOnSceneLoad) {
                Respawn();
            }
        }

        void OnReload() {
            // Because OnDisable() order is non-deterministic, we need to check for gameObject.activeSelf
            // instead of depending on individual components.
            if (_stopCleaningOnDisable && !this.gameObject.activeSelf)
                return;
            _janitor.Clean();
        }
    }
}