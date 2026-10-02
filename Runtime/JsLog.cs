using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Brackets the id of the bridge an error line came from, right after
        /// its level ("\u0001E\u00023\u0002message"), so the line can be read
        /// through that bridge's source map. Lines without it read untranslated.
        /// </summary>
        public const char SourceMarker = '\u0002';

        static readonly Dictionary<int, Func<string, string>> _translators = new Dictionary<int, Func<string, string>>();

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

        /// <summary>
        /// Sets how error lines from bridge <paramref name="sourceId"/> map JS
        /// positions to source lines; null removes it. The bridge owns this:
        /// it sets it from its runner and clears it when disposed.
        /// </summary>
        internal static void SetTranslator(int sourceId, Func<string, string> translate) {
            lock (_lock) {
                if (translate == null) _translators.Remove(sourceId);
                else _translators[sourceId] = translate;
            }
        }

        /// <summary>The translator bridge <paramref name="sourceId"/> set, or null.</summary>
        internal static Func<string, string> TranslatorFor(int sourceId) {
            lock (_lock) return _translators.TryGetValue(sourceId, out var t) ? t : null;
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
        public static Level SplitLevel(string raw, out string body) => SplitLevel(raw, out body, out _);

        /// <summary>
        /// <see cref="SplitLevel(string, out string)"/>, also returning the id of
        /// the bridge the line names, or 0 when it names none.
        /// </summary>
        public static Level SplitLevel(string raw, out string body, out int sourceId) {
            sourceId = 0;
            body = raw;
            if (raw == null || raw.Length < 2 || raw[0] != LevelMarker) return Level.Log;
            Level level;
            switch (raw[1]) {
                case 'E': level = Level.Error; break;
                case 'W': level = Level.Warn; break;
                default: return Level.Log;
            }
            body = raw.Substring(2);
            if (body.Length > 0 && body[0] == SourceMarker) {
                var end = body.IndexOf(SourceMarker, 1);
                if (end > 1 && int.TryParse(body.Substring(1, end - 1), out var id)) {
                    sourceId = id;
                    body = body.Substring(end + 1);
                }
            }
            return level;
        }

        /// <summary>Routes one line of console output to the matching Unity log level.</summary>
        public static void Route(string msg) {
            switch (SplitLevel(msg, out var body, out var sourceId)) {
                case Level.Error:
                    Func<string, string> translate;
                    lock (_lock) {
                        _errorCount++;
                        _lastError = body;
                        _translators.TryGetValue(sourceId, out translate);
                    }
                    // An error with JS frames (a thrown Error, an unhandled
                    // rejection) goes out as an exception whose stack is those
                    // frames, so the Console links to the script instead of to
                    // this line. A plain message stays a plain error.
                    if (JSException.HasFrames(body))
                        Debug.LogException(JSException.FromText("[QuickJS] " + body).Translate(translate));
                    else
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
