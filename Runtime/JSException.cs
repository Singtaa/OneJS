using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OneJS {
    /// <summary>
    /// An error thrown in JavaScript, as C# sees it. <see cref="Exception.Message"/>
    /// is the JS error's own text ("TypeError: x is undefined") and the stack
    /// trace is the JS one, source-mapped when the runner has a map.
    ///
    /// The stack is written the way Unity writes C# frames, so
    /// <c>Debug.LogException</c> shows the JS frames with their files as links,
    /// and the entry opens the user's script rather than the C# line that
    /// happened to run it.
    /// </summary>
    public class JSException : Exception {
        static readonly Regex FrameWithName = new Regex(@"^at\s+(.*?)\s+\((.+?):(\d+)(?::\d+)?\)$");
        static readonly Regex FrameBare = new Regex(@"^at\s+(.+?):(\d+)(?::\d+)?$");

        readonly string _jsStack;
        readonly string _unityStack;

        /// <param name="message">The JS error's text, without its frames.</param>
        /// <param name="jsStack">The JS frames, one "at f (file:line:col)" per line.</param>
        public JSException(string message, string jsStack, Exception innerException = null)
            : base(message, innerException) {
            _jsStack = jsStack ?? "";
            _unityStack = ToUnityFrames(_jsStack);
        }

        /// <summary>The JS frames as JS printed them ("    at f (file:line:col)").</summary>
        public string JsStack => _jsStack;

        /// <summary>The JS frames in Unity's form: "f () (at file:line)".</summary>
        public override string StackTrace => _unityStack;

        public override string ToString() =>
            string.IsNullOrEmpty(_jsStack) ? $"{GetType().FullName}: {Message}" : $"{GetType().FullName}: {Message}\n{_jsStack}";

        /// <summary>
        /// Splits QuickJS's error text, the message followed by its "at" lines,
        /// into a JSException. Lines before the first frame are the message.
        /// When the error is a C# exception the bridge threw into JS, that
        /// exception is the inner one, so a log of this shows the C# stack too.
        /// </summary>
        public static JSException FromText(string text) {
            Split(text, out var message, out var stack);
            return new JSException(message, stack, QuickJSNative.TakeDispatchCause(text));
        }

        /// <summary>True when <paramref name="text"/> carries at least one JS frame.</summary>
        public static bool HasFrames(string text) {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var line in text.Split('\n'))
                if (IsFrame(line)) return true;
            return false;
        }

        /// <summary>
        /// The same error with its frames run through <paramref name="translate"/>,
        /// a runner's source map lookup. Returns this one when there is nothing to do.
        /// </summary>
        public JSException Translate(Func<string, string> translate) {
            if (translate == null || string.IsNullOrEmpty(_jsStack)) return this;
            var translated = translate(_jsStack);
            return translated == _jsStack ? this : new JSException(Message, translated, InnerException);
        }

        static bool IsFrame(string line) => line.TrimStart().StartsWith("at ", StringComparison.Ordinal);

        static void Split(string text, out string message, out string stack) {
            var messageLines = new List<string>();
            var frames = new StringBuilder();
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n')) {
                if (IsFrame(raw)) frames.Append(raw.TrimEnd()).Append('\n');
                else if (frames.Length == 0) messageLines.Add(raw);
                // Text after the frames (rare) is not a frame and not the message
            }
            message = string.Join("\n", messageLines).Trim();
            stack = frames.ToString().TrimEnd('\n');
        }

        static string ToUnityFrames(string jsStack) {
            if (string.IsNullOrEmpty(jsStack)) return "";
            var sb = new StringBuilder();
            foreach (var raw in jsStack.Split('\n')) {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var m = FrameWithName.Match(line);
                if (m.Success) {
                    sb.Append(m.Groups[1].Value).Append(" () (at ").Append(m.Groups[2].Value)
                      .Append(':').Append(m.Groups[3].Value).Append(")\n");
                    continue;
                }
                m = FrameBare.Match(line);
                if (m.Success) {
                    sb.Append("<anonymous> () (at ").Append(m.Groups[1].Value)
                      .Append(':').Append(m.Groups[2].Value).Append(")\n");
                    continue;
                }
                sb.Append(line.Substring(line.StartsWith("at ") ? 3 : 0)).Append('\n');
            }
            return sb.ToString();
        }
    }
}
