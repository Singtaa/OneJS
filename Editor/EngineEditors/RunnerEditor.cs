using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    [CustomEditor(typeof(Runner))]
    [CanEditMultipleObjects]
    public class RunnerEditor : UnityEditor.Editor {
        static int _selectedTab;

        SerializedProperty _entryFile;
        SerializedProperty _runOnStart;
        SerializedProperty _liveReload;
        SerializedProperty _pollingInterval;
        SerializedProperty _clearGameObjects;
        SerializedProperty _clearLogs;
        SerializedProperty _respawnJanitorOnSceneLoad;
        SerializedProperty _stopCleaningOnDisable;
        SerializedProperty _standalone;


        void OnEnable() {
            _entryFile = serializedObject.FindProperty("entryFile");
            _runOnStart = serializedObject.FindProperty("runOnStart");
            _liveReload = serializedObject.FindProperty("liveReload");
            _pollingInterval = serializedObject.FindProperty("pollingInterval");
            _clearGameObjects = serializedObject.FindProperty("clearGameObjects");
            _clearLogs = serializedObject.FindProperty("clearLogs");
            _respawnJanitorOnSceneLoad = serializedObject.FindProperty("respawnJanitorOnSceneLoad");
            _stopCleaningOnDisable = serializedObject.FindProperty("stopCleaningOnDisable");
            _standalone = serializedObject.FindProperty("standalone");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            EditorGUILayout.HelpBox("Executes and optionally live-reloads an entry file, while managing scene-related cleanups and object lifecycles.", MessageType.None);

            EditorGUILayout.PropertyField(_entryFile, new GUIContent("Entry File"));
            EditorGUILayout.PropertyField(_runOnStart, new GUIContent("Run On Start"));
            EditorGUILayout.PropertyField(_liveReload, new GUIContent("Live Reload"));
            if (_liveReload.boolValue) {
                EditorGUILayout.PropertyField(_pollingInterval, new GUIContent("    Polling Interval (ms)"));
                EditorGUILayout.PropertyField(_clearGameObjects, new GUIContent("    Clear GameObjects on Reload"));
                EditorGUILayout.PropertyField(_clearLogs, new GUIContent("    Clear Logs on Reload"));
                EditorGUILayout.PropertyField(_respawnJanitorOnSceneLoad, new GUIContent("    Respawn Janitor on SceneLoad"));
                EditorGUILayout.PropertyField(_stopCleaningOnDisable, new GUIContent("    Stop Cleaning during OnDisable()"));
                EditorGUILayout.PropertyField(_standalone, new GUIContent("    Enable for Standalone"));
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}