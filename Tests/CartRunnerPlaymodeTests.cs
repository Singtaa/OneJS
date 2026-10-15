#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OneJS.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// A runner linked to a real root cart, in Play mode (Specs/ROOT_CARTS.md, step 1).
    ///
    /// The cart is a folder at the project root with an oj.json naming it, and its editor
    /// build sits in Library/OneJS/Carts/{folder}/app.js.txt in the shape ojplay's Unity
    /// target writes it. The runner links by name only, here a name that is not the
    /// folder's, and its Panel Settings is a plain one from no project folder at all.
    /// </summary>
    [TestFixture]
    public class CartRunnerPlaymodeTests {
        const string Folder = "OneJSCartPlayFixture";
        const string Twin = "OneJSCartPlayFixtureTwin";
        const string Name = "Cart Play Fixture";

        GameObject _host;
        JSRunner _runner;

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        static string CartDir => Path.Combine(ProjectRoot, Folder);
        static string BuildDir => Path.Combine(ProjectRoot, "Library", "OneJS", "Carts", Folder);

        static string Bundle(string version) =>
            "globalThis.__cartWorkingDir = __workingDir;\n" +
            $"var label = new CS.UnityEngine.UIElements.Label(\"{version}\");\n" +
            "label.name = \"cart-label\";\n" +
            "__root.Add(label);\n";

        static void WriteCart(string folder, string name) {
            var dir = Path.Combine(ProjectRoot, folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "oj.json"), "{ \"name\": \"" + name + "\", \"version\": \"1.0.0\" }");
            File.WriteAllText(Path.Combine(dir, "index.tsx"), "// built by ojplay, not read by the runner");
            RootCarts.Invalidate();
        }

        static void WriteBundle(string version) {
            Directory.CreateDirectory(BuildDir);
            File.WriteAllText(Path.Combine(BuildDir, "app.js.txt"), Bundle(version));
        }

        static void Delete() {
            foreach (var dir in new[] { CartDir, Path.Combine(ProjectRoot, Twin), BuildDir })
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            RootCarts.Invalidate();
        }

        [SetUp]
        public void SetUp() {
            Delete();
            WriteCart(Folder, Name);
            WriteBundle("v1");
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) Object.DestroyImmediate(_host);
            var janitor = GameObject.Find("Janitor");
            if (janitor != null) Object.DestroyImmediate(janitor);
            foreach (var es in FindCompat.FindObjectsByType<EventSystem>())
                if (es.gameObject.name == "EventSystem (OneJS)") Object.DestroyImmediate(es.gameObject);
            Delete();
        }

        void StartRunner(string cart, bool panelSettings = true) {
            _host = new GameObject("CartRunner");
            _host.SetActive(false);
            _runner = _host.AddComponent<JSRunner>();
            var field = typeof(JSRunner).GetField("_cart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "JSRunner._cart was not found: ojplay finds this field by name, so it must not move.");
            field.SetValue(_runner, cart);
            if (panelSettings) _runner.SetPanelSettings(ScriptableObject.CreateInstance<PanelSettings>());
            _host.SetActive(true);
        }

        IEnumerator Until(System.Func<bool> done, float seconds, string what) {
            var until = Time.realtimeSinceStartup + seconds;
            while (!done()) {
                if (Time.realtimeSinceStartup > until) Assert.Fail($"{what} within {seconds} s.");
                yield return null;
            }
        }

        string Label() => _runner.Bridge?.Root?.Q<Label>("cart-label")?.text;

        [UnityTest]
        public IEnumerator RunsTheLibraryBuild_WithTheCartFolderAsWorkingDirectory() {
            var cartBefore = Directory.GetFileSystemEntries(CartDir, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
            StartRunner("cart play fixture");

            yield return Until(() => Label() == "v1", 10f, "The cart's build did not render");

            Assert.AreEqual(CartDir, _runner.Bridge.Eval("globalThis.__cartWorkingDir"), "__workingDir is the cart folder.");
            Assert.AreEqual(_runner.PanelSettingsAsset, _host.GetComponent<UIDocument>().panelSettings,
                "Any Panel Settings works for a cart, from no project folder at all.");
            CollectionAssert.AreEqual(cartBefore,
                Directory.GetFileSystemEntries(CartDir, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray(),
                "Unity adds nothing inside a cart.");
        }

        [UnityTest]
        public IEnumerator HotReloads_WhenTheLibraryBuildChanges() {
            StartRunner(Name);
            yield return Until(() => Label() == "v1", 10f, "The cart's build did not render");
            var reloads = _runner.ReloadCount;

            WriteBundle("v2");

            yield return Until(() => Label() == "v2", 10f, "The rewritten build was not reloaded");
            Assert.Greater(_runner.ReloadCount, reloads);
        }

        [UnityTest]
        public IEnumerator AMissingName_DoesNotStart_AndSaysSoOnce() {
            LogAssert.Expect(LogType.Error, new Regex("No cart named No Such Cart in this project\\."));
            StartRunner("No Such Cart");
            for (var i = 0; i < 30; i++) yield return null;
            Assert.IsFalse(_runner.IsRunning, "A runner whose name resolves to no cart does not start.");
        }

        [UnityTest]
        public IEnumerator TwoCartsWithTheName_NeitherRuns_AndBothAreNamedOnce() {
            WriteCart(Twin, Name.ToLowerInvariant());
            LogAssert.Expect(LogType.Error, new Regex($"Two carts are named {Name}: {Folder}/oj\\.json and {Twin}/oj\\.json\\."));
            StartRunner(Name);
            for (var i = 0; i < 30; i++) yield return null;
            Assert.IsFalse(_runner.IsRunning, "A name two carts answer to runs neither.");
        }

        // With no Panel Settings as well, the cart's problem is the one said, as the inspector
        // puts it first. Any other error, the Panel Settings one included, fails the test.

        [UnityTest]
        public IEnumerator AMissingName_WithNoPanelSettings_SaysTheCartProblem() {
            LogAssert.Expect(LogType.Error, new Regex("No cart named No Such Cart in this project\\."));
            StartRunner("No Such Cart", panelSettings: false);
            for (var i = 0; i < 30; i++) yield return null;
            Assert.IsFalse(_runner.IsRunning);
        }

        [UnityTest]
        public IEnumerator TwoCartsWithTheName_WithNoPanelSettings_SaysTheCartProblem() {
            WriteCart(Twin, Name.ToLowerInvariant());
            LogAssert.Expect(LogType.Error, new Regex($"Two carts are named {Name}: {Folder}/oj\\.json and {Twin}/oj\\.json\\."));
            StartRunner(Name, panelSettings: false);
            for (var i = 0; i < 30; i++) yield return null;
            Assert.IsFalse(_runner.IsRunning);
        }

        [UnityTest]
        public IEnumerator AFoundCart_WithNoPanelSettings_SaysPanelSettings() {
            LogAssert.Expect(LogType.Error, new Regex($"runs the cart {Name} but has no Panel Settings"));
            StartRunner(Name, panelSettings: false);
            for (var i = 0; i < 30; i++) yield return null;
            Assert.IsFalse(_runner.IsRunning);
        }
    }
}
#endif
