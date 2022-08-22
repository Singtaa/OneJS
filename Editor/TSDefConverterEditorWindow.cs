using OneJS.Utils;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    public class TSDefConverterEditorWindow : EditorWindow {
        [SerializeField] string _typeName;
        [SerializeField] string _defstr;
        [SerializeField] Vector2 _scrollPos;


        [MenuItem("OneJS/C# to TSDef Converter")]
        private static void ShowWindow() {
            var window = GetWindow<TSDefConverterEditorWindow>();
            window.titleContent = new GUIContent("C# to TSDef Converter");
            window.Show();
        }

        private void OnGUI() {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Fully Qualified Type Name:");
            _typeName = GUILayout.TextField(_typeName);
            if (GUILayout.Button("Convert")) {
                var type = AssemblyFinder.FindType(_typeName);
                if (type == null) {
                    Debug.LogError($"Type {_typeName} not found.");
                    return;
                }
                var converter = new TSDefConverter(type);
                _defstr = converter.Convert();
            }
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Result:");
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(300));
            GUIStyle myTextAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = false };
            GUILayout.TextArea(_defstr, myTextAreaStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Copy to Clipboard")) {
                GUIUtility.systemCopyBuffer = _defstr;
            }
        }
    }
}