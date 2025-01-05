using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    [CustomEditor(typeof(EditorScriptEngine))]
    public class EditorScriptEngineEditor : UnityEditor.Editor {
        SerializedProperty _folderName;
        SerializedProperty _scriptEnginePrefab;
        SerializedProperty _preloads;
        SerializedProperty _globalObjects;
        SerializedProperty _styleSheets;
        SerializedProperty _defaultFiles;
        SerializedProperty _dtsGenerator;
        SerializedProperty _devMode;
        SerializedProperty _extraLogging;
        SerializedProperty _initialized;

        void OnEnable() {
            _folderName = serializedObject.FindProperty("folderName");
            _scriptEnginePrefab = serializedObject.FindProperty("scriptEnginePrefab");
            _preloads = serializedObject.FindProperty("preloads");
            _globalObjects = serializedObject.FindProperty("globalObjects");
            _styleSheets = serializedObject.FindProperty("styleSheets");
            _defaultFiles = serializedObject.FindProperty("defaultFiles");
            _dtsGenerator = serializedObject.FindProperty("dtsGenerator");
            _devMode = serializedObject.FindProperty("devMode");
            _extraLogging = serializedObject.FindProperty("extraLogging");
            _initialized = serializedObject.FindProperty("initialized");
        }

        public override void OnInspectorGUI() {
            var editorScriptEngine = target as EditorScriptEngine;
            serializedObject.Update();
            if (_initialized.boolValue == false) {
                EditorGUILayout.HelpBox("Please use the Initialize button to set up your EditorScriptEngine's Working Directory for the first time.", MessageType.Info);
                EditorGUILayout.PropertyField(_folderName, new GUIContent("WorkingDir Name"));
                if (GUILayout.Button(new GUIContent("Initialize", "Initialize the Working Directory."))) {
                    InitWorkingDir();
                }
                serializedObject.ApplyModifiedProperties();
                return;
            }
            GUI.enabled = false;
            EditorGUILayout.PropertyField(_folderName, new GUIContent("Folder Name"));
            GUI.enabled = true;
            // EditorGUILayout.PropertyField(_scriptEnginePrefab, new GUIContent("ScriptEngine Prefab"));
            EditorGUILayout.PropertyField(_preloads, new GUIContent("Preloads"));
            EditorGUILayout.PropertyField(_globalObjects, new GUIContent("Globals"));
            EditorGUILayout.PropertyField(_styleSheets, new GUIContent("Stylesheets"));
            // EditorGUILayout.PropertyField(_defaultFiles, new GUIContent("Default Files"));
            EditorGUILayout.PropertyField(_dtsGenerator, new GUIContent("DTS Generator"), true);
            EditorGUILayout.PropertyField(_devMode, new GUIContent("Dev Mode"));
            
            if (_devMode.boolValue) {
                EditorGUILayout.PropertyField(_extraLogging, new GUIContent("      Extra Logging"));
            }

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            if (GUILayout.Button(new GUIContent("Open VSCode", "Opens the Working Directory with VSCode"), GUILayout.Height(30))) {
                OneJSEditorUtil.VSCodeOpenDir(editorScriptEngine.WorkingDir);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Working Dir", "Opens your JS Working Dir in Explorer or Finder"), GUILayout.Height(30))) {
                OneJSEditorUtil.OpenDir(editorScriptEngine.WorkingDir);
            }
            if (GUILayout.Button(new GUIContent("Project Dir", "Opens your Project Directory in Explorer or Finder"), GUILayout.Height(30))) {
                OneJSEditorUtil.OpenDir(Path.GetDirectoryName(Application.dataPath));
            }
            if (GUILayout.Button(new GUIContent("Persistent Path", "Opens the Persistent Data Path in Explorer or Finder"), GUILayout.Height(30))) {
                OneJSEditorUtil.OpenDir(Application.persistentDataPath);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

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
                    editor.Generate(editorScriptEngine.globalObjects, editorScriptEngine.WorkingDir);
                }
            }

            if (GUILayout.Button(new GUIContent("Reload", "Re-run the editor entry script"), GUILayout.ExpandHeight(true))) {
                editorScriptEngine.Reload();
            }

            GUILayout.EndHorizontal();
            serializedObject.ApplyModifiedProperties();
        }

        void InitWorkingDir() {
            var editorScriptEngine = target as EditorScriptEngine;
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), editorScriptEngine.folderName);
            var folderAlreadyExists = Directory.Exists(path);
            if (!folderAlreadyExists || EditorUtility.DisplayDialog($"WARNING: '{editorScriptEngine.folderName}' directory already exists",
                    "Do you want to recreate the existing directory? (WARNING: This will erase all current contents.) Or would you prefer to use the directory as it is?",
                    "Recreate", "Use Existing")) {
                if (DeleteEverythingInPath(path)) {
                    editorScriptEngine.Init();
                }
            }
            _initialized.boolValue = true;
            serializedObject.ApplyModifiedProperties();
        }
        
        /// <summary>
        /// Root folder at path still remains
        /// </summary>
        bool DeleteEverythingInPath(string path) {
            var dotGitPath = Path.Combine(path, ".git");
            if (Directory.Exists(dotGitPath)) {
                Debug.Log($".git folder detected at {path}, aborting deletion.");
                return false;
            }
            if (Directory.Exists(path)) {
                var di = new DirectoryInfo(path);
                foreach (FileInfo file in di.EnumerateFiles()) {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.EnumerateDirectories()) {
                    dir.Delete(true);
                }
            }
            return true;
        }
    }
}