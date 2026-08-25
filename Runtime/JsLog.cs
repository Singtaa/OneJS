using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Routes a line of JS console output to the matching Unity log level, and
    /// keeps a tally of errors.
    ///
    /// The native console maps log, warn, error and info onto a single C#
    /// callback that takes nothing but a string, so the level is gone before C#
    /// sees it. The bootstrap encodes it into the message; this splits it back
    /// off. Before that, a React handler that threw and a debug print both
    /// arrived as Debug.Log, and anything keyed on Debug.LogError (a CI gate,
    /// LogAssert, a test asserting no errors) stayed green through both.
    ///
    /// Deliberately its own class rather than another QuickJSNative partial:
    /// that type's static constructor P/Invokes into the native library, so
    /// anything living on it can only be tested where that library loads. None
    /// of this needs the library, and a test for it should not need one either.
    /// </summary>
    public static class JsLog {
        /// <summary>Severity a line of JS console output arrived with.</summary>
        public enum Level { Log, Warn, Error }

        /// <summary>
        /// Marker the bootstrap prefixes onto a line to carry its level across a
        /// callback that only takes a string. A control character, so ordinary
        /// output can never be mistaken for one.
        /// </summary>
        public const char LevelMarker = '\u0001';

        static readonly object _lock = new object();
        static int _errorCount;
        static string _lastError;

        /// <summary>
        /// JS errors logged since the last reset. Zero is the useful assertion
        /// after driving an interaction: without it, "the handler ran" and "the
        /// handler worked" look identical from C#.
        /// </summary>
        public static int ErrorCount {
            get { lock (_lock) return _errorCount; }
        }

        /// <summary>The most recent JS error message, or null if there has been none.</summary>
        public static string LastError {
            get { lock (_lock) return _lastError; }
        }

        /// <summary>Clears the error tally and the last-error message.</summary>
        public static void ResetErrorCount() {
            lock (_lock) {
                _errorCount = 0;
                _lastError = null;
            }
        }

        /// <summary>
        /// Splits a level marker off a line. Anything without a recognised
        /// marker comes back untouched at Log level, so an unwrapped console (an
        /// older bootstrap, or WebGL, where the host page owns console) still
        /// prints in full rather than losing its first characters.
        /// </summary>
        public static Level SplitLevel(string raw, out string body) {
            body = raw;
            if (raw == null || raw.Length < 2 || raw[0] != LevelMarker) return Level.Log;
            Level level;
            switch (raw[1]) {
                case 'E': level = Level.Error; break;
                case 'W': level = Level.Warn; break;
                default: return Level.Log;
            }
            body = raw.Substring(2);
            return level;
        }

        /// <summary>Routes one line of console output to the matching Unity log level.</summary>
        public static void Route(string msg) {
            switch (SplitLevel(msg, out var body)) {
                case Level.Error:
                    lock (_lock) {
                        _errorCount++;
                        _lastError = body;
                    }
                    Debug.LogError("[QuickJS] " + body);
                    break;
                case Level.Warn:
                    Debug.LogWarning("[QuickJS] " + body);
                    break;
                default:
                    Debug.Log("[QuickJS] " + body);
                    break;
            }
        }
    }
}
