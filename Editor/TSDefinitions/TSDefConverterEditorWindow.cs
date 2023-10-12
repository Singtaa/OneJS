using System;
using System.Diagnostics;
using OneJS.Editor;
using OneJS.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor.TSDefinitions {
    public class TSDefConverterEditorWindow : EditorWindow {
        [SerializeField]
        private string _typeName;

        [SerializeField]
        private bool _jintSyntaxForEvents = true;

        [SerializeField]
        private bool _includeBaseMembers = true;

        [SerializeField]
        private bool _includeDeclare = true;

        [SerializeField]
        private bool _extractBaseDefinitions = false;

        [SerializeField]
        private bool _excludeUnityBaseTypes = false;

        [SerializeField]
        private string _outputStr;

        private TextField _outputField;

        private VisualElement nsQuestionContainer;
        private Action<bool> nsQuestionCallback;

        [MenuItem("Tools/OneJS/C# to TSDef Converter")]
        private static void ShowWindow() {
            var window = GetWindow<TSDefConverterEditorWindow>();
            window.titleContent = new GUIContent("C# to TSDef Converter");
            window.Show();
        }

        void CreateGUI() {
            var root = new VisualElement {
                style = {
                    width = new StyleLength(Length.Percent(100)),
                    height = new StyleLength(Length.Percent(100)),
                }
            };

            var scrollContainer = new ScrollView(ScrollViewMode.Vertical);
            scrollContainer.contentContainer.style.flexGrow = 1;
            scrollContainer.contentContainer.style.paddingBottom = 10;
            scrollContainer.contentContainer.style.paddingLeft = 10;
            scrollContainer.contentContainer.style.paddingRight = 10;
            scrollContainer.contentContainer.style.paddingTop = 10;
            root.Add(scrollContainer);

            var container = scrollContainer.contentContainer;

            container.Add(new Label("C# to TSDef Converter") {
                style = {
                    fontSize = 20,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginBottom = 20,
                }
            });

            container.Add(
                new HelpBox("Generated typings can be verbose at times. Feel free to remove " +
                            "stuff you don't need. Remember, TS type definitions are just for compile-time " +
                            "type checking. They are nice to have, but not required.",
                    HelpBoxMessageType.Info
                ) {
                    style = {
                        // For some reason, the help box needs height set explicitly
                        minHeight = 40,
                        unityTextAlign = TextAnchor.UpperLeft,
                        marginBottom = 20,
                    }
                });

            var typeNameField = new TextField("Fully Qualified Type Name:") {
                value = _typeName,
            };
            typeNameField.RegisterValueChangedCallback(evt => {
                _typeName = evt.newValue;
                SettingsUpdated();
            });
            container.Add(typeNameField);

            var jintSyntaxForEventsToggle = new Toggle("Use Jint syntax for events") {
                value = _jintSyntaxForEvents,
                style = { marginTop = 5 }
            };
            jintSyntaxForEventsToggle.RegisterValueChangedCallback(evt => {
                _jintSyntaxForEvents = evt.newValue;
                SettingsUpdated();
            });
            container.Add(jintSyntaxForEventsToggle);

            var includeBaseMembersToggle = new Toggle("Include Base Members") {
                value = _includeBaseMembers,
                style = { marginTop = 5 }
            };
            includeBaseMembersToggle.RegisterValueChangedCallback(evt => {
                _includeBaseMembers = evt.newValue;
                SettingsUpdated();
            });
            container.Add(includeBaseMembersToggle);

            var includeDeclareToggle = new Toggle("Include `declare module` wrapper") {
                value = _includeDeclare,
                style = { marginTop = 5 }
            };
            includeDeclareToggle.RegisterValueChangedCallback(evt => {
                _includeDeclare = evt.newValue;
                SettingsUpdated();
            });
            container.Add(includeDeclareToggle);

            var extractBaseDefinitionsToggle = new Toggle("Extract Base Definitions") {
                value = _extractBaseDefinitions,
                style = { marginTop = 5 }
            };
            extractBaseDefinitionsToggle.RegisterValueChangedCallback(evt => {
                _extractBaseDefinitions = evt.newValue;
                SettingsUpdated();
            });
            container.Add(extractBaseDefinitionsToggle);

            var excludeUnityBaseTypes = new Toggle("Exclude Unity Base Types(UnityObject, MonoBehaviour, etc)") {
                value = _excludeUnityBaseTypes,
                style = { marginTop = 5 }
            };
            excludeUnityBaseTypes.RegisterValueChangedCallback(evt => {
                _excludeUnityBaseTypes = evt.newValue;
                SettingsUpdated();
            });
            container.Add(excludeUnityBaseTypes);

            container.Add(new Button(() => GenerateDefinition(true)) {
                text = "Convert",
                style = { marginTop = 10 }
            });

            nsQuestionContainer = new VisualElement {
                style = {
                    display = new StyleEnum<DisplayStyle>(DisplayStyle.None),
                    marginTop = 20,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            nsQuestionContainer.Add(new Label("Do you want to generate definitions for all types in the namespace?"));

            var nsQuestionButtons = new VisualElement {
                style = {
                    marginTop = 5,
                    unityTextAlign = TextAnchor.MiddleCenter
                }
            };
            nsQuestionButtons.Add(new Button(() => { nsQuestionCallback?.Invoke(true); }) {
                text = "Yes",
            });
            nsQuestionButtons.Add(new Button(() => { nsQuestionCallback?.Invoke(false); }) {
                text = "No",
            });
            nsQuestionContainer.Add(nsQuestionButtons);

            container.Add(nsQuestionContainer);

            container.Add(new Label("Result:") {
                style = {
                    marginTop = 20,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            });
            _outputField = new TextField {
                multiline = true,
                value = _outputStr.Length > 10000 ? _outputStr.Substring(0, 10000) + "..." : _outputStr,
                isReadOnly = true,

                style = {
                    marginTop = 5,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    maxHeight = 9999,
                }
            };
            container.Add(_outputField);

            container.Add(new Button(() => GUIUtility.systemCopyBuffer = _outputStr) { text = "Copy to Clipboard" });

            rootVisualElement.Add(root);
        }

        private void ShowNamespaceQuestion(Action<bool> callback) {
            nsQuestionContainer.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
            nsQuestionCallback = (result) => {
                callback?.Invoke(result);
                nsQuestionContainer.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
            };
        }

        private void SettingsUpdated() {
            if (_outputStr == null) return;
            if (string.IsNullOrEmpty(_typeName)) return;
            GenerateDefinition();
        }

        private void GenerateDefinition(bool logError = false) {
            var type = AssemblyFinder.FindType(_typeName);

            var options = new TSDefConverterOptions {
                IncludeDeclare = _includeDeclare,
                IncludeBaseMembers = _includeBaseMembers,
                ExcludeUnityBaseTypes = _excludeUnityBaseTypes,
                ExtractBaseDefinitions = _extractBaseDefinitions,
                JintSyntaxForEvents = _jintSyntaxForEvents
            };

            if (type == null) {
                var typesInNs = AssemblyFinder.FindTypesInNamespace(_typeName);
                if (typesInNs.Count == 0) {
                    if (logError)
                        Debug.LogError($"Type {_typeName} not found.");
                    return;
                }

                ShowNamespaceQuestion(r => {
                    if (!r) {
                        if (logError)
                            Debug.LogError($"Type {_typeName} not found.");
                        return;
                    }

                    options.GenerateAllTypesInNamespace = true;
                    options.TypesInNamespace = typesInNs.ToArray();

                    RunWithOptions(options);
                });

                return;
            }


            options.Type = type;
            RunWithOptions(options);
        }

        private void RunWithOptions(TSDefConverterOptions options) {
            var ctx = TsDefConverterContext.NewContext(options);
            var converter = new TSDefConverter(ctx);
            _outputStr = converter.Convert();
            _outputField.value = _outputStr.Length > 10000 ? _outputStr.Substring(0, 10000) + "..." : _outputStr;
        }
    }
}
