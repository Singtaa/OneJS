using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OneJS;
using OneJS.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Root carts (Specs/ROOT_CARTS.md): the one-level scan, the name link and its two
    /// refusals, what a cart runner's paths resolve to, and that it writes nothing into the
    /// cart. The scan tests run against a temp folder; the runner tests put a real cart at
    /// the project root, because that is the only place a runner looks.
    /// </summary>
    [TestFixture]
    public class RootCartsTests {
        const string CartFolder = "OneJSRootCartFixture";
        const string TwinFolder = "OneJSRootCartFixtureTwin";
        const string CartName = "Root Cart Fixture";
        const string PanelFolder = "Assets/OneJSRootCartFixture";

        string _temp;
        readonly List<GameObject> _spawned = new List<GameObject>();

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        [SetUp]
        public void SetUp() {
            _temp = Path.Combine(Path.GetTempPath(), "OneJSRootCarts_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
            DeleteProjectFixture();
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (Directory.Exists(_temp)) Directory.Delete(_temp, true);
            DeleteProjectFixture();
            RootCarts.Invalidate();
        }

        static void DeleteProjectFixture() {
            foreach (var folder in new[] { CartFolder, TwinFolder }) {
                var full = Path.Combine(ProjectRoot, folder);
                if (Directory.Exists(full)) Directory.Delete(full, true);
            }
            if (AssetDatabase.IsValidFolder(PanelFolder)) AssetDatabase.DeleteAsset(PanelFolder);
            RootCarts.Invalidate();
        }

        void Write(string relative, string text) {
            var path = Path.Combine(_temp, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        // MARK: The scan

        [Test]
        public void Scan_ReadsOneLevel_AndSkipsUnitysFoldersDotFoldersAndNodeModules() {
            Write("Lightning/oj.json", "{ \"name\": \"Lightning\", \"version\": \"1.2.0\" }");
            foreach (var skipped in new[] { "Assets", "Packages", "ProjectSettings", "Library", "Temp", "Logs", "UserSettings", "node_modules", ".oj" })
                Write(skipped + "/oj.json", "{ \"name\": \"" + skipped + "\" }");
            Write("Group/Storm/oj.json", "{ \"name\": \"Storm\" }");
            Write("Plain/readme.md", "not a cart");

            var carts = RootCarts.Scan(_temp);

            CollectionAssert.AreEqual(new[] { "Lightning" }, carts.Select(c => c.Folder).ToArray(),
                "Only <root>/*/oj.json is a cart, and Unity's folders, dot folders and node_modules are never one.");
            Assert.AreEqual("Lightning", carts[0].Name);
            Assert.AreEqual("1.2.0", carts[0].Version);
            Assert.AreEqual("index.tsx", carts[0].Entry, "oj.json's entry defaults to index.tsx.");
        }

        [Test]
        public void Scan_ListsAnOjJsonWithNoNameOrNoJson_AsUnusableWithItsFix() {
            Write("Sandbox/oj.json", "{ \"version\": \"0.1.0\" }");
            Write("Broken/oj.json", "name: Broken");

            var carts = RootCarts.Scan(_temp).ToDictionary(c => c.Folder);

            Assert.IsFalse(carts["Sandbox"].Usable);
            StringAssert.Contains("Sandbox/oj.json has no \"name\", so nothing can link to it", carts["Sandbox"].Problem);
            StringAssert.Contains("\"name\": \"Sandbox\"", carts["Sandbox"].Problem, "The fix names the folder as an example.");
            Assert.IsFalse(carts["Broken"].Usable);
            StringAssert.Contains("Broken/oj.json is not a JSON object", carts["Broken"].Problem);
            Assert.AreEqual(RootCarts.Outcome.Missing, RootCarts.Resolve("Sandbox", RootCarts.Scan(_temp)).Outcome,
                "An unusable cart is never linked, even by its folder's name.");
        }

        // MARK: The name link

        [Test]
        public void Resolve_ComparesTheName_TrimmedAndIgnoringCase() {
            Write("drop-everything/oj.json", "{ \"name\": \"  Drop Everything \" }");
            var carts = RootCarts.Scan(_temp);

            var resolution = RootCarts.Resolve("drop everything", carts);

            Assert.AreEqual(RootCarts.Outcome.Found, resolution.Outcome);
            Assert.AreEqual("drop-everything", resolution.Cart.Folder, "The link is the name; the folder is found from it.");
            Assert.IsNull(resolution.Problem);
        }

        [Test]
        public void Resolve_TwoCartsWithOneName_RefusesBoth_NamingBothPaths() {
            Write("Lightning/oj.json", "{ \"name\": \"Lightning\" }");
            Write("lightning-fork/oj.json", "{ \"name\": \"lightning\" }");

            var resolution = RootCarts.Resolve("Lightning", RootCarts.Scan(_temp));

            Assert.AreEqual(RootCarts.Outcome.Duplicate, resolution.Outcome);
            Assert.IsNull(resolution.Cart, "Neither wins by scan order.");
            Assert.AreEqual(
                "Two carts are named Lightning: Lightning/oj.json and lightning-fork/oj.json. " +
                "A runner links by name, so it runs neither. Rename one in its oj.json.",
                resolution.Problem);
        }

        [Test]
        public void Resolve_AMissingName_SaysSo() {
            Write("sumo/oj.json", "{ \"name\": \"Sumo Deluxe\" }");

            var resolution = RootCarts.Resolve("Sumo", RootCarts.Scan(_temp));

            Assert.AreEqual(RootCarts.Outcome.Missing, resolution.Outcome);
            Assert.AreEqual("No cart named Sumo in this project.", resolution.Problem);
        }

        [Test]
        public void CompareVersions_ReadsTheNumbers() {
            Assert.Less(RootCarts.CompareVersions("0.10.2", "0.11.0"), 0);
            Assert.AreEqual(0, RootCarts.CompareVersions("0.11.0", "0.11.0"));
            Assert.Greater(RootCarts.CompareVersions("1.0.0-beta.1", "0.11.0"), 0);
        }

        // MARK: files.json

        static RootCarts.Cart TempCart(string root, string folder) =>
            new RootCarts.Cart { Name = folder, Folder = folder, FullPath = Path.Combine(root, folder) };

        [Test]
        public void ReadFiles_MapsEachSourceUnderTheCart_ToItsPlaceUnderStreamingAssets() {
            Write("Lightning/glow.png", "x");
            Write("Lightning/.oj/carts/@singtaa/art@1.0.0/glow.png", "y");
            Write("out/files.json", "{ \"version\": 1, \"base\": \"onejs/carts/Lightning\", \"files\": [" +
                "{ \"from\": \"glow.png\", \"to\": \"glow.png\" }," +
                "{ \"from\": \".oj/carts/@singtaa/art@1.0.0/glow.png\", \"to\": \"@singtaa/art@1.0.0/glow.png\" } ] }");

            var files = CartRunners.ReadFiles(TempCart(_temp, "Lightning"), Path.Combine(_temp, "out/files.json"));

            CollectionAssert.AreEqual(new[] {
                "onejs/carts/Lightning/glow.png",
                "onejs/carts/Lightning/@singtaa/art@1.0.0/glow.png",
            }, files.Select(f => f.destination).ToArray());
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_temp, "Lightning", "glow.png")), files[0].source);
        }

        [Test]
        public void ReadFiles_RefusesAnotherBase_AFileOutsideTheCart_AndAMissingFile() {
            Write("Lightning/glow.png", "x");
            Write("secret.txt", "s");
            var cart = TempCart(_temp, "Lightning");

            Write("a/files.json", "{ \"version\": 1, \"base\": \"onejs/carts/Storm\", \"files\": [] }");
            Assert.Throws<BuildFailedException>(() => CartRunners.ReadFiles(cart, Path.Combine(_temp, "a/files.json")));

            Write("b/files.json", "{ \"version\": 1, \"base\": \"onejs/carts/Lightning\", \"files\": [ { \"from\": \"../secret.txt\", \"to\": \"secret.txt\" } ] }");
            Assert.Throws<BuildFailedException>(() => CartRunners.ReadFiles(cart, Path.Combine(_temp, "b/files.json")));

            Write("c/files.json", "{ \"version\": 1, \"base\": \"onejs/carts/Lightning\", \"files\": [ { \"from\": \"gone.png\", \"to\": \"gone.png\" } ] }");
            Assert.Throws<BuildFailedException>(() => CartRunners.ReadFiles(cart, Path.Combine(_temp, "c/files.json")));
        }

        // MARK: A cart runner

        JSRunner Runner(string cart, PanelSettings panelSettings = null) {
            var go = new GameObject("CartRunner");
            go.SetActive(false);
            _spawned.Add(go);
            var runner = go.AddComponent<JSRunner>();
            var so = new SerializedObject(runner);
            so.FindProperty("_cart").stringValue = cart;
            so.FindProperty("_panelSettings").objectReferenceValue = panelSettings;
            so.ApplyModifiedPropertiesWithoutUndo();
            return runner;
        }

        static string WriteProjectCart(string folder, string name) {
            var dir = Path.Combine(ProjectRoot, folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "oj.json"), "{ \"name\": \"" + name + "\" }");
            File.WriteAllText(Path.Combine(dir, "index.tsx"), "// the cart's own source");
            RootCarts.Invalidate();
            return dir;
        }

        [Test]
        public void ARunnerWithNoCart_IsTheLegacyRunner() {
            var runner = Runner("");
            Assert.IsFalse(runner.IsCart);
            Assert.IsNull(runner.CartFolder);
            Assert.IsNull(runner.WorkingDirFullPath, "No Panel Settings and no cart: no working directory, as before carts.");
        }

        [Test]
        public void ACartRunner_ReadsItsCartWhereItSits_AndItsBuildFromLibrary() {
            var dir = WriteProjectCart(CartFolder, CartName);
            var runner = Runner("root cart fixture");

            Assert.IsTrue(runner.IsCart);
            Assert.AreEqual(dir, runner.WorkingDirFullPath, "The working directory is the cart folder.");
            Assert.AreEqual(Path.Combine(ProjectRoot, "Library", "OneJS", "Carts", CartFolder, "app.js.txt"), runner.EntryFileFullPath);
            Assert.IsNull(runner.InstanceFolder, "A cart has no folder under Assets.");
            Assert.IsNull(runner.TypingsFullPath, "No type generation for a cart.");
        }

        [Test]
        public void ACartRunner_WritesNothingIntoTheCart() {
            var dir = WriteProjectCart(CartFolder, CartName);
            var before = Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
            var runner = Runner(CartName);

            Assert.IsFalse(runner.EnsureProjectSetup(), "No scaffolding, packs or default files for a cart.");
            Assert.IsFalse(runner.RestoreDefaultFile("package.json"));
            Assert.IsNull(runner.DescribeMissingDefaultFiles());
            Assert.IsNull(runner.TypingsFullPath, "Type generation has nowhere to write for a cart.");

            CollectionAssert.AreEqual(before,
                Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray(),
                "Unity adds nothing inside a cart.");
        }

        [Test]
        public void ACartRunner_TakesAnyPanelSettings_WhoseFolderMeansNothing() {
            WriteProjectCart(CartFolder, CartName);
            Directory.CreateDirectory(Path.Combine(ProjectRoot, PanelFolder));
            AssetDatabase.Refresh();
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, PanelFolder + "/PanelSettings.asset");
            var runner = Runner(CartName, ps);

            Assert.IsFalse(runner.IsPanelSettingsInValidProjectFolder(), "Test setup: the folder is no app folder.");
            Assert.IsNotNull(runner.WorkingDirFullPath, "A cart runner does not consult the Panel Settings' folder.");
            CollectionAssert.AreEquivalent(new[] { "PanelSettings.asset", "PanelSettings.asset.meta" },
                Directory.GetFiles(Path.Combine(ProjectRoot, PanelFolder)).Select(Path.GetFileName).ToArray(),
                "Nothing is written beside the Panel Settings.");
        }

        // MARK: Player build refusals

        static void BuildCart(JSRunner runner) {
            var method = typeof(JSRunnerBuildProcessor).GetMethod("BuildCart", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "JSRunnerBuildProcessor.BuildCart(JSRunner) was not found via reflection.");
            try {
                method.Invoke(new JSRunnerBuildProcessor(), new object[] { runner });
            } catch (TargetInvocationException e) {
                throw e.InnerException;
            }
        }

        [Test]
        public void APlayerBuild_RefusesAMissingName_InTheInspectorsWords() {
            var runner = Runner("No Such Cart");
            var e = Assert.Throws<BuildFailedException>(() => BuildCart(runner));
            StringAssert.Contains("No cart named No Such Cart in this project.", e.Message);
        }

        [Test]
        public void APlayerBuild_RefusesTwoCartsWithTheName_NamingBoth() {
            WriteProjectCart(CartFolder, CartName);
            WriteProjectCart(TwinFolder, CartName.ToUpperInvariant());
            var runner = Runner(CartName);
            var e = Assert.Throws<BuildFailedException>(() => BuildCart(runner));
            StringAssert.Contains($"Two carts are named {CartName}: {CartFolder}/oj.json and {TwinFolder}/oj.json.", e.Message);
        }

        [Test]
        public void APlayerBuildWithNoCart_LeavesStreamingAssetsCartsAlone() {
            var commit = typeof(JSRunnerBuildProcessor).GetMethod("CommitCartsTo", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(commit, "JSRunnerBuildProcessor.CommitCartsTo(string) was not found via reflection.");
            var dest = Path.Combine(_temp, "StreamingAssets", "onejs", "carts");
            commit.Invoke(new JSRunnerBuildProcessor(), new object[] { dest });
            Assert.IsFalse(Directory.Exists(dest), "A build of apps under Assets alone creates no onejs/carts.");
        }

        static void Touch(string file) {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, "x");
        }

        [Test]
        public void AfterABuild_TheCartsCopyLeavesTheProject_AndAppAssetsStay() {
            var streaming = Path.Combine(_temp, "StreamingAssets");
            var onejs = Path.Combine(streaming, "onejs");
            Touch(Path.Combine(onejs, "carts", CartFolder, "glow.png"));
            Touch(Path.Combine(onejs, "carts", CartFolder + ".meta"));
            Touch(Path.Combine(onejs, "carts.meta"));
            Touch(Path.Combine(onejs, "carts.staging", CartFolder, "glow.png"));
            Touch(Path.Combine(onejs, "assets", "glow.png"));
            Touch(Path.Combine(onejs, "assets.meta"));

            Assert.IsTrue(JSRunnerBuildProcessor.RemoveCartFiles(streaming, madeStreamingAssets: true));

            Assert.IsFalse(Directory.Exists(Path.Combine(onejs, "carts")), "The build's copy of the carts' files is removed.");
            Assert.IsFalse(File.Exists(Path.Combine(onejs, "carts.meta")), "Its .meta goes with it.");
            Assert.IsFalse(Directory.Exists(Path.Combine(onejs, "carts.staging")), "A staging folder left by a failed build is removed too.");
            FileAssert.Exists(Path.Combine(onejs, "assets", "glow.png"), "The apps' onejs/assets is not the carts' to remove.");
            Assert.IsFalse(JSRunnerBuildProcessor.RemoveCartFiles(streaming, madeStreamingAssets: true), "A second removal finds nothing.");
        }

        [Test]
        public void TheCartsCopy_TakesTheFoldersItLeftEmpty_ButNotAStreamingAssetsItDidNotMake() {
            var streaming = Path.Combine(_temp, "StreamingAssets");
            Touch(Path.Combine(streaming, "onejs", "carts", CartFolder, "glow.png"));
            Touch(Path.Combine(streaming, "onejs.meta"));
            JSRunnerBuildProcessor.RemoveCartFiles(streaming, madeStreamingAssets: false);
            Assert.IsFalse(Directory.Exists(Path.Combine(streaming, "onejs")), "An onejs/ the removal left empty goes.");
            Assert.IsFalse(File.Exists(Path.Combine(streaming, "onejs.meta")), "Its .meta goes with it.");
            Assert.IsTrue(Directory.Exists(streaming), "A StreamingAssets the project already had stays, even empty.");

            Touch(Path.Combine(streaming, "onejs", "carts", CartFolder, "glow.png"));
            JSRunnerBuildProcessor.RemoveCartFiles(streaming, madeStreamingAssets: true);
            Assert.IsFalse(Directory.Exists(streaming), "A StreamingAssets the cart commit made goes once it is empty.");
        }
    }
}
