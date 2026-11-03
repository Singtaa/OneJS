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
    /// - A timer thread compares the bundle's size and write time every <see cref="StatInterval"/>.
    ///   A change wakes the editor, because an unfocused editor's update otherwise throttles to
    ///   almost nothing while focus is in the code editor.
    /// - A change is only a reason to look. The runner compares the bundle's hash, so a write
    ///   that changed nothing costs one hash and no reload.
    /// - It waits <see cref="Settle"/> after the last change it saw before saying so. esbuild
    ///   writes the bundle in place, and a large write can be seen half done; reading then would
    ///   load half a bundle.
    /// - Every poll interval (the inspector's Poll Interval) the runner compares the hash anyway,
    ///   as live reload did before #155, so a write the size and write time cannot show (the same
    ///   size, on a file system with coarse timestamps) costs that interval, never the reload.
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
        /// <summary>How often the timer thread compares the bundle's size and write time.</summary>
        public const double StatInterval = 0.03;

        readonly string _path;
        readonly double _pollInterval;
        readonly Action _wake;
        readonly object _gate = new object();
        readonly Timer _timer;
        bool _disposed;

        // Owned by the timer thread once it starts: the callback is one-shot and re-armed after it
        // returns, so two never run at once.
        long _length = -1;
        long _writeTicks;

        // Stopwatch timestamp of the latest change not yet acted on, 0 when there is none.
        // Written on the timer thread, read and cleared on the main thread.
        long _pendingChange;
        int _changes;
        double _nextPoll;

        /// <param name="path">The bundle.</param>
        /// <param name="pollInterval">How often the runner compares the hash even when nothing seems to have changed.</param>
        /// <param name="wake">Called from the timer thread on each change, to wake an idle editor.</param>
        public BundleWatcher(string path, double pollInterval, Action wake = null) {
            _path = path;
            _pollInterval = Math.Max(0.05, pollInterval);
            _wake = wake;
            RecordStat();
            _nextPoll = Now + _pollInterval;
            // Armed only once the field is set, since the callback re-arms through it.
            _timer = new Timer(Look, null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(Milliseconds(StatInterval), Timeout.Infinite);
        }

        public string Path => _path;

        /// <summary>Changes the timer thread has seen, for tests and measurement.</summary>
        public int ChangeCount => Volatile.Read(ref _changes);

        /// <summary>Whether the runner should compare the bundle's hash now. Main thread, every frame.</summary>
        public bool ShouldCheck() {
            var pending = Interlocked.Read(ref _pendingChange);
            if (pending != 0) {
                if (Seconds(Stopwatch.GetTimestamp() - pending) < Settle) return false;
                // Only the change that was read is consumed; a newer one waits its own settle.
                if (Interlocked.CompareExchange(ref _pendingChange, 0, pending) != pending) return false;
                _nextPoll = Now + _pollInterval;
                return true;
            }

            var now = Now;
            if (now < _nextPoll) return false;
            _nextPoll = now + _pollInterval;
            return true;
        }

        void Look(object state) {
            try {
                if (StatChanged()) {
                    Interlocked.Exchange(ref _pendingChange, Stopwatch.GetTimestamp());
                    Interlocked.Increment(ref _changes);
                    try {
                        _wake?.Invoke();
                    } catch (Exception) {
                        // Waking the editor is a courtesy; the next frame looks anyway.
                    }
                }
            } finally {
                lock (_gate) {
                    if (!_disposed) _timer.Change(Milliseconds(StatInterval), Timeout.Infinite);
                }
            }
        }

        void RecordStat() {
            try {
                var info = new FileInfo(_path);
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

        static double Now => Seconds(Stopwatch.GetTimestamp());

        static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        static int Milliseconds(double seconds) => (int)Math.Round(seconds * 1000);

        public void Dispose() {
            lock (_gate) {
                if (_disposed) return;
                _disposed = true;
                _timer.Dispose();
            }
        }
    }
}
#endif
