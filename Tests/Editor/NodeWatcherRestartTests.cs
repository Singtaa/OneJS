using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using OneJS.Editor;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// A watcher's output reaches the editor after a domain reload. The editor reads a
    /// watcher's stdout and stderr through pipes, and a reload drops its end of them while
    /// the process runs on. A watcher that was only reattached by its pid then wrote into
    /// pipes nobody read: a clean rebuild prints nothing, so its first line was a build
    /// error, which never reached the Console and ended the watcher, and the fix to the
    /// error was never built. A real npm watch script stands in for esbuild; the reload is
    /// the manager's Process closed and its table emptied, which is what a reload does.
    /// </summary>
    [TestFixture]
    public class NodeWatcherRestartTests {
        const string WatchScript = @"
const fs = require('fs')
console.log('ready')
setInterval(() => {
    for (const n of [1, 2]) {
        const f = 'trigger' + n
        if (fs.existsSync(f)) { fs.rmSync(f); console.error('said ' + n) }
    }
}, 50)
";

        string _dir;

        [SetUp]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "OneJSWatcherRestart_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_dir, "node_modules"));
            File.WriteAllText(Path.Combine(_dir, "package.json"), "{ \"scripts\": { \"watch\": \"node watch.js\" } }\n");
            File.WriteAllText(Path.Combine(_dir, "watch.js"), WatchScript);
        }

        [TearDown]
        public void TearDown() {
            NodeWatcherManager.StopWatcher(_dir);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static System.Collections.IDictionary Watchers() =>
            (System.Collections.IDictionary)typeof(NodeWatcherManager)
                .GetField("_watchers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);

        /// <summary>What a domain reload does to the manager: its Process, and with it the editor's end of the pipes, is gone, and its table is empty.</summary>
        static void Reload() {
            var table = Watchers();
            foreach (Process process in table.Values) process.Close();
            table.Clear();
            NodeWatcherManager.ReattachAll();
        }

        bool OutputWithin(string text, int ms) {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms) {
                if (NodeWatcherManager.GetOutput(_dir).Contains(text)) return true;
                Thread.Sleep(50);
            }
            return NodeWatcherManager.GetOutput(_dir).Contains(text);
        }

        [Test]
        public void AfterAReload_AWatchersOutputStillReachesTheEditor_AndItKeepsRunning() {
            string npm = null;
            try { npm = NodeWatcherManager.GetNpmExecutable(); } catch (Exception) { }
            if (string.IsNullOrEmpty(npm) || !File.Exists(npm)) Assert.Ignore("No npm on this machine.");

            Assert.IsTrue(NodeWatcherManager.StartWatcher(_dir), "the watcher started");
            // Every line it prints reaches the editor, not only npm's first
            Assert.IsTrue(OutputWithin("ready", 15000), "its output was read: " + NodeWatcherManager.GetOutput(_dir));

            Reload();
            Assert.IsTrue(NodeWatcherManager.IsRunning(_dir), "a watcher runs after the reload");
            Assert.IsTrue(OutputWithin("ready", 15000), "it is ready again");

            File.WriteAllText(Path.Combine(_dir, "trigger1"), "");
            Assert.IsTrue(OutputWithin("said 1", 5000), "its first line after the reload, a build error in life, reached the editor");

            File.WriteAllText(Path.Combine(_dir, "trigger2"), "");
            Assert.IsTrue(OutputWithin("said 2", 5000), "it is still watching, so the fix that follows is built");
        }
    }
}
