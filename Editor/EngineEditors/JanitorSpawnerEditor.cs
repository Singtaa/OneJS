using OneJS.Engine;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    [CustomEditor(typeof(JanitorSpawner))]
    [CanEditMultipleObjects]
    public class JanitorSpawnerEditor : UnityEditor.Editor {
        SerializedProperty _clearGameObjects;
        SerializedProperty _clearLogs;
        SerializedProperty _respawnOnSceneLoad;

        void OnEnable() {
            _clearGameObjects = serializedObject.FindProperty("_clearGameObjects");
            _clearLogs = serializedObject.FindProperty("_clearLogs");
            _respawnOnSceneLoad = serializedObject.FindProperty("_respawnOnSceneLoad");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "Spawns a Janitor on Game Start that can clean up GameObjects and Logs upon engine reloads.",
                MessageType.None);
            EditorGUILayout.PropertyField(_clearGameObjects);
            EditorGUILayout.PropertyField(_clearLogs);
            EditorGUILayout.PropertyField(_respawnOnSceneLoad);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
