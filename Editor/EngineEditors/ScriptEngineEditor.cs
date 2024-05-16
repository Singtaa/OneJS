using System.Reflection;
using OneJS;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    [CustomEditor(typeof(ScriptEngine))]
    [CanEditMultipleObjects]
    public class ScriptEngineEditor : UnityEditor.Editor {
        SerializedProperty _editorWorkingDirInfo;
        SerializedProperty _playerWorkingDirInfo;
        SerializedProperty _preloads;
        SerializedProperty _globalObjects;
        SerializedProperty _styleSheets;

        void OnEnable() {
            _editorWorkingDirInfo = serializedObject.FindProperty("editorWorkingDirInfo");
            _playerWorkingDirInfo = serializedObject.FindProperty("playerWorkingDirInfo");
            _preloads = serializedObject.FindProperty("preloads");
            _globalObjects = serializedObject.FindProperty("globalObjects");
            _styleSheets = serializedObject.FindProperty("styleSheets");
        }
        
        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_editorWorkingDirInfo, new GUIContent("Editor WorkingDir"));
            EditorGUILayout.PropertyField(_playerWorkingDirInfo, new GUIContent("Player WorkingDir"));
            EditorGUILayout.PropertyField(_preloads, new GUIContent("Preloads"));
            EditorGUILayout.PropertyField(_globalObjects, new GUIContent("Global Objects"));
            EditorGUILayout.PropertyField(_styleSheets, new GUIContent("Style Sheets"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
