using System;
using UnityEngine;

namespace OneJS.Engine {
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(ScriptEngine))]
    public class JanitorSpawner : MonoBehaviour {
        public Janitor Janitor => _janitor;

        [Tooltip("Clean up spawned GameObjects on every ScriptEngine reload.")]
        [SerializeField] bool _clearGameObjects;
        [Tooltip("Clear console logs on every ScriptEngine reload.")]
        [SerializeField] bool _clearLogs;

        ScriptEngine _scriptEngine;
        Janitor _janitor;

        void Awake() {
            _scriptEngine = GetComponent<ScriptEngine>();
            _janitor = new GameObject("Janitor").AddComponent<Janitor>();
            _janitor.clearGameObjects = _clearGameObjects;
            _janitor.clearLogs = _clearLogs;
        }

        void OnEnable() {
            _scriptEngine.OnReload += OnReload;
        }

        void OnDisable() {
            _scriptEngine.OnReload -= OnReload;
        }

        void Start() {
        }

        void OnReload() {
            _janitor.Clean();
        }
    }
}