#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace OneJS {
    /// <summary>
    /// Says when a JSRunner's bundle may have changed, so live reload follows a rebuild within
    /// about a tenth of a second instead of on the next half-second poll.
    ///
    /// It works the same way on every platform, so the tests that run on one test them all:
    /// - A thread of its own compares the bundle's size and write time every <see cref="StatInterval"/>.
    ///   A change wakes the editor, because an unfocused editor's update otherwise throttles to
    ///   almost nothing while focus is in the code editor.
    /// - A change is only a reason to look. The runner compares the bundle's hash, so a write
    ///   that changed nothing costs one hash and no reload.
    /// - It waits <see cref="Settle"/> after the last change it saw before saying so. esbuild
    ///   writes the bundle in place, and a large write can be seen half done; reading then would
    ///   load half a bundle.
    /// - While nothing changes, the runner hashes nothing. Hashing the bundle every half second
    ///   cost more CPU in an idle editor than the watching itself. The price: a rewrite that keeps
    ///   both the size and the write time (on a file system with timestamps of a second or more,
    ///   such as FAT or some network drives) is seen only with the next change.
    ///
    /// It does not use the operating system's events (a FileSystemWatcher). Under Unity's Mono those
    /// are three paths: FSEvents on macOS, which lag while other processes churn files; inotify on
    /// Linux; and on Windows a managed thread that looks every 750 ms, later than the half-second
    /// poll (#155, 6000.5.2f1). A stat every 30 ms costs microseconds, and holds no handle on the
    /// folder, so the folder and every folder above it can still be renamed while the preview runs.
    /// </summary>
    internal sealed class BundleWatcher : IDisposable {
        /// <summary>How long after the last change seen the bundle counts as written.</summary>
        public const double Settle = 0.05;
        /// <summary>How often the watching thread compares the bundle's size and write time.</summary>
        public const double StatInterval = 0.03;

        readonly string _path;
        readonly Action _wake;
        readonly ManualResetEvent _stop = new ManualResetEvent(false);
        readonly Thread _thread;

        // Owned by the watching thread once it starts. One FileInfo, refreshed on each look, since
        // making a new one each time cost about as much as the stat it wraps.
        FileInfo _info;
        long _length = -1;
        long _writeTicks;

        // Stopwatch timestamp of the latest change not yet acted on, 0 when there is none.
        // Written on the watching thread, read and cleared on the main thread.
        long _pendingChange;
        int _changes;

        /// <param name="path">The bundle.</param>
        /// <param name="wake">Called from the watching thread on each change, to wake an idle editor.</param>
        public BundleWatcher(string path, Action wake = null) {
            _path = path;
            _wake = wake;
            RecordStat();
            // A thread of its own rather than a System.Threading.Timer: under Unity's Mono each timer
            // tick goes through its scheduler thread and the thread pool, whose workers spin, which
            // cost 0.3 s of CPU a minute doing nothing and 1.1 to 1.9 s in the editor, against
            // 0.05 s for a thread that waits on an event (6000.5.2f1). One stat is 2.3 us.
            _thread = new Thread(Watch) { IsBackground = true, Name = "OneJS bundle watcher" };
            _thread.Start();
        }

        public string Path => _path;

        /// <summary>Changes the watching thread has seen, for tests and measurement.</summary>
        public int ChangeCount => Volatile.Read(ref _changes);

        /// <summary>Whether the runner should compare the bundle's hash now. Main thread, every frame.</summary>
        public bool ShouldCheck() {
            var pending = Interlocked.Read(ref _pendingChange);
            if (pending == 0) return false;
            if (Seconds(Stopwatch.GetTimestamp() - pending) < Settle) return false;
            // Only the change that was read is consumed; a newer one waits its own settle.
            return Interlocked.CompareExchange(ref _pendingChange, 0, pending) == pending;
        }

        void Watch() {
            var interval = Milliseconds(StatInterval);
            while (!_stop.WaitOne(interval)) {
                if (!StatChanged()) continue;
                Interlocked.Exchange(ref _pendingChange, Stopwatch.GetTimestamp());
                Interlocked.Increment(ref _changes);
                try {
                    _wake?.Invoke();
                } catch (Exception) {
                    // Waking the editor is a courtesy; the next frame looks anyway.
                }
            }
        }

        void RecordStat() {
            try {
                var info = _info ??= new FileInfo(_path);
                info.Refresh();
                _length = info.Exists ? info.Length : -1;
                _writeTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
            } catch (Exception) {
                _length = -1;
                _writeTicks = 0;
            }
        }

        bool StatChanged() {
            var length = _length;
            var write = _writeTicks;
            RecordStat();
            return length != _length || write != _writeTicks;
        }

        static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        static int Milliseconds(double seconds) => (int)Math.Round(seconds * 1000);

        public void Dispose() {
            _stop.Set();
            // It wakes at once; a stat stuck on an unanswering network drive is left to finish on
            // its own, since the thread is a background one and looks at nothing after the stop.
            _thread.Join(200);
        }
    }
}
#endif
