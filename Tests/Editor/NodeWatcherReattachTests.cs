using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using OneJS.Editor;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// A watcher is reattached after a domain reload whether or not its runner asks for it
    /// (#143). One whose scene was closed when a reload happened used to be forgotten, missed
    /// by StopAll when the editor quit, and outlived the editor. A real node process stands in
    /// for the watcher, and the reload is the manager's table of watchers being emptied, which
    /// is what a reload does to it.
    /// </summary>
    [TestFixture]
    public class NodeWatcherReattachTests {
        string _dir;
        Process _node;

        [SetUp]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "OneJSWatcherReattach_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown() {
            NodeWatcherManager.StopWatcher(_dir);
            try { if (_node != null && !_node.HasExited) _node.Kill(); } catch (InvalidOperationException) { }
            _node?.Dispose();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        /// <summary>A node that runs until it is killed, from beside the npm the manager uses, or null where there is none.</summary>
        static Process StartNode(string dir) {
            string npm;
            try {
                npm = NodeWatcherManager.GetNpmExecutable();
            } catch (Exception) {
                return null;
            }
            var bin = Path.GetDirectoryName(npm ?? "");
            if (string.IsNullOrEmpty(bin)) return null;
            var node = Path.Combine(bin, Path.DirectorySeparatorChar == '\\' ? "node.exe" : "node");
            if (!File.Exists(node)) return null;
            try {
                // Its own working directory: one the editor's current directory names may be a
                // temp folder an earlier test deleted, and node exits at once in that.
                return Process.Start(new ProcessStartInfo(node, "-e \"setInterval(() => {}, 1000)\"") {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir,
                });
            } catch (Win32Exception) {
                return null;
            }
        }

        static void ForgetWatchers() {
            var table = (System.Collections.IDictionary)typeof(NodeWatcherManager)
                .GetField("_watchers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            table.Clear();
        }

        [Test]
        public void AWatcherIsReattachedAfterAReloadWithoutItsRunnerAsking_SoStopAllReachesIt() {
            _node = StartNode(_dir);
            if (_node == null) Assert.Ignore("No node beside npm on this machine to stand in for a watcher.");
            Assert.IsFalse(_node.WaitForExit(500), "the stand-in watcher keeps running");

            NodeWatcherManager.Track(_dir, _node);
            ForgetWatchers();
            Assert.IsFalse(NodeWatcherManager.IsRunning(_dir), "the reload forgot the watcher, as it does");

            NodeWatcherManager.ReattachAll();
            Assert.IsTrue(NodeWatcherManager.IsRunning(_dir), "reattached with no runner asking");

            NodeWatcherManager.StopWatcher(_dir);
            Assert.IsTrue(_node.WaitForExit(5000), "stopping the reattached watcher ended its process");
            Assert.IsFalse(NodeWatcherManager.IsTracked(_dir), "a stopped watcher is not reattached after the next reload");
        }
    }
}
