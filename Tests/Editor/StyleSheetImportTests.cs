using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OneJS.CustomStyleSheets;
using OneJS.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Issue #154: OneJS bundled Unity's CSS parser under Unity's own assembly name, the editor loaded it in
    /// place of its own, and on Unity 6.6 and later every stylesheet import failed. These hold on any editor:
    /// on 6000.6.7f1 and 6000.7.0b7 the first two failed before OneJS's parser had its own name.
    /// </summary>
    public class StyleSheetImportTests {
        const string Dir = "Assets/OneJSStyleSheetImportTests";

        [TearDown]
        public void TearDown() {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        static string Write(string name, string text) {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "OneJSStyleSheetImportTests");
            var path = $"{Dir}/{name}";
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        [Test]
        public void UnitysOwnImporterReadsUssAndUxmlWithOneJSInstalled() {
            // An import that fails logs an error, which fails the test by itself.
            var uss = Write("probe.uss", ".probe { color: red; }\n");
            var uxml = Write("probe.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label class=\"probe\" /></ui:UXML>\n");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(uss);
            Assert.IsNotNull(sheet);
            Assert.IsFalse(sheet.importedWithErrors);
            Assert.AreEqual(1, new SerializedObject(sheet).FindProperty("m_Rules").arraySize);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml));
        }

        [Test]
        public void NoAssemblyNamedExCSSUnityLoadsFromOneJS() {
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("OneJS.Runtime")), ".."));
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "ExCSS.Unity")) {
                Assert.IsFalse(Path.GetFullPath(assembly.Location).StartsWith(root), $"ExCSS.Unity loads from {assembly.Location}, inside OneJS");
            }
            var parser = typeof(UssCompiler).GetField("_parser", BindingFlags.Instance | BindingFlags.NonPublic).FieldType;
            Assert.AreEqual("OneJS.ExCSS", parser.Assembly.GetName().Name);
        }

        [Test]
        public void TheOldParserPathIsATombstoneThatLoadsNowhere() {
            // Importing a package never removes a file, so the old path keeps a harmless assembly that replaces the old one.
            var path = Path.Combine(Path.GetDirectoryName(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("OneJS.Runtime")), "Styling/ExCSS.Unity.dll").Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            Assert.IsNotNull(importer, path);
            Assert.IsFalse(importer.GetCompatibleWithEditor());
            Assert.IsFalse(importer.GetCompatibleWithAnyPlatform());
            Assert.AreEqual("OneJS.ExCSS.Unity.Removed", AssemblyName.GetAssemblyName(Path.GetFullPath(path)).Name);
        }

        [Test]
        public void TheRepairLeavesAHealthySheetAlone() {
            var uss = Write("healthy.uss", ".healthy { color: red; }\n");
            var uxml = Write("healthy.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" />\n");
            var failed = StyleImportRepair.FailedImports();
            CollectionAssert.DoesNotContain(failed, uss);
            CollectionAssert.DoesNotContain(failed, uxml);
        }
    }
}
