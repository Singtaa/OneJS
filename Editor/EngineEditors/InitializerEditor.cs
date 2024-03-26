using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    [CustomEditor(typeof(Initializer))]
    [CanEditMultipleObjects]
    public class InitializerEditor : UnityEditor.Editor {
        SerializedProperty _tsconfig;
        SerializedProperty _esbuild;
        SerializedProperty _index;
        SerializedProperty _onejsCoreZip;
        SerializedProperty _outputsZip;
        SerializedProperty _readme;

        bool showAssets;

        void OnEnable() {
            _tsconfig = serializedObject.FindProperty("defaultTsconfig");
            _esbuild = serializedObject.FindProperty("defaultEsbuild");
            _index = serializedObject.FindProperty("defaultIndex");
            _onejsCoreZip = serializedObject.FindProperty("onejsCoreZip");
            _outputsZip = serializedObject.FindProperty("outputsZip");
            _readme = serializedObject.FindProperty("readme");
        }

        public override void OnInspectorGUI() {
            var initializer = target as Initializer;
            serializedObject.Update();
            showAssets = EditorGUILayout.Foldout(showAssets, "ASSETS", true);
            if (showAssets) {
                EditorGUILayout.PropertyField(_tsconfig, new GUIContent("tsconfig.json"));
                EditorGUILayout.PropertyField(_esbuild, new GUIContent("esbuild.mjs"));
                EditorGUILayout.PropertyField(_index, new GUIContent("index.tsx"));
                EditorGUILayout.PropertyField(_onejsCoreZip, new GUIContent("onejs-core.tgz"));
                EditorGUILayout.PropertyField(_outputsZip, new GUIContent("outputs.tgz"));
                EditorGUILayout.PropertyField(_readme, new GUIContent("README.md"));
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}