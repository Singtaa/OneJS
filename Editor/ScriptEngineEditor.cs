using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using OneJS;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    [CustomEditor(typeof(ScriptEngine))]
    [CanEditMultipleObjects]
    public class ScriptEngineEditor : UnityEditor.Editor {
        static int _selectedTab;

        SerializedProperty _assemblies;
        SerializedProperty _extensions;
        SerializedProperty _namespaces;
        SerializedProperty _staticClasses;
        SerializedProperty _objects;
        SerializedProperty _preloadedScripts;
        SerializedProperty _postloadedScripts;

        SerializedProperty _catchDotNetExceptions;
        SerializedProperty _logRedundantErrors;
        SerializedProperty _allowReflection;
        SerializedProperty _allowGetType;
        SerializedProperty _memoryLimit;
        SerializedProperty _timeout;
        SerializedProperty _recursionDepth;
        SerializedProperty _maxExecutionStackCount;

        SerializedProperty _styleSheets;
        SerializedProperty _breakpoints;

        SerializedProperty _editorModeWorkingDirInfo;
        SerializedProperty _playerModeWorkingDirInfo;

        SerializedProperty _pathMappings;
        SerializedProperty _setDontDestroyOnLoad;
        SerializedProperty _initEngineOnStart;
        SerializedProperty _enableExtraLogging;


        void OnEnable() {
            _assemblies = serializedObject.FindProperty("_assemblies");
            _extensions = serializedObject.FindProperty("_extensions");
            _namespaces = serializedObject.FindProperty("_namespaces");
            _staticClasses = serializedObject.FindProperty("_staticClasses");
            _objects = serializedObject.FindProperty("_objects");
            _preloadedScripts = serializedObject.FindProperty("_preloadedScripts");
            _postloadedScripts = serializedObject.FindProperty("_postloadedScripts");

            _catchDotNetExceptions = serializedObject.FindProperty("_catchDotNetExceptions");
            _logRedundantErrors = serializedObject.FindProperty("_logRedundantErrors");
            _allowReflection = serializedObject.FindProperty("_allowReflection");
            _allowGetType = serializedObject.FindProperty("_allowGetType");
            _memoryLimit = serializedObject.FindProperty("_memoryLimit");
            _timeout = serializedObject.FindProperty("_timeout");
            _recursionDepth = serializedObject.FindProperty("_recursionDepth");
            _maxExecutionStackCount = serializedObject.FindProperty("_maxExecutionStackCount");

            _styleSheets = serializedObject.FindProperty("_styleSheets");
            _breakpoints = serializedObject.FindProperty("_breakpoints");

            _editorModeWorkingDirInfo = serializedObject.FindProperty("_editorModeWorkingDirInfo");
            _playerModeWorkingDirInfo = serializedObject.FindProperty("_playerModeWorkingDirInfo");

            _pathMappings = serializedObject.FindProperty("_pathMappings");
            _setDontDestroyOnLoad = serializedObject.FindProperty("_setDontDestroyOnLoad");
            _initEngineOnStart = serializedObject.FindProperty("_initEngineOnStart");
            _enableExtraLogging = serializedObject.FindProperty("_enableExtraLogging");
        }

        public override void OnInspectorGUI() {
            var scriptEngine = target as ScriptEngine;
            serializedObject.Update();
            _selectedTab = GUILayout.Toolbar(_selectedTab, new GUIContent[] {
                new GUIContent("INTEROP", ".Net to JS Interop Settings"),
                new GUIContent("SECURITY", "Security Settings"),
                new GUIContent("STYLING", "Base styling settings"),
                new GUIContent("MISC", "Miscellaneous Settings for OneJS ScriptEngine"),
            }, GUILayout.Height(30));

            GUILayout.Space(8);

            switch (_selectedTab) {
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
                    EditorGUILayout.PropertyField(_logRedundantErrors);
                    EditorGUILayout.PropertyField(_allowReflection);
                    EditorGUILayout.PropertyField(_allowGetType, new GUIContent("Allow GetType()"));
                    EditorGUILayout.PropertyField(_memoryLimit);
                    EditorGUILayout.PropertyField(_timeout);
                    EditorGUILayout.PropertyField(_recursionDepth);
                    EditorGUILayout.PropertyField(_maxExecutionStackCount);
                    break;
                case 2:
                    EditorGUILayout.PropertyField(_styleSheets);
                    EditorGUILayout.PropertyField(_breakpoints);
                    break;
                case 3:
                    var fa = _editorModeWorkingDirInfo.serializedObject.targetObject.GetType()
                        .GetField(_editorModeWorkingDirInfo.propertyPath,
                            BindingFlags.Instance | BindingFlags.NonPublic);
                    var va = fa.GetValue(_editorModeWorkingDirInfo.serializedObject.targetObject);
                    var fb = _playerModeWorkingDirInfo.serializedObject.targetObject.GetType()
                        .GetField(_playerModeWorkingDirInfo.propertyPath,
                            BindingFlags.Instance | BindingFlags.NonPublic);
                    var vb = fb.GetValue(_playerModeWorkingDirInfo.serializedObject.targetObject);
                    if (va is EditorModeWorkingDirInfo a &&
                        vb is PlayerModeWorkingDirInfo b &&
                        a.baseDir == EditorModeWorkingDirInfo.EditorModeBaseDir.PersistentDataPath &&
                        b.baseDir == PlayerModeWorkingDirInfo.PlayerModeBaseDir.PersistentDataPath &&
                        a.relativePath == b.relativePath) {
                        EditorGUILayout.HelpBox(
                            "The Editor and Player mode working directories are the same. This is not recommended. You should set them to different directories.",
                            MessageType.Warning);
                    }
                    // // .boxedValue is only available in Unity 2022.1+
                    // var a = _editorModeWorkingDirInfo.boxedValue as EditorModeWorkingDirInfo;
                    // var b = _playerModeWorkingDirInfo.boxedValue as PlayerModeWorkingDirInfo;
                    // if (a.baseDir == EditorModeWorkingDirInfo.EditorModeBaseDir.PersistentDataPath &&
                    //     b.baseDir == PlayerModeWorkingDirInfo.PlayerModeBaseDir.PersistentDataPath &&
                    //     a.relativePath == b.relativePath) {
                    //     EditorGUILayout.HelpBox(
                    //         "The Editor and Player mode working directories are the same. This is not recommended. You should set them to different directories.",
                    //         MessageType.Warning);
                    // }
                    EditorGUILayout.PropertyField(_editorModeWorkingDirInfo);
                    EditorGUILayout.PropertyField(_playerModeWorkingDirInfo);
                    EditorGUILayout.PropertyField(_preloadedScripts);
                    EditorGUILayout.PropertyField(_postloadedScripts);
                    EditorGUILayout.PropertyField(_pathMappings);
                    EditorGUILayout.PropertyField(_setDontDestroyOnLoad);
                    EditorGUILayout.PropertyField(_initEngineOnStart);
                    EditorGUILayout.PropertyField(_enableExtraLogging);
                    break;
                default:
                    break;
            }
            EditorGUILayout.Space(12);
            if (GUILayout.Button(new GUIContent("Open VSCode", "Opens the Working Directory with VSCode"), GUILayout.Height(30))) {
                VSCodeOpenDir(scriptEngine.WorkingDir);
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

        public static void VSCodeOpenDir(string path) {
#if UNITY_STANDALONE_WIN
            var processName = GetCodeExecutablePathOnWindows();
#elif UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            var processName = GetCodeExecutablePathOnUnix();
#else
            var processName = "unknown";
            UnityEngine.Debug.LogWarning("Unknown platform. Cannot open VSCode folder");
            return;
#endif
            var argStr = $"\"{Path.GetFullPath(path)}\"";
            var proc = new Process() {
                StartInfo = new ProcessStartInfo() {
                    FileName = processName,
                    Arguments = argStr,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
            };
            proc.Start();
        }

        static string GetCodeExecutablePathOnWindows() {
            string[] possiblePaths = new string[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft VS Code\code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft VS Code\code.exe")
            };

            foreach (var path in possiblePaths) {
                if (File.Exists(path)) {
                    return path;
                }
            }

            // Additional search in PATH environment variable
            string pathEnvironmentVariable = Environment.GetEnvironmentVariable("PATH");
            if (pathEnvironmentVariable != null) {
                foreach (var path in pathEnvironmentVariable.Split(Path.PathSeparator)) {
                    string fullPath = Path.Combine(path, "code.exe");
                    if (File.Exists(fullPath)) {
                        return fullPath;
                    }
                }
            }

            return null;
        }

        static string GetCodeExecutablePathOnUnix() {
            string[] possiblePaths = new string[] {
                "/usr/local/bin/code",
                "/usr/bin/code",
                "/snap/bin/code"
            };

            foreach (var path in possiblePaths) {
                if (File.Exists(path)) {
                    return path;
                }
            }

            // Additional search in PATH environment variable
            string pathEnvironmentVariable = Environment.GetEnvironmentVariable("PATH");
            if (pathEnvironmentVariable != null) {
                foreach (var path in pathEnvironmentVariable.Split(Path.PathSeparator)) {
                    string fullPath = Path.Combine(path, "code");
                    if (File.Exists(fullPath)) {
                        return fullPath;
                    }
                }
            }

            return null;
        }
    }
}