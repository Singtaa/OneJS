using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    [CustomEditor(typeof(Initializer))]
    [CanEditMultipleObjects]
    public class InitializerEditor : UnityEditor.Editor {
        SerializedProperty _gitignore;
        SerializedProperty _tsconfig;
        SerializedProperty _esbuild;
        SerializedProperty _index;
        SerializedProperty _appDts;
        SerializedProperty _tailwindConfig;
        SerializedProperty _postcssConfig;
        SerializedProperty _readme;
        SerializedProperty _onejsCoreZip;
        SerializedProperty _outputsZip;

        SerializedProperty _version;
        SerializedProperty _forceExtract;
        SerializedProperty _ignoreList;

        bool showAssets;

        void OnEnable() {
            _gitignore = serializedObject.FindProperty("defaultGitIgnore");
            _tsconfig = serializedObject.FindProperty("defaultTsconfig");
            _esbuild = serializedObject.FindProperty("defaultEsbuild");
            _index = serializedObject.FindProperty("defaultIndex");
            _appDts = serializedObject.FindProperty("defaultAppDts");
            _tailwindConfig = serializedObject.FindProperty("tailwindConfig");
            _postcssConfig = serializedObject.FindProperty("postcssConfig");
            _readme = serializedObject.FindProperty("readme");
            _onejsCoreZip = serializedObject.FindProperty("onejsCoreZip");
            _outputsZip = serializedObject.FindProperty("outputsZip");

            _version = serializedObject.FindProperty("version");
            _forceExtract = serializedObject.FindProperty("forceExtract");
            _ignoreList = serializedObject.FindProperty("ignoreList");
        }

        public override void OnInspectorGUI() {
            var initializer = target as Initializer;
            serializedObject.Update();
            EditorGUILayout.HelpBox("Sets up OneJS for first-time use by creating essential files in the WorkingDir if they are missing. Also takes care of @outputs folder extraction for Standalone Player.", MessageType.None);

            showAssets = EditorGUILayout.Foldout(showAssets, "Default Assets", true);
            if (showAssets) {
                EditorGUILayout.PropertyField(_gitignore, new GUIContent("    .gitignore"));
                EditorGUILayout.PropertyField(_tsconfig, new GUIContent("    tsconfig.json"));
                EditorGUILayout.PropertyField(_esbuild, new GUIContent("    esbuild.mjs"));
                EditorGUILayout.PropertyField(_tailwindConfig, new GUIContent("    tailwind.config.js"));
                EditorGUILayout.PropertyField(_postcssConfig, new GUIContent("    postcss.config.js"));
                EditorGUILayout.PropertyField(_index, new GUIContent("    index.tsx"));
                EditorGUILayout.PropertyField(_appDts, new GUIContent("    app.d.ts"));
                EditorGUILayout.PropertyField(_readme, new GUIContent("    README.md"));
                GUILayout.Space(10);
                EditorGUILayout.PropertyField(_onejsCoreZip, new GUIContent("    onejs-core.tgz"));
                EditorGUILayout.PropertyField(_outputsZip, new GUIContent("    outputs.tgz"));
                GUILayout.Space(10);
            }

            EditorGUILayout.PropertyField(_version, new GUIContent("Version"));
            EditorGUILayout.PropertyField(_forceExtract, new GUIContent("Force Extract"));
            EditorGUILayout.PropertyField(_ignoreList, new GUIContent("Ignore List"));

            GUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("Package outputs.tgz", "Packages the @outputs folder into outputs.tgz."), GUILayout.Height(30))) {
                initializer.PackageOutputsZipWithPrompt();
            }

            GUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }
    }
}