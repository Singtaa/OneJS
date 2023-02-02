using System;
using OneJS;
using OneJS.Engine;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LiveReload))]
[CanEditMultipleObjects]
public class LiveReloadEditor : Editor {
    SerializedProperty _runOnStart;
    SerializedProperty _turnOnForStandalone;
    SerializedProperty _entryScript;
    SerializedProperty _watchFilter;

    SerializedProperty _netSync;
    SerializedProperty _mode;
    SerializedProperty _port;
    SerializedProperty _serverIP;
    SerializedProperty _useRandomPortForClient;

    void OnEnable() {
        _runOnStart = serializedObject.FindProperty("_runOnStart");
        _turnOnForStandalone = serializedObject.FindProperty("_turnOnForStandalone");
        _entryScript = serializedObject.FindProperty("_entryScript");
        _watchFilter = serializedObject.FindProperty("_watchFilter");

        _netSync = serializedObject.FindProperty("_netSync");
        _mode = serializedObject.FindProperty("_mode");
        _port = serializedObject.FindProperty("_port");
        _serverIP = serializedObject.FindProperty("_serverIP");
        _useRandomPortForClient = serializedObject.FindProperty("_useRandomPortForClient");
    }


    public override void OnInspectorGUI() {
        serializedObject.Update();
        
        EditorGUILayout.HelpBox("This component will watch the Working Directory for you. When change is detected, " +
                                "the script engine will reload and the entry script will be re-run.", MessageType.None);

        EditorGUILayout.PropertyField(_runOnStart);
        EditorGUILayout.PropertyField(_turnOnForStandalone);
        EditorGUILayout.PropertyField(_entryScript);
        EditorGUILayout.PropertyField(_watchFilter);

        EditorGUILayout.PropertyField(_netSync);
        if (_netSync.boolValue) {
            EditorGUILayout.PropertyField(_mode);
            EditorGUILayout.PropertyField(_port);
            if (_mode.enumValueIndex == (int)ENetSyncMode.Client) {
                EditorGUILayout.PropertyField(_serverIP);
                EditorGUILayout.PropertyField(_useRandomPortForClient);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}