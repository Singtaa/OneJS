using System;
using NaughtyAttributes;
using UnityEngine;

namespace OneJS.Engine {
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(ScriptEngine))]
    public class JanitorSpawner : MonoBehaviour {
        [InfoBox("Spawns a Janitor on Game Start that can clean up GameObjects and Logs upon engine reloads.")]
        [Tooltip("Turn this on to also have the Janitor to clear console logs on every Script Reload.")]
        [SerializeField] bool _clearGameObjects;
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