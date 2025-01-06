using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    public class DTSGeneratorEditor {
        // SerializedProperty _assemblies;
        // SerializedProperty _namespaces;
        // SerializedProperty _whitelistedTypes;
        // SerializedProperty _blacklistedTypes;
        // SerializedProperty _savePath;
        // // SerializedProperty _strictAssemblies;
        // // SerializedProperty _strictNamespaces;
        // SerializedProperty _whitelistOnly;
        // SerializedProperty _exact;
        // SerializedProperty _includeGlobalObjects;

        DTSGenerator _dtsGenerator;

        public DTSGeneratorEditor(DTSGenerator dtsGenerator) {
            _dtsGenerator = dtsGenerator;
        }

        // public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        //     DTSGenerator genny = property.managedReferenceValue as DTSGenerator;
        //
        //     if (genny == null) {
        //         genny = new DTSGenerator();
        //         // EditorGUI.LabelField(position, label.text, "Null or invalid object");
        //         // return;
        //     }
        //
        //     EditorGUILayout.HelpBox("For generating TypeScript definitions from C# assemblies/namespaces", MessageType.None);
        //
        //     EditorGUILayout.PropertyField(_assemblies, new GUIContent("Assemblies"));
        //     EditorGUILayout.PropertyField(_namespaces, new GUIContent("Namespaces"));
        //     EditorGUILayout.PropertyField(_whitelistedTypes, new GUIContent("Whitelisted Types"));
        //     EditorGUILayout.PropertyField(_blacklistedTypes, new GUIContent("Blacklisted Types"));
        //     EditorGUILayout.PropertyField(_savePath, new GUIContent("Save Path"));
        //     // EditorGUILayout.PropertyField(_strictAssemblies, new GUIContent("Strict Assemblies"));
        //     // EditorGUILayout.PropertyField(_strictNamespaces, new GUIContent("Strict Namespaces"));
        //     EditorGUILayout.PropertyField(_whitelistOnly, new GUIContent("Whitelist Only"));
        //     EditorGUILayout.PropertyField(_exact, new GUIContent("Exact"));
        //     EditorGUILayout.PropertyField(_includeGlobalObjects, new GUIContent("Include Global Objects"));
        //
        //     EditorGUILayout.Space(10);
        //     // if (GUILayout.Button(new GUIContent("Generate", "Generate TS Definitions"), GUILayout.Height(30))) {
        //     //     if (EditorUtility.DisplayDialog("TS Definitions Generation",
        //     //             "This may take a few minutes depending on the number of types. Are you sure you want to proceed right now?",
        //     //             "Confirm", "Cancel")) {
        //     //         Generate(genny);
        //     //     }
        //     // }
        //
        //     // EditorGUI.BeginProperty(position, label, property);
        //     // var indent = EditorGUI.indentLevel;
        //     // EditorGUI.indentLevel = 0;
        //     //
        //     // EditorGUI.PropertyField(position, property, GUIContent.none);
        //     //
        //     // EditorGUI.indentLevel = indent;
        //     // EditorGUI.EndProperty();
        // }

        // void OnEnable() {
        //     _assemblies = serializedObject.FindProperty("assemblies");
        //     _namespaces = serializedObject.FindProperty("namespaces");
        //     _whitelistedTypes = serializedObject.FindProperty("whitelistedTypes");
        //     _blacklistedTypes = serializedObject.FindProperty("blacklistedTypes");
        //     _savePath = serializedObject.FindProperty("savePath");
        //     // _strictAssemblies = serializedObject.FindProperty("strictAssemblies");
        //     // _strictNamespaces = serializedObject.FindProperty("strictNamespaces");
        //     _whitelistOnly = serializedObject.FindProperty("whitelistOnly");
        //     _exact = serializedObject.FindProperty("exact");
        //     _includeGlobalObjects = serializedObject.FindProperty("includeGlobalObjects");
        // }

        // public override void OnInspectorGUI() {
        //     // serializedObject.Update();
        //     
        //
        //     // if (GUILayout.Button(new GUIContent("Test", "Test"), GUILayout.Height(30))) {
        //     //     Test();
        //     // }
        //
        //     // serializedObject.ApplyModifiedProperties();
        // }

        public void Generate(ObjectMappingPair[] globals, string workingDir) {
            var t = new Stopwatch();
            t.Start();
            var assemblies = _dtsGenerator.assemblies.Select((a) => {
                try {
                    return Assembly.Load(a);
                } catch (Exception e) {
                    Debug.Log($"Could not load assembly \"{a}\". Please check your string(s) in the Assemblies list.");
                    Debug.LogError(e);
                    return null;
                }
            }).Where(a => a != null).ToArray();
            var namespaces = _dtsGenerator.namespaces;
            var typesToAdd = _dtsGenerator.whitelistedTypes.Select((t) => {
                Type type = null;
                foreach (Assembly assembly in assemblies) {
                    type = assembly.GetType(t);
                    if (type != null)
                        break;
                }
                if (type == null)
                    Debug.Log($"Could not load type \"{t}\". Please check your string(s) in the Whitelisted Types list.");
                return type;
            }).Where(t => t != null).ToArray();
            var typesToRemove = _dtsGenerator.blacklistedTypes.Select((t) => {
                Type type = null;
                foreach (Assembly assembly in assemblies) {
                    type = assembly.GetType(t);
                    if (type != null)
                        break;
                }
                if (type == null)
                    Debug.Log($"Could not load type \"{t}\". Please check your string(s) in the Blacklisted Types list.");
                return type;
            }).Where(t => t != null).ToArray();
            var types = DTSGen.GetTypes(assemblies, namespaces);
            types = types.Concat(typesToAdd).ToArray();
            var uniqueSet = new HashSet<Type>(types);
            uniqueSet.ExceptWith(typesToRemove);

            if (_dtsGenerator.includeGlobalObjects) {
                uniqueSet.UnionWith(globals.Select(pair => pair.obj.GetType()));
            }

            if (_dtsGenerator.whitelistOnly) {
                // set uniqueSet to typesToAdd
                uniqueSet = new HashSet<Type>(typesToAdd);
            }

            var fullSavePath = Path.Combine(workingDir, _dtsGenerator.savePath);
            GenerateDTS(fullSavePath, uniqueSet.ToArray(), assemblies, namespaces);

            if (_dtsGenerator.includeGlobalObjects) {
                AppendGlobals(globals, fullSavePath);
            }
            t.Stop();
            Debug.Log($"[{t.Elapsed.TotalSeconds} seconds] Generated {fullSavePath}");
        }

        void GenerateDTS(string fullSavePath, Type[] types, Assembly[] assemblies, string[] namespaces) {
            var directory = Path.GetDirectoryName(fullSavePath);
            if (directory == null) {
                Debug.LogError("Invalid Save Path. Null directory.");
                return;
            }
            if (!Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            // var res = _exact.boolValue ? DTSGen.GenerateExact(types) : DTSGen.Generate(types);
            // if (_strictAssemblies.boolValue) {
            //     NamespaceNode root = NamespaceTreeParser.ParseNamespaces(res);
            //     // NamespaceTreeParser.PrintNamespaceTree(root);
            //     string[] namespacesToKeep = _namespaces.ToStringArray().Select(s => "CS." + s).ToArray();
            //     res = NamespaceTreeFilter.FilterNamespaces(root, namespacesToKeep);
            // }
            var res = DTSGen.Generate(types, _dtsGenerator.exact, _dtsGenerator.strictAssemblies ? assemblies : null, _dtsGenerator.strictNamespaces ? namespaces : null);
            File.WriteAllText(fullSavePath, res);
        }

        void AppendGlobals(ObjectMappingPair[] globals, string filepath) {
            if (globals.Length == 0)
                return;

            using (StreamWriter writer = File.AppendText(filepath)) {
                writer.WriteLine($"");
                foreach (var pair in globals) {
                    writer.WriteLine($"declare const {pair.name}: CS.{pair.obj.GetType().FullName};");
                }
            }
        }

        static void Test() {
            string tsDefs = @"

declare namespace CS {
    const __keep_incompatibility: unique symbol;
    interface $Ref<T> {
        value: T
    }
    namespace A {
        namespace B {
            namespace C {
                namespace D {
                    // A.B.C.D content
                }
                namespace E {
                    // A.B.C.E content
                }
            }
            namespace F {
                // A.B.F content
            }
        }
        namespace G {
            // A.G content
        }
    }
    namespace H {
        // H content
    }
}";

            string[] namespacesToKeep = { "CS.A.B.C.D", "CS.A.B.F" };

            NamespaceNode root = NamespaceTreeParser.ParseNamespaces(tsDefs);
            NamespaceTreeParser.PrintNamespaceTree(root);

            string filteredResult = NamespaceTreeFilter.FilterNamespaces(root, namespacesToKeep);
            Debug.Log(filteredResult);
        }
    }

    public class NamespaceNode {
        public string Name { get; set; }
        public string FullName { get; set; }
        public List<NamespaceNode> Children { get; } = new List<NamespaceNode>();
        public string FullContent { get; set; }
        public int IndentLevel { get; set; }
    }

    public class NamespaceTreeParser {
        public static NamespaceNode ParseNamespaces(string tsDefs) {
            NamespaceNode root = null;
            var stack = new Stack<NamespaceNode>();
            var lines = tsDefs.Split('\n');
            var braceCount = 0;
            var contentBuilders = new Dictionary<NamespaceNode, StringBuilder>();
            var currentIndentLevel = 0;

            foreach (var line in lines) {
                var trimmedLine = line.TrimStart();
                var lineIndent = line.Length - trimmedLine.Length;

                if (trimmedLine.StartsWith("namespace") || trimmedLine.StartsWith("declare namespace")) {
                    var match = Regex.Match(trimmedLine, @"(?:declare\s+)?namespace\s+(\w+)");
                    if (match.Success) {
                        var namespaceName = match.Groups[1].Value;
                        var newNode = new NamespaceNode {
                            Name = namespaceName,
                            FullName = stack.Count > 0 ? $"{stack.Peek().FullName}.{namespaceName}" : namespaceName,
                            IndentLevel = currentIndentLevel
                        };

                        if (root == null) {
                            root = newNode;
                        } else {
                            stack.Peek().Children.Add(newNode);
                        }

                        stack.Push(newNode);
                        contentBuilders[newNode] = new StringBuilder();
                        contentBuilders[newNode].AppendLine(line);
                    }
                    currentIndentLevel = lineIndent / 4;
                } else {
                    foreach (var node in stack) {
                        contentBuilders[node].AppendLine(line);
                    }
                }

                braceCount += trimmedLine.Count(c => c == '{');
                braceCount -= trimmedLine.Count(c => c == '}');

                if (braceCount < stack.Count && stack.Count > 0) {
                    var poppedNode = stack.Pop();
                    poppedNode.FullContent = contentBuilders[poppedNode].ToString().Trim();
                    contentBuilders.Remove(poppedNode);
                }
            }

            // Handle any remaining nodes in the stack
            while (stack.Count > 0) {
                var poppedNode = stack.Pop();
                poppedNode.FullContent = contentBuilders[poppedNode].ToString().Trim();
                contentBuilders.Remove(poppedNode);
            }

            return root;
        }

        public static void PrintNamespaceTree(NamespaceNode node, int indent = 0) {
            Debug.Log(new string(' ', indent * 4) + $"{node.Name} (FullName: {node.FullName}, Indent: {node.IndentLevel})");
            Debug.Log(node.FullContent);
            foreach (var child in node.Children) {
                PrintNamespaceTree(child, indent + 1);
            }
        }
    }

    public class NamespaceTreeFilter {
        public static string FilterNamespaces(NamespaceNode root, string[] namespacesToKeep) {
            var result = new StringBuilder();
            result.Append("declare ");
            FilterNamespacesRecursive(root, namespacesToKeep, result, 0);
            return result.ToString().Trim();
        }

        private static void FilterNamespacesRecursive(NamespaceNode node, string[] namespacesToKeep, StringBuilder result, int indentLevel) {
            bool exactKeep = namespacesToKeep.Any(ns => node.FullName == ns);

            bool ancestralKeep = node.FullName == "CS" || namespacesToKeep.Any(ns => ns.StartsWith(node.FullName + "."));

            if (exactKeep) {
                result.Append(new string(' ', indentLevel * 4) + node.FullContent);
                result.AppendLine();
            } else if (ancestralKeep) {
                result.AppendLine(new string(' ', indentLevel * 4) + $"namespace {node.Name} {{");
                foreach (var child in node.Children) {
                    FilterNamespacesRecursive(child, namespacesToKeep, result, indentLevel + 1);
                }
                result.AppendLine(new string(' ', indentLevel * 4) + $"}}");
            }

            // bool keepThisNamespace = node.Name == "CS" || namespacesToKeep.Any(ns =>
            //     node.FullName == ns ||
            //     ns.StartsWith(node.FullName + ".") ||
            //     node.FullName.StartsWith(ns + "."));
            //
            // if (keepThisNamespace) {
            //     var lines = node.FullContent.Split('\n');
            //     var indent = new string(' ', indentLevel * 4);
            //
            //     foreach (var line in lines) {
            //         result.AppendLine(indent + line.TrimStart());
            //     }
            //
            //     foreach (var child in node.Children) {
            //         FilterNamespacesRecursive(child, namespacesToKeep, result, indentLevel + 1);
            //     }
            // }
        }
    }
}