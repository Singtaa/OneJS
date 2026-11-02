#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace OneJS {
    /// <summary>
    /// Says when a JSRunner's bundle may have changed, so live reload follows a rebuild within
    /// a few frames instead of on the next half-second poll.
    ///
    /// The operating system reports changes (a <see cref="FileSystemWatcher"/>: FSEvents or
    /// kqueue on macOS, inotify on Linux, ReadDirectoryChangesW on Windows), and none of them is
    /// trusted alone: Mono's watchers have dropped and doubled events on every platform, and a
    /// network drive or an exhausted inotify limit gives none at all. So:
    /// - An event is only a reason to look. The runner still compares the bundle's hash, so a
    ///   duplicate event, or one for a write that changed nothing, costs one hash and no reload.
    /// - It waits <see cref="Settle"/> after the last event before saying so. esbuild writes the
    ///   bundle in place, and a write of a megabyte can arrive as several events; reading after
    ///   the first would load half a bundle.
    /// - Underneath, every <see cref="SafetyInterval"/> it compares the file's size and write
    ///   time, so a missed event costs at most that delay, never the reload.
    /// - When the watcher cannot start, or reports an error, it polls at the runner's old poll
    ///   interval with the hash, exactly as live reload always did, and tries the watcher again.
    /// </summary>
    internal sealed class BundleWatcher : IDisposable {
        /// <summary>How long after the last event the bundle counts as written.</summary>
        public const double Settle = 0.05;
        /// <summary>How often the file's size and write time are compared while events arrive.</summary>
        public const double SafetyInterval = 2.0;
        /// <summary>How often a broken watcher is started again.</summary>
        const double RetryInterval = 10.0;

        readonly string _path;
        readonly double _fallbackInterval;
        readonly Action _wake;

        FileSystemWatcher _watcher;
        // Stopwatch timestamp of the latest event not yet acted on, 0 when there is none.
        // Written from the watcher's thread pool thread, read on the main thread.
        long _pendingEvent;
        int _broken;
        int _events;
        double _nextLook;
        double _nextRetry;
        long _length = -1;
        long _writeTicks;

        /// <param name="path">The bundle.</param>
        /// <param name="fallbackInterval">How often to look when there are no events to go by.</param>
        /// <param name="wake">Called from the watcher's thread on each event, to wake an idle editor.</param>
        public BundleWatcher(string path, double fallbackInterval, Action wake = null) {
            _path = path;
            _fallbackInterval = Math.Max(0.05, fallbackInterval);
            _wake = wake;
            RecordStat();
            Start();
            _nextLook = Now + Interval;
        }

        public string Path => _path;

        /// <summary>Whether the operating system is reporting changes, rather than only the poll.</summary>
        public bool IsWatching => _watcher != null;

        /// <summary>Events received, for tests and measurement.</summary>
        public int EventCount => Volatile.Read(ref _events);

        /// <summary>For tests: drop every event, as a watcher that misses them would.</summary>
        internal bool IgnoreEvents { get; set; }

        /// <summary>Whether the runner should compare the bundle's hash now. Main thread, every frame.</summary>
        public bool ShouldCheck() {
            var now = Now;
            if (Volatile.Read(ref _broken) == 1 && now >= _nextRetry) {
                // The watcher said it lost events (an overflowed buffer, a vanished directory):
                // look now, and start a new one.
                Stop();
                Start();
                _nextRetry = now + RetryInterval;
                Interlocked.Exchange(ref _pendingEvent, Stopwatch.GetTimestamp());
            }

            var pending = Interlocked.Read(ref _pendingEvent);
            if (pending != 0) {
                if (Seconds(Stopwatch.GetTimestamp() - pending) < Settle) return false;
                // Only the event that was read is consumed; a newer one waits its own settle.
                if (Interlocked.CompareExchange(ref _pendingEvent, 0, pending) != pending) return false;
                RecordStat();
                _nextLook = now + Interval;
                return true;
            }

            if (now < _nextLook) return false;
            _nextLook = now + Interval;
            if (_watcher == null) {
                RecordStat();
                return true;
            }
            return StatChanged();
        }

        double Interval => _watcher != null ? SafetyInterval : _fallbackInterval;

        void Start() {
            Volatile.Write(ref _broken, 0);
            try {
                var dir = System.IO.Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                var watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(_path)) {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                };
                watcher.Changed += OnEvent;
                watcher.Created += OnEvent;
                watcher.Deleted += OnEvent;
                watcher.Renamed += OnEvent;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            } catch (Exception) {
                // A network drive, an inotify watch limit, a platform without a watcher: poll.
                _watcher = null;
                _nextRetry = Now + RetryInterval;
                Volatile.Write(ref _broken, 1);
            }
        }

        void Stop() {
            var watcher = _watcher;
            _watcher = null;
            if (watcher == null) return;
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnEvent;
            watcher.Created -= OnEvent;
            watcher.Deleted -= OnEvent;
            watcher.Renamed -= OnEvent;
            watcher.Error -= OnError;
            watcher.Dispose();
        }

        void OnEvent(object sender, FileSystemEventArgs e) {
            if (IgnoreEvents) return;
            Interlocked.Exchange(ref _pendingEvent, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref _events);
            try {
                _wake?.Invoke();
            } catch (Exception) {
                // Waking the editor is a courtesy; the next frame looks anyway.
            }
        }

        void OnError(object sender, ErrorEventArgs e) {
            Volatile.Write(ref _broken, 1);
        }

        void RecordStat() {
            try {
                var info = new FileInfo(_path);
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

        static double Now => Seconds(Stopwatch.GetTimestamp());

        static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        public void Dispose() => Stop();
    }
}
#endif
