using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Live reload's change detection, on the real file system of whichever platform runs it.
    /// The watcher is one stat poll on a timer thread everywhere, so these test what every
    /// platform runs. Each change is made the way a build tool can make it: written in place
    /// (esbuild), deleted and written again, and written beside then moved over. A poll interval
    /// of 5 s keeps the runner's hash poll out of the timings, so a pass means the stat saw it.
    /// </summary>
    public class BundleWatcherTests {
        const double NoPoll = 5;

        string _dir;
        string _bundle;

        [SetUp]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "onejs-bundle-watcher-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _bundle = Path.Combine(_dir, "app.js.txt");
            File.WriteAllText(_bundle, "first");
        }

        [TearDown]
        public void TearDown() {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        /// <summary>Seconds until the watcher first says to look, or -1 when it never does within the timeout.</summary>
        static double SecondsUntilCheck(BundleWatcher watcher, double timeout) {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < timeout) {
                if (watcher.ShouldCheck()) return clock.Elapsed.TotalSeconds;
                Thread.Sleep(5);
            }
            return -1;
        }

        static int ChecksWithin(BundleWatcher watcher, double seconds) {
            var clock = Stopwatch.StartNew();
            var n = 0;
            while (clock.Elapsed.TotalSeconds < seconds) {
                if (watcher.ShouldCheck()) n++;
                Thread.Sleep(5);
            }
            return n;
        }

        static string ThisFile([CallerFilePath] string path = "") => path;

        /// <summary>
        /// One way to see a change, the same on every platform, so the suite that runs on Linux CI
        /// tests what macOS and Windows run too. Three paths (FSEvents, inotify, and Windows' poll)
        /// meant CI covered one of them (#155). Fails on a FileSystemWatcher, or on a platform branch.
        /// </summary>
        [Test]
        public void ThereIsOneWayToSeeAChangeOnEveryPlatform() {
            var fields = typeof(BundleWatcher).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var watchers = fields.Where(f => typeof(FileSystemWatcher).IsAssignableFrom(f.FieldType)).Select(f => f.Name).ToList();
            Assert.That(watchers, Is.Empty, "BundleWatcher holds a FileSystemWatcher");

            var source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile()), "..", "..", "Runtime", "BundleWatcher.cs"));
            Assert.IsTrue(File.Exists(source), $"BundleWatcher.cs not found at {source}, so nothing was compared");
            var code = File.ReadAllLines(source).Where(l => !l.TrimStart().StartsWith("//")).ToList();
            var branches = code.Where(l => Regex.IsMatch(l, @"UNITY_EDITOR_(WIN|OSX|LINUX)|UNITY_STANDALONE_|RuntimePlatform|Application\.platform|OSPlatform|FileSystemWatcher")).Select(l => l.Trim()).ToList();
            Assert.That(branches, Is.Empty, "BundleWatcher.cs branches on the platform or uses a FileSystemWatcher");
        }

        /// <summary>
        /// Each of five writes reaches the runner within a quarter second. A watcher whose events
        /// lag (Mono's on Windows looked every 750 ms) fails this, where a single write under a
        /// second passed it.
        /// </summary>
        [Test]
        public void EveryInPlaceWriteIsSeenWithinAQuarterSecond() {
            using var watcher = new BundleWatcher(_bundle, NoPoll);
            Thread.Sleep(100);
            var seen = new List<double>();
            for (int i = 0; i < 5; i++) {
                File.WriteAllText(_bundle, new string('w', 100 + i));
                seen.Add(SecondsUntilCheck(watcher, 3));
                Thread.Sleep(120 + 70 * i);
            }
            var report = $"seen after {string.Join(", ", seen.Select(x => x.ToString("F3")))} s, {watcher.ChangeCount} changes";
            TestContext.WriteLine(report);
            Assert.That(seen, Has.All.InRange(0.0, 0.25), report);
        }

        /// <summary>
        /// A write wakes the editor from the timer thread, with no frame asking: an unfocused
        /// editor's update throttles to almost nothing, so a watcher that only looks when the
        /// main thread asks would wait for focus.
        /// </summary>
        [Test]
        public void AWriteWakesTheEditorWithoutAFrame() {
            var wakes = 0;
            using var watcher = new BundleWatcher(_bundle, NoPoll, () => Interlocked.Increment(ref wakes));
            Thread.Sleep(100);
            var after = new List<double>();
            for (int i = 0; i < 5; i++) {
                var before = Volatile.Read(ref wakes);
                File.WriteAllText(_bundle, new string('k', 100 + i));
                var clock = Stopwatch.StartNew();
                while (Volatile.Read(ref wakes) == before && clock.Elapsed.TotalSeconds < 2) Thread.Sleep(1);
                after.Add(Volatile.Read(ref wakes) == before ? -1 : clock.Elapsed.TotalSeconds);
                Thread.Sleep(80);
            }
            Assert.That(after, Has.All.InRange(0.0, 0.2), $"woken after {string.Join(", ", after.Select(x => x.ToString("F3")))} s");
        }

        [Test]
        public void ABurstOfWritesIsOneCheckOnceItSettles() {
            using var watcher = new BundleWatcher(_bundle, NoPoll);
            Thread.Sleep(100);
            for (int i = 0; i < 20; i++) {
                File.WriteAllText(_bundle, new string('x', 1000 + i));
                Thread.Sleep(2);
            }
            var checks = ChecksWithin(watcher, 0.6);
            Assert.That(checks, Is.InRange(1, 2), $"{checks} checks for one burst of 20 writes ({watcher.ChangeCount} changes seen)");
        }

        [Test]
        public void ABundleDeletedAndWrittenAgainIsSeen() {
            using var watcher = new BundleWatcher(_bundle, NoPoll);
            Thread.Sleep(100);
            File.Delete(_bundle);
            Thread.Sleep(20);
            File.WriteAllText(_bundle, "recreated");
            var seconds = SecondsUntilCheck(watcher, 3);
            Assert.That(seconds, Is.InRange(0, 0.25), $"reported after {seconds:F3} s");
        }

        [Test]
        public void ABundleMovedOverTheOldOneIsSeen() {
            using var watcher = new BundleWatcher(_bundle, NoPoll);
            Thread.Sleep(100);
            var beside = Path.Combine(_dir, "app.js.txt.tmp");
            File.WriteAllText(beside, "moved over, longer");
            File.Delete(_bundle);
            File.Move(beside, _bundle);
            var seconds = SecondsUntilCheck(watcher, 3);
            Assert.That(seconds, Is.InRange(0, 0.25), $"reported after {seconds:F3} s");
        }

        [Test]
        public void ABundleInAFolderThatDoesNotExistYetIsSeenWhenItAppears() {
            var later = Path.Combine(_dir, "not-yet", "app.js.txt");
            using var watcher = new BundleWatcher(later, NoPoll);
            Thread.Sleep(100);
            Directory.CreateDirectory(Path.GetDirectoryName(later));
            File.WriteAllText(later, "built at last");
            var seconds = SecondsUntilCheck(watcher, 3);
            Assert.That(seconds, Is.InRange(0, 0.25), $"reported after {seconds:F3} s");
        }

        [Test]
        public void AnotherFileInTheFolderIsNotAReason() {
            using var watcher = new BundleWatcher(_bundle, NoPoll);
            Thread.Sleep(100);
            File.WriteAllText(Path.Combine(_dir, "app.js.map.txt"), "map");
            Assert.AreEqual(0, ChecksWithin(watcher, 0.4));
            Assert.AreEqual(0, watcher.ChangeCount);
        }

        /// <summary>
        /// A rewrite with the same size and, as a file system with coarse timestamps would report,
        /// the same write time is invisible to the stat, and the runner's hash poll still finds it.
        /// </summary>
        [Test]
        public void AWriteTheStatCannotSeeIsFoundByThePoll() {
            // The stat can land in the microseconds between the write and putting the write time
            // back, which tests nothing, so that try is made again.
            for (int attempt = 0; attempt < 3; attempt++) {
                var written = File.GetLastWriteTimeUtc(_bundle);
                using var watcher = new BundleWatcher(_bundle, 0.5);
                Thread.Sleep(100);
                File.WriteAllText(_bundle, attempt % 2 == 0 ? "fresh" : "first");
                File.SetLastWriteTimeUtc(_bundle, written);
                var seconds = SecondsUntilCheck(watcher, 2);
                if (watcher.ChangeCount != 0) continue;
                Assert.That(seconds, Is.InRange(0.3, 0.55), $"reported after {seconds:F3} s, the poll being due 0.4 s after the write");
                return;
            }
            Assert.Fail("the stat saw every try, so the poll was never what found the write");
        }

        [Test]
        public void WithNothingChangedItLooksOnlyAtThePollInterval() {
            using var watcher = new BundleWatcher(_bundle, 0.2);
            var checks = ChecksWithin(watcher, 0.75);
            Assert.That(checks, Is.InRange(2, 4), $"{checks} checks in 0.75 s at a 0.2 s interval");
            Assert.AreEqual(0, watcher.ChangeCount);
        }

        [Test]
        public void NothingIsSeenOrWokenAfterDispose() {
            var wakes = 0;
            var watcher = new BundleWatcher(_bundle, NoPoll, () => Interlocked.Increment(ref wakes));
            Thread.Sleep(100);
            watcher.Dispose();
            Thread.Sleep((int)(BundleWatcher.StatInterval * 2000));
            File.WriteAllText(_bundle, "after dispose");
            Thread.Sleep(300);
            Assert.AreEqual(0, Volatile.Read(ref wakes));
            Assert.AreEqual(0, watcher.ChangeCount);
        }
    }
}
