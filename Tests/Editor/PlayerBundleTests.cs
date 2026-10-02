using System.IO;
using NUnit.Framework;
using OneJS.Editor;
using UnityEditor.Build;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The production build a player gets, and the editor's development bundle
    /// coming back afterwards.
    ///
    /// Each app here has a real package.json whose build script is a small node
    /// program that writes what NODE_ENV it was given into ../app.js.txt, so
    /// these run npm for real, the way a player build does, without needing
    /// esbuild. onejs-unity's own tests cover what the build does with NODE_ENV.
    /// </summary>
    [TestFixture]
    public class PlayerBundleTests {
        string _app;
        string _workingDir;
        string _bundle;
        string _map;

        [SetUp]
        public void SetUp() {
            _app = Path.Combine(Path.GetTempPath(), "onejs-player-bundle-" + Path.GetRandomFileName());
            _workingDir = Path.Combine(_app, "~");
            _bundle = Path.Combine(_app, "app.js.txt");
            _map = Path.Combine(_app, "app.js.map.txt");
            Directory.CreateDirectory(Path.Combine(_workingDir, "node_modules"));
            File.WriteAllText(_bundle, "editor bundle");
            File.WriteAllText(_map, "editor map");
            PlayerBundle.ForgetEditorBundles();
        }

        [TearDown]
        public void TearDown() {
            PlayerBundle.ForgetEditorBundles();
            if (Directory.Exists(_app)) Directory.Delete(_app, true);
        }

        /// <summary>An app whose `npm run build` runs `script` with node.</summary>
        void BuildScript(string script) {
            File.WriteAllText(Path.Combine(_workingDir, "build.js"), script);
            File.WriteAllText(Path.Combine(_workingDir, "package.json"),
                "{ \"name\": \"app\", \"scripts\": { \"build\": \"node build.js\" } }");
        }

        const string WritesNodeEnv =
            "const fs = require('fs'); fs.writeFileSync('../app.js.txt', 'NODE_ENV=' + process.env.NODE_ENV); fs.writeFileSync('../app.js.map.txt', 'player map')";

        PlayerBundle.Result Build() {
            var result = PlayerBundle.BuildForPlayer(_workingDir, _bundle, "Hud");
            if (result.Outcome == PlayerBundle.Outcome.NpmUnavailable) Assert.Ignore("npm is not available here: " + result.Reason);
            return result;
        }

        [Test]
        public void RunsTheBuildScriptWithNodeEnvProduction() {
            BuildScript(WritesNodeEnv);
            var result = Build();
            Assert.AreEqual(PlayerBundle.Outcome.Built, result.Outcome, result.Reason);
            Assert.AreEqual("NODE_ENV=production", File.ReadAllText(_bundle));
            Assert.AreEqual("player map", File.ReadAllText(_map));
        }

        [Test]
        public void GivesTheEditorItsOwnBundleBackAfterThePlayerBuild() {
            BuildScript(WritesNodeEnv);
            Build();
            PlayerBundle.RestoreEditorBundles();
            Assert.AreEqual("editor bundle", File.ReadAllText(_bundle));
            Assert.AreEqual("editor map", File.ReadAllText(_map));
        }

        [Test]
        public void KeepsABundleTheWatcherRebuiltAfterThePlayerBuild() {
            BuildScript(WritesNodeEnv);
            Build();
            File.WriteAllText(_bundle, "rebuilt by the watcher");
            PlayerBundle.RestoreEditorBundles();
            Assert.AreEqual("rebuilt by the watcher", File.ReadAllText(_bundle));
        }

        [Test]
        public void FailsThePlayerBuildWhenTheAppDoesNotBuild_SayingWhichAppAndWhy() {
            BuildScript("console.error('index.tsx: Expected a closing paren'); process.exit(1)");
            BuildFailedException e = null;
            try {
                Build();
            } catch (BuildFailedException thrown) {
                e = thrown;
            }
            Assert.IsNotNull(e, "A build script that exits 1 must fail the player build.");
            StringAssert.Contains("Hud", e.Message);
            StringAssert.Contains("index.tsx: Expected a closing paren", e.Message);
            Assert.AreEqual("editor bundle", File.ReadAllText(_bundle));
        }

        [Test]
        public void ShipsTheBundleOnDiskWhenDependenciesAreNotInstalled() {
            BuildScript(WritesNodeEnv);
            Directory.Delete(Path.Combine(_workingDir, "node_modules"));
            var result = PlayerBundle.BuildForPlayer(_workingDir, _bundle, "App");
            Assert.AreEqual(PlayerBundle.Outcome.Skipped, result.Outcome);
            StringAssert.Contains("npm install", result.Reason);
            Assert.AreEqual("editor bundle", File.ReadAllText(_bundle));
        }

        [Test]
        public void ShipsTheBundleOnDiskWhenThereIsNoBuildScript() {
            File.WriteAllText(Path.Combine(_workingDir, "package.json"), "{ \"name\": \"app\", \"scripts\": { \"watch\": \"x\" } }");
            var result = PlayerBundle.BuildForPlayer(_workingDir, _bundle, "App");
            Assert.AreEqual(PlayerBundle.Outcome.Skipped, result.Outcome);
            StringAssert.Contains("build script", result.Reason);
        }

        [Test]
        public void SaysWhenAPlayerBundleStillCarriesReactsDevelopmentBuild() {
            // What a config from before oneJSConfig produces: it never passes
            // NODE_ENV on to esbuild, so React's development build ships anyway.
            BuildScript("require('fs').writeFileSync('../app.js.txt', '// node_modules/react/cjs/react.development.js')");
            var result = Build();
            Assert.IsTrue(result.DevelopmentReact);
            StringAssert.Contains("oneJSConfig", PlayerBundle.DevelopmentReactAdvice("App"));
        }

        [Test]
        public void RecognizesReactsDevelopmentBuildsInABundle() {
            Assert.IsTrue(PlayerBundle.CarriesDevelopmentReact("// node_modules/react/cjs/react.development.js\nvar x"));
            Assert.IsTrue(PlayerBundle.CarriesDevelopmentReact("// node_modules/react-reconciler/cjs/react-reconciler.development.js"));
            Assert.IsFalse(PlayerBundle.CarriesDevelopmentReact("// node_modules/react/cjs/react.production.js"));
        }
    }
}
