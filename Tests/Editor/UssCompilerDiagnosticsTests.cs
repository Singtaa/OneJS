using NUnit.Framework;
using OneJS.CustomStyleSheets;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The USS parser is deliberately tolerant, one bad line must not abort a
    /// sheet, but that used to mean a typo'd property compiled cleanly and
    /// UI Toolkit ignored it with no trace. These pin the diagnostics that
    /// replace the silence. The typo test doubles as the guard on the
    /// reflected property table: if a Unity upgrade renames the internal
    /// table, the diagnostic stops firing and the test goes red.
    /// </summary>
    public class UssCompilerDiagnosticsTests {
        StyleSheet _asset;

        [SetUp]
        public void CreateAsset() => _asset = ScriptableObject.CreateInstance<StyleSheet>();

        [TearDown]
        public void DestroyAsset() => Object.DestroyImmediate(_asset);

        [Test]
        public void TypodProperty_YieldsOneDiagnosticNamingIt() {
            var compiler = new UssCompiler();
            compiler.Compile(_asset, ".card { bacground-color: red; width: 100px; }");

            Assert.AreEqual(1, compiler.Diagnostics.Count);
            Assert.AreEqual("bacground-color", compiler.Diagnostics[0].Property);
            StringAssert.Contains("ignore", compiler.Diagnostics[0].Message);
        }

        [Test]
        public void ValidSheet_UnityPrefixedPropertiesIncluded_YieldsNoDiagnostics() {
            var compiler = new UssCompiler();
            compiler.Compile(_asset,
                ".a { background-color: #ff0000; -unity-font-style: bold; padding-top: 4px; }");

            Assert.AreEqual(0, compiler.Diagnostics.Count,
                compiler.Diagnostics.Count > 0 ? compiler.Diagnostics[0].ToString() : "");
        }

        [Test]
        public void CustomVariableDeclarations_AreNotFlagged() {
            var compiler = new UssCompiler();
            compiler.Compile(_asset, "* { --tw-scale-x: 1; --my-color: #123456; }");

            Assert.AreEqual(0, compiler.Diagnostics.Count,
                compiler.Diagnostics.Count > 0 ? compiler.Diagnostics[0].ToString() : "");
        }

        [Test]
        public void DiagnosticsClear_BetweenCompiles() {
            var compiler = new UssCompiler();
            compiler.Compile(_asset, ".a { flexx: 1; }");
            Assert.AreEqual(1, compiler.Diagnostics.Count);

            var second = ScriptableObject.CreateInstance<StyleSheet>();
            try {
                compiler.Compile(second, ".a { flex-grow: 1; }");
                Assert.AreEqual(0, compiler.Diagnostics.Count);
            } finally {
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void DiagnosticCarriesTheRuleLine() {
            var compiler = new UssCompiler();
            compiler.Compile(_asset, ".a {\n    color: red;\n}\n.b {\n    colr: red;\n}");

            Assert.AreEqual(1, compiler.Diagnostics.Count);
            Assert.AreEqual("colr", compiler.Diagnostics[0].Property);
            Assert.Greater(compiler.Diagnostics[0].Line, 1);
        }
    }
}
