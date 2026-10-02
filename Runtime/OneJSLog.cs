using System;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// How OneJS logs an exception it caught: with Debug.LogException, so the
    /// stack survives and its files are links in the Console.
    ///
    /// The exception is wrapped under OneJS's own "[Prefix] what failed" line,
    /// which the Console shows as "Rethrow as Exception: ..." beneath the
    /// original's stack; the summary line stays the original's type and text.
    /// A JSException's frames are first read through the runner's source map,
    /// so its entry opens the script rather than the C# line that ran it.
    /// </summary>
    internal static class OneJSLog {
        public static void Exception(string message, Exception ex, UnityEngine.Object context = null,
            Func<string, string> translate = null) {
            if (ex is JSException js) ex = js.Translate(translate);
            Debug.LogException(new Exception(message, ex), context);
        }
    }
}
