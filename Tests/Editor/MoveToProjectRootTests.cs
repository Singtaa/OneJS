using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using OneJS.Editor;
using UnityEngine;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Move to Project Root's folder move (Specs/ROOT_CARTS.md 5.9): git mv when the project's
    /// repository tracks the app's ~ or holds it as a submodule, so history follows the cart,
    /// and a plain move otherwise. Against temp repositories, never the project's own.
    /// </summary>
    [TestFixture]
    public class MoveToProjectRootTests {
        string _temp;

        [SetUp]
        public void SetUp() {
            _temp = Path.Combine(Path.GetTempPath(), "OneJSMoveToRoot_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
        }

        [TearDown]
        public void TearDown() {
            if (!Directory.Exists(_temp)) return;
            // git marks its objects read only, which Directory.Delete refuses on Windows.
            foreach (var file in Directory.GetFiles(_temp, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_temp, true);
        }

        static string Git(string dir, string arguments) {
            using var process = Process.Start(new ProcessStartInfo("git", arguments) {
                WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            });
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, $"git {arguments} in {dir}: {error}");
            return output;
        }

        static void Write(string path, string text) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        static string Repo(string dir) {
            Directory.CreateDirectory(dir);
            Git(dir, "init -q -b main");
            Git(dir, "config user.email test@example.com");
            Git(dir, "config user.name Test");
            Git(dir, "config commit.gpgsign false");
            return dir;
        }

        [Test]
        public void MovesAnUntrackedFolderPlainly() {
            var project = Path.Combine(_temp, "Project");
            var from = Path.Combine(project, "Assets", "Tinder", "~");
            Write(Path.Combine(from, "oj.json"), "{\"name\":\"Tinder\"}");
            var to = Path.Combine(project, "Tinder");

            Assert.AreEqual("moved", MoveToProjectRoot.MoveFolder(project, from, to));
            Assert.IsFalse(Directory.Exists(from));
            Assert.AreEqual("{\"name\":\"Tinder\"}", File.ReadAllText(Path.Combine(to, "oj.json")));
        }

        [Test]
        public void MovesATrackedFolderWithGitMvSoItsHistoryFollows() {
            var project = Repo(Path.Combine(_temp, "Project"));
            var from = Path.Combine(project, "Assets", "Tinder", "~");
            Write(Path.Combine(from, "index.tsx"), "export {}\n");
            Write(Path.Combine(from, "node_modules", "x", "index.js"), "");
            Write(Path.Combine(project, ".gitignore"), "node_modules\n");
            Git(project, "add -A");
            Git(project, "commit -q -m \"Take Tinder\"");
            var to = Path.Combine(project, "Tinder");

            Assert.AreEqual("git mv", MoveToProjectRoot.MoveFolder(project, from, to));
            // Untracked files move with the folder too: git mv renames the directory.
            Assert.IsTrue(File.Exists(Path.Combine(to, "node_modules", "x", "index.js")));
            StringAssert.Contains("R  Assets/Tinder/~/index.tsx -> Tinder/index.tsx", Git(project, "status --porcelain"));
            Git(project, "commit -q -m \"Move Tinder to the root\"");
            StringAssert.Contains("Take Tinder", Git(project, "log --follow --format=%s -- Tinder/index.tsx"));
        }

        [Test]
        public void MovesASubmoduleWithGitMvAndKeepsItWorking() {
            var cart = Repo(Path.Combine(_temp, "TinderCart"));
            Write(Path.Combine(cart, "oj.json"), "{\"name\":\"Tinder\"}");
            Git(cart, "add -A");
            Git(cart, "commit -q -m \"Tinder\"");
            var project = Repo(Path.Combine(_temp, "Project"));
            Write(Path.Combine(project, "Assets", ".keep"), "");
            Git(project, $"-c protocol.file.allow=always submodule add -q \"{cart.Replace('\\', '/')}\" \"Assets/Tinder/~\"");
            Git(project, "commit -q -m \"Tinder as a submodule\"");
            var from = Path.Combine(project, "Assets", "Tinder", "~");
            var to = Path.Combine(project, "Tinder");

            Assert.AreEqual("git mv, as a submodule", MoveToProjectRoot.MoveFolder(project, from, to));
            StringAssert.Contains("path = Tinder", File.ReadAllText(Path.Combine(project, ".gitmodules")));
            // The moved submodule is still a working repository with its history.
            StringAssert.Contains("Tinder", Git(to, "log --format=%s"));
            Assert.AreEqual("", Git(to, "status --porcelain"));
        }

        [Test]
        public void OffersNothingToARunnerThatIsNotAnApp() {
            Assert.IsNull(MoveToProjectRoot.PlanFor(null));
            var go = new GameObject("MoveToRootNoApp");
            try {
                Assert.IsNull(MoveToProjectRoot.PlanFor(go.AddComponent<JSRunner>()), "a runner with no Panel Settings has no app to move");
            } finally {
                Object.DestroyImmediate(go);
            }
        }
    }
}
