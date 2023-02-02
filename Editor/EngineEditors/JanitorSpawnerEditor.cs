using OneJS.Engine;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(JanitorSpawner))]
[CanEditMultipleObjects]
public class JanitorSpawnerEditor : Editor {
    SerializedProperty _clearGameObjects;
    SerializedProperty _clearLogs;

    void OnEnable() {
        _clearGameObjects = serializedObject.FindProperty("_clearGameObjects");
        _clearLogs = serializedObject.FindProperty("_clearLogs");
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();
        EditorGUILayout.HelpBox(
            "Spawns a Janitor on Game Start that can clean up GameObjects and Logs upon engine reloads.",
            MessageType.None);
        EditorGUILayout.PropertyField(_clearGameObjects);
        EditorGUILayout.PropertyField(_clearLogs);
        serializedObject.ApplyModifiedProperties();
    }
}