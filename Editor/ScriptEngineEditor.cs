// using System.Collections;
// using System.Collections.Generic;
// using OneJS;
// using UnityEditor;
// using UnityEngine;
//
// [CustomEditor(typeof(ScriptEngine))]
// [CanEditMultipleObjects]
// public class ScriptEngineInspector : Editor {
//     SerializedProperty _styleSheets;
//     SerializedProperty _assemblies;
//     SerializedProperty _extensions;
//     SerializedProperty _namespaces;
//     SerializedProperty _staticClasses;
//     SerializedProperty _objects;
//     SerializedProperty _allowReflection;
//
//     void OnEnable() {
//         _styleSheets = serializedObject.FindProperty("_styleSheets");
//         _assemblies = serializedObject.FindProperty("_assemblies");
//         _extensions = serializedObject.FindProperty("_extensions");
//         _namespaces = serializedObject.FindProperty("_namespaces");
//         _staticClasses = serializedObject.FindProperty("_staticClasses");
//         _objects = serializedObject.FindProperty("_objects");
//         _allowReflection = serializedObject.FindProperty("_allowReflection");
//         // _namespaces.isExpanded = false;
//     }
//
//     public override void OnInspectorGUI() {
//         // EditorGUILayout.HelpBox("Hello", MessageType.None);
//         // base.OnInspectorGUI();
//         serializedObject.Update();
//         // EditorGUILayout.HelpBox("Hello", MessageType.None);
//         // EditorGUILayout.PropertyField(_styleSheets);
//         // EditorGUILayout.PropertyField(_assemblies);
//         // EditorGUILayout.PropertyField(_extensions);
//         // EditorGUILayout.PropertyField(_namespaces);
//         // EditorGUILayout.PropertyField(_staticClasses);
//         // EditorGUILayout.PropertyField(_objects);
//         EditorGUILayout.PropertyField(_allowReflection);
//         serializedObject.ApplyModifiedProperties();
//     }
// }