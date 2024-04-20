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
        SerializedProperty _tailwindConfig;
        SerializedProperty _postcssConfig;
        SerializedProperty _readme;
        SerializedProperty _onejsCoreZip;
        SerializedProperty _outputsZip;

        bool showAssets;

        void OnEnable() {
            _tsconfig = serializedObject.FindProperty("defaultTsconfig");
            _esbuild = serializedObject.FindProperty("defaultEsbuild");
            _index = serializedObject.FindProperty("defaultIndex");
            _tailwindConfig = serializedObject.FindProperty("tailwindConfig");
            _postcssConfig = serializedObject.FindProperty("postcssConfig");
            _readme = serializedObject.FindProperty("readme");
            _onejsCoreZip = serializedObject.FindProperty("onejsCoreZip");
            _outputsZip = serializedObject.FindProperty("outputsZip");
        }

        public override void OnInspectorGUI() {
            var initializer = target as Initializer;
            serializedObject.Update();
            EditorGUILayout.HelpBox("This component sets up OneJS for first time use. It creates essential files in the WorkingDir if they are missing.", MessageType.None);

            showAssets = EditorGUILayout.Foldout(showAssets, "Default Assets", true);
            if (showAssets) {
                EditorGUILayout.PropertyField(_tsconfig, new GUIContent("tsconfig.json"));
                EditorGUILayout.PropertyField(_esbuild, new GUIContent("esbuild.mjs"));
                EditorGUILayout.PropertyField(_tailwindConfig, new GUIContent("tailwind.config.js"));
                EditorGUILayout.PropertyField(_postcssConfig, new GUIContent("postcss.config.js"));
                EditorGUILayout.PropertyField(_index, new GUIContent("index.tsx"));
                EditorGUILayout.PropertyField(_readme, new GUIContent("README.md"));
                EditorGUILayout.PropertyField(_onejsCoreZip, new GUIContent("onejs-core.tgz"));
                EditorGUILayout.PropertyField(_outputsZip, new GUIContent("outputs.tgz"));
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}