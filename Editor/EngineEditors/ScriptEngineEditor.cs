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
            var scriptEngine = target as ScriptEngine;
            serializedObject.Update();
            EditorGUILayout.PropertyField(_editorWorkingDirInfo, new GUIContent("Editor WorkingDir"));
            EditorGUILayout.PropertyField(_playerWorkingDirInfo, new GUIContent("Player WorkingDir"));
            EditorGUILayout.PropertyField(_preloads, new GUIContent("Preloads"));
            EditorGUILayout.PropertyField(_globalObjects, new GUIContent("Global Objects"));
            EditorGUILayout.PropertyField(_styleSheets, new GUIContent("Style Sheets"));
            if (GUILayout.Button(new GUIContent("Open VSCode", "Opens the Working Directory with VSCode"), GUILayout.Height(30))) {
                VSCodeOpenDir(scriptEngine.WorkingDir);
            }
            serializedObject.ApplyModifiedProperties();
        }

        public static void VSCodeOpenDir(string path) {
#if UNITY_STANDALONE_WIN
            var processName = GetCodeExecutablePathOnWindows();
#elif UNITY_STANDALONE_OSX
            var processName = "/usr/local/bin/code";
#elif UNITY_STANDALONE_LINUX
            var processName = "vscode";
#else
            var processName = "unknown";
            Debug.LogWarning("Unknown platform. Cannot open VSCode folder");
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
    }
}