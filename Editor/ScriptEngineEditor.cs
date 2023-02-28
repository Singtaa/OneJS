using System.Collections;
using System.Collections.Generic;
using OneJS;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScriptEngine))]
[CanEditMultipleObjects]
public class ScriptEngineEditor : Editor {
    SerializedProperty _assemblies;
    SerializedProperty _extensions;
    SerializedProperty _namespaces;
    SerializedProperty _staticClasses;
    SerializedProperty _objects;
    SerializedProperty _preloadedScripts;
    SerializedProperty _postloadedScripts;

    SerializedProperty _catchDotNetExceptions;
    SerializedProperty _allowReflection;
    SerializedProperty _allowGetType;
    SerializedProperty _memoryLimit;
    SerializedProperty _timeout;
    SerializedProperty _recursionDepth;

    SerializedProperty _styleSheets;
    SerializedProperty _breakpoints;

    SerializedProperty _editorModeWorkingDirInfo;
    SerializedProperty _playerModeWorkingDirInfo;

    SerializedProperty _selectedTab;

    void OnEnable() {
        _selectedTab = serializedObject.FindProperty("_selectedTab");

        _assemblies = serializedObject.FindProperty("_assemblies");
        _extensions = serializedObject.FindProperty("_extensions");
        _namespaces = serializedObject.FindProperty("_namespaces");
        _staticClasses = serializedObject.FindProperty("_staticClasses");
        _objects = serializedObject.FindProperty("_objects");
        _preloadedScripts = serializedObject.FindProperty("_preloadedScripts");
        _postloadedScripts = serializedObject.FindProperty("_postloadedScripts");

        _catchDotNetExceptions = serializedObject.FindProperty("_catchDotNetExceptions");
        _allowReflection = serializedObject.FindProperty("_allowReflection");
        _allowGetType = serializedObject.FindProperty("_allowGetType");
        _memoryLimit = serializedObject.FindProperty("_memoryLimit");
        _timeout = serializedObject.FindProperty("_timeout");
        _recursionDepth = serializedObject.FindProperty("_recursionDepth");

        _styleSheets = serializedObject.FindProperty("_styleSheets");
        _breakpoints = serializedObject.FindProperty("_breakpoints");

        _editorModeWorkingDirInfo = serializedObject.FindProperty("_editorModeWorkingDirInfo");
        _playerModeWorkingDirInfo = serializedObject.FindProperty("_playerModeWorkingDirInfo");
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();
        _selectedTab.intValue = GUILayout.Toolbar(_selectedTab.intValue, new GUIContent[] {
            new GUIContent("INTEROP", ".Net to JS Interop Settings"),
            new GUIContent("SECURITY", "Security Settings"),
            new GUIContent("STYLING", "Base styling settings"),
            new GUIContent("MISC", "Miscellaneous Settings for OneJS ScriptEngine"),
        }, GUILayout.Height(30));

        GUILayout.Space(8);

        switch (_selectedTab.intValue) {
            case 0:
                EditorGUILayout.HelpBox(
                    "The Objects list accepts any UnityEngine.Object, not just MonoBehaviours. To pick a specific MonoBehaviour component, you can right-click on the Inspector Tab of the selected GameObject and pick Properties. A standalone window will pop up for you to drag the specifc MonoBehavior from.",
                    MessageType.None);
                EditorGUILayout.PropertyField(_objects);
                EditorGUILayout.PropertyField(_assemblies);
                EditorGUILayout.PropertyField(_namespaces);
                EditorGUILayout.PropertyField(_extensions);
                EditorGUILayout.PropertyField(_staticClasses);
                break;
            case 1:
                EditorGUILayout.PropertyField(_catchDotNetExceptions, new GUIContent("Catch .Net Exceptions"));
                EditorGUILayout.PropertyField(_allowReflection);
                EditorGUILayout.PropertyField(_allowGetType, new GUIContent("Allow GetType()"));
                EditorGUILayout.PropertyField(_memoryLimit);
                EditorGUILayout.PropertyField(_timeout);
                EditorGUILayout.PropertyField(_recursionDepth);
                break;
            case 2:
                EditorGUILayout.PropertyField(_styleSheets);
                EditorGUILayout.PropertyField(_breakpoints);
                break;
            case 3:
                var a = _editorModeWorkingDirInfo.boxedValue as EditorModeWorkingDirInfo;
                var b = _playerModeWorkingDirInfo.boxedValue as PlayerModeWorkingDirInfo;
                if (a.baseDir == EditorModeWorkingDirInfo.EditorModeBaseDir.PersistentDataPath &&
                    b.baseDir == PlayerModeWorkingDirInfo.PlayerModeBaseDir.PersistentDataPath &&
                    a.relativePath == b.relativePath) {
                    EditorGUILayout.HelpBox(
                        "The Editor and Player mode working directories are the same. This is not recommended. You should set them to different directories.",
                        MessageType.Warning);
                }
                EditorGUILayout.PropertyField(_editorModeWorkingDirInfo);
                EditorGUILayout.PropertyField(_playerModeWorkingDirInfo);
                EditorGUILayout.PropertyField(_preloadedScripts);
                EditorGUILayout.PropertyField(_postloadedScripts);
                break;
            default:
                break;
        }
        // EditorGUILayout.HelpBox("Hello", MessageType.None);
        // base.OnInspectorGUI();
        // EditorGUILayout.HelpBox("Hello", MessageType.None);
        // EditorGUILayout.PropertyField(_styleSheets);
        // EditorGUILayout.PropertyField(_assemblies);
        // EditorGUILayout.PropertyField(_extensions);
        // EditorGUILayout.PropertyField(_namespaces);
        // EditorGUILayout.PropertyField(_staticClasses);
        // EditorGUILayout.PropertyField(_objects);
        serializedObject.ApplyModifiedProperties();
    }
}