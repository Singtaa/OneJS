using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using OneJS;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace OneJS.Editor {
    [CustomEditor(typeof(ScriptEngine))]
    [CanEditMultipleObjects]
    public class ScriptEngineEditor : UnityEditor.Editor {
        SerializedProperty _editorWorkingDirInfo;
        SerializedProperty _playerWorkingDirInfo;
        SerializedProperty _preloads;
        SerializedProperty _globalObjects;
        SerializedProperty _styleSheets;
        SerializedProperty _dtsGenerator;
        SerializedProperty _debuggerSupport;
        SerializedProperty _basePath;
        SerializedProperty _debuggerPort;
        SerializedProperty _miscSettings;

        void OnEnable() {
            _editorWorkingDirInfo = serializedObject.FindProperty("editorWorkingDirInfo");
            _playerWorkingDirInfo = serializedObject.FindProperty("playerWorkingDirInfo");
            _preloads = serializedObject.FindProperty("preloads");
            _globalObjects = serializedObject.FindProperty("globalObjects");
            _styleSheets = serializedObject.FindProperty("styleSheets");
            _dtsGenerator = serializedObject.FindProperty("dtsGenerator");
            _debuggerSupport = serializedObject.FindProperty("debuggerSupport");
            _basePath = serializedObject.FindProperty("basePath");
            _debuggerPort = serializedObject.FindProperty("port");
            _miscSettings = serializedObject.FindProperty("miscSettings");
        }

        public override void OnInspectorGUI() {
            var scriptEngine = target as ScriptEngine;
            serializedObject.Update();

            EditorGUILayout.PropertyField(_editorWorkingDirInfo, new GUIContent("Editor WorkingDir"));
            EditorGUILayout.PropertyField(_playerWorkingDirInfo, new GUIContent("Player WorkingDir"));
            EditorGUILayout.PropertyField(_preloads, new GUIContent("Preloads"));
            EditorGUILayout.PropertyField(_globalObjects, new GUIContent("Globals"));
            EditorGUILayout.PropertyField(_styleSheets, new GUIContent("Stylesheets"));
            EditorGUILayout.PropertyField(_dtsGenerator, new GUIContent("DTS Generator"), true);
            EditorGUILayout.PropertyField(_debuggerSupport, new GUIContent("Debugger Support"));
            if (_debuggerSupport.boolValue) {
                EditorGUILayout.PropertyField(_basePath, new GUIContent("    Base Path"));
                EditorGUILayout.PropertyField(_debuggerPort, new GUIContent("    Debugger Port"));
            }
            EditorGUILayout.PropertyField(_miscSettings, new GUIContent("Misc Settings"), true);
            
            EditorGUILayout.Space(10);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            if (GUILayout.Button(new GUIContent("Open VSCode", "Opens the Working Directory with VSCode"), GUILayout.Height(30))) {
                OneJSEditorUtil.VSCodeOpenDir(scriptEngine.WorkingDir);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Working Dir", "Opens your JS Working Dir in Explorer or Finder"), GUILayout.Height(30))) {
                OneJSEditorUtil.OpenDir(scriptEngine.WorkingDir);
            }
            if (GUILayout.Button(new GUIContent("Project Dir", "Opens the Project Directory in Explorer or Finder"), GUILayout.Height(30))) {
                OneJSEditorUtil.OpenDir(Path.GetDirectoryName(Application.dataPath));
            }
            if (GUILayout.Button(new GUIContent("Persistent Path", "Opens the PersistentDataPath in Explorer or Finder"), GUILayout.Height(30))) {
                OneJSEditorUtil.OpenDir(Application.persistentDataPath);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // var generator = scriptEngine.GetComponent<DTSGenerator>();
            // if (generator != null && GUILayout.Button(new GUIContent("Generate DTS", "Generate TS Definitions. Configurations are on the DTS Generator component beblow."), GUILayout.ExpandHeight(true))) {
            //     var editor = CreateEditor(generator);
            //     if (editor is DTSGeneratorDrawer generatorEditor) {
            //         generatorEditor.Generate();
            //     }
            // }

            if (GUILayout.Button(new GUIContent("Generate DTS", "Generate TS Definitions."), GUILayout.ExpandHeight(true))) {
                if (EditorUtility.DisplayDialog("TS Definitions Generation",
                        "This may take a few minutes depending on the number of types. Are you sure you want to proceed right now?",
                        "Confirm", "Cancel")) {
#if UNITY_2022_1_OR_NEWER
                    var genny = (DTSGenerator)_dtsGenerator.boxedValue;
#else
                    var fieldInfo = _dtsGenerator.serializedObject.targetObject.GetType().GetField(_dtsGenerator.propertyPath, BindingFlags.Instance | BindingFlags.Public);
                    var genny = fieldInfo.GetValue(serializedObject.targetObject) as DTSGenerator;
#endif
                    var editor = new DTSGeneratorEditor(genny);
                    editor.Generate(scriptEngine.globalObjects, scriptEngine.WorkingDir);
                }
            }

            var runner = scriptEngine.GetComponent<Runner>();
            if (Application.isPlaying && runner != null && GUILayout.Button(new GUIContent("Reload", "Forcibly Reload the Runner."), GUILayout.ExpandHeight(true))) {
                runner.Reload();
            }

            GUILayout.EndHorizontal();
            serializedObject.ApplyModifiedProperties();
        }
    }
}