#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace OneJS {
    /// <summary>
    /// Says when a JSRunner's bundle may have changed, so live reload follows a rebuild within
    /// a few frames instead of on the next half-second poll.
    ///
    /// The operating system reports changes through a <see cref="FileSystemWatcher"/> (FSEvents on
    /// macOS, inotify on Linux), and none of them is trusted alone: Mono's watchers have dropped
    /// and doubled events on every platform, and a network drive or an exhausted inotify limit
    /// gives none at all. So:
    /// - An event is only a reason to look. The runner still compares the bundle's hash, so a
    ///   duplicate event, or one for a write that changed nothing, costs one hash and no reload.
    /// - It waits <see cref="Settle"/> after the last event before saying so. esbuild writes the
    ///   bundle in place, and a write of a megabyte can arrive as several events; reading after
    ///   the first would load half a bundle.
    /// - Underneath, every <see cref="SafetyInterval"/> it compares the file's size and write
    ///   time, so a missed event costs at most that delay, never the reload.
    /// - When the watcher cannot start, or reports an error, it polls at the runner's old poll
    ///   interval with the hash, exactly as live reload always did, and tries the watcher again.
    /// - On Windows, Unity's Mono gives FileSystemWatcher no operating system events at all: its
    ///   runtime picks DefaultWatcher, a managed thread that sleeps 750 ms and then compares every
    ///   file in the folder, so each event came about 0.76 s after the write, later than the
    ///   half-second poll it replaced (#155, 6000.5.2f1). Where Mono's watcher only polls like
    ///   that, the bundle's size and write time are compared on every look, at most every
    ///   <see cref="StatInterval"/>, and a change counts as an event. Its late events still
    ///   arrive, and still wake an unfocused editor. ReadDirectoryChangesW is not called directly:
    ///   its handle on the folder would stop that folder, and every folder above it, from being
    ///   renamed or moved while the preview runs.
    /// </summary>
    internal sealed class BundleWatcher : IDisposable {
        /// <summary>How long after the last event the bundle counts as written.</summary>
        public const double Settle = 0.05;
        /// <summary>How often the file's size and write time are compared while events arrive.</summary>
        public const double SafetyInterval = 2.0;
        /// <summary>How often the file's size and write time are compared where Mono's watcher only polls.</summary>
        public const double StatInterval = 0.03;
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
        double _nextStat;
        double _nextRetry;
        bool _comparesEveryLook;
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

        /// <summary>
        /// Whether the file is compared on every look because Mono's watcher only polls (always on
        /// Windows), rather than left to the watcher's events.
        /// </summary>
        public bool ComparesEveryLook => _watcher != null && _comparesEveryLook;

        /// <summary>The class Mono picked to serve the watcher, for tests and measurement.</summary>
        public string Backend { get; private set; } = "none";

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

            if (_comparesEveryLook && _watcher != null && now >= _nextStat) {
                _nextStat = now + StatInterval;
                if (StatChanged()) Interlocked.Exchange(ref _pendingEvent, Stopwatch.GetTimestamp());
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
                Backend = BackendOf(watcher);
#if UNITY_EDITOR_WIN
                _comparesEveryLook = true;
#else
                _comparesEveryLook = Backend == "DefaultWatcher";
#endif
            } catch (Exception) {
                // A network drive, an inotify watch limit, a platform without a watcher: poll.
                _watcher = null;
                Backend = "none";
                _nextRetry = Now + RetryInterval;
                Volatile.Write(ref _broken, 1);
            }
        }

        /// <summary>
        /// The class Mono picked to serve a FileSystemWatcher, from its private <c>watcher</c> field:
        /// CoreFXFileSystemWatcherProxy wraps the operating system's own watcher, DefaultWatcher
        /// polls. "unknown" if Mono's internals move, which then counts as an operating system's.
        /// </summary>
        static string BackendOf(FileSystemWatcher watcher) {
            try {
                var field = typeof(FileSystemWatcher).GetField("watcher", BindingFlags.Instance | BindingFlags.NonPublic);
                return field?.GetValue(watcher)?.GetType().Name ?? "unknown";
            } catch (Exception) {
                return "unknown";
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
