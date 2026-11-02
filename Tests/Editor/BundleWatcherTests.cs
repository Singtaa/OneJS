using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Live reload's change detection, on the real file system of whichever platform runs it:
    /// macOS, Windows and Linux each have their own watcher underneath (and Mono's have missed
    /// and doubled events on each, and on Windows only polls), so these run on all three. Each change is made the way a
    /// build tool can make it: written in place (esbuild), deleted and written again, and
    /// written beside then moved over.
    /// </summary>
    public class BundleWatcherTests {
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

        /// <summary>
        /// Each of five writes reaches the runner within a quarter second. A watcher whose events
        /// lag (Mono's DefaultWatcher, which Unity's Mono uses on Windows, sleeps 750 ms between
        /// looks) fails this, where a single write under a second passed it.
        /// </summary>
        [Test]
        public void EveryInPlaceWriteIsSeenWithinAQuarterSecond() {
            using var watcher = new BundleWatcher(_bundle, 0.5);
            Assert.IsTrue(watcher.IsWatching, "the watcher started");
            Thread.Sleep(100);
            var seen = new List<double>();
            for (int i = 0; i < 5; i++) {
                File.WriteAllText(_bundle, new string('w', 100 + i));
                seen.Add(SecondsUntilCheck(watcher, 3));
                Thread.Sleep(120 + 70 * i);
            }
            var report = $"{watcher.Backend}, compared every look: {watcher.ComparesEveryLook}; seen after {string.Join(", ", seen.Select(x => x.ToString("F3")))} s";
            TestContext.WriteLine(report);
            Assert.That(seen, Has.All.InRange(0.0, 0.25), report);
        }

        [Test]
        public void MonosChoiceOfWatcherIsOneThisKnows() {
            using var watcher = new BundleWatcher(_bundle, 0.5);
            // DefaultWatcher polls, so the file is compared on every look; the proxy wraps the
            // operating system's own watcher. Anything else means Mono's internals moved, and
            // BackendOf no longer tells a polling watcher apart.
            Assert.That(watcher.Backend, Is.AnyOf("DefaultWatcher", "CoreFXFileSystemWatcherProxy"));
            if (watcher.Backend == "DefaultWatcher") Assert.IsTrue(watcher.ComparesEveryLook);
        }

        [Test]
        public void ABurstOfWritesIsOneCheckOnceItSettles() {
            using var watcher = new BundleWatcher(_bundle, 0.5);
            Thread.Sleep(100);
            for (int i = 0; i < 20; i++) {
                File.WriteAllText(_bundle, new string('x', 1000 + i));
                Thread.Sleep(2);
            }
            var checks = ChecksWithin(watcher, 0.6);
            Assert.That(checks, Is.InRange(1, 2), $"{checks} checks for one burst of 20 writes ({watcher.EventCount} events)");
        }

        [Test]
        public void ABundleDeletedAndWrittenAgainIsSeen() {
            using var watcher = new BundleWatcher(_bundle, 0.5);
            Thread.Sleep(100);
            File.Delete(_bundle);
            Thread.Sleep(20);
            File.WriteAllText(_bundle, "recreated");
            var seconds = SecondsUntilCheck(watcher, 3);
            Assert.That(seconds, Is.InRange(0, BundleWatcher.SafetyInterval / 2), $"reported after {seconds:F3} s");
        }

        [Test]
        public void ABundleMovedOverTheOldOneIsSeen() {
            using var watcher = new BundleWatcher(_bundle, 0.5);
            Thread.Sleep(100);
            var beside = Path.Combine(_dir, "app.js.txt.tmp");
            File.WriteAllText(beside, "moved over, longer");
            File.Delete(_bundle);
            File.Move(beside, _bundle);
            var seconds = SecondsUntilCheck(watcher, 3);
            Assert.That(seconds, Is.InRange(0, BundleWatcher.SafetyInterval / 2), $"reported after {seconds:F3} s");
        }

        [Test]
        public void AnotherFileInTheFolderIsNotAReason() {
            using var watcher = new BundleWatcher(_bundle, 0.5);
            Thread.Sleep(100);
            File.WriteAllText(Path.Combine(_dir, "app.js.map.txt"), "map");
            Assert.AreEqual(0, ChecksWithin(watcher, 0.4));
        }

        [Test]
        public void AMissedEventCostsTheSafetyPollNotTheReload() {
            using var watcher = new BundleWatcher(_bundle, 0.5) { IgnoreEvents = true };
            Thread.Sleep(100);
            File.WriteAllText(_bundle, "changed while nobody was told");
            var seconds = SecondsUntilCheck(watcher, BundleWatcher.SafetyInterval + 1.5);
            Assert.That(seconds, Is.GreaterThanOrEqualTo(0), "the safety poll never saw the change");
            Assert.That(seconds, Is.LessThanOrEqualTo(BundleWatcher.SafetyInterval + 0.2));
        }

        [Test]
        public void TheSafetyPollIsQuietWhileNothingChanges() {
            using var watcher = new BundleWatcher(_bundle, 0.5);
            Assert.AreEqual(0, ChecksWithin(watcher, BundleWatcher.SafetyInterval + 0.3));
        }

        [Test]
        public void WithNoWatcherItPollsAtTheRunnersInterval() {
            // A folder that does not exist yet cannot be watched, which stands in for a network drive.
            var missing = Path.Combine(_dir, "not-yet", "app.js.txt");
            using var watcher = new BundleWatcher(missing, 0.2);
            Assert.IsFalse(watcher.IsWatching);
            var checks = ChecksWithin(watcher, 0.75);
            Assert.That(checks, Is.InRange(2, 4), $"{checks} checks in 0.75 s at a 0.2 s interval");
        }
    }
}
