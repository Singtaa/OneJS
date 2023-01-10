using OneJS.Utils;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    public class TSDefConverterEditorWindow : EditorWindow {
        [SerializeField] string _typeName;
        [SerializeField] bool _jintSyntaxForEvents = true;
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
            EditorGUILayout.HelpBox("Generated typings can be verbose at times. Feel free to remove stuff you don't need. Remember, TS type definitions are just for compile-time type checking. They are nice to have, but not required.", MessageType.Info);
            EditorGUILayout.LabelField("Fully Qualified Type Name:");
            _typeName = GUILayout.TextField(_typeName);
            _jintSyntaxForEvents = EditorGUILayout.Toggle("Use Jint syntax for events", _jintSyntaxForEvents);
            if (GUILayout.Button("Convert")) {
                var type = AssemblyFinder.FindType(_typeName);
                if (type == null) {
                    Debug.LogError($"Type {_typeName} not found.");
                    return;
                }
                var converter = new TSDefConverter(type, _jintSyntaxForEvents);
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