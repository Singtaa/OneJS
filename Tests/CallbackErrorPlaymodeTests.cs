using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>A C# event a script subscribes to, raised from C#.</summary>
    public static class CallbackErrorHost {
        public static Action Fire;
    }

    /// <summary>
    /// A JS function C# calls (onPlay, an event handler bound to a C# delegate)
    /// that throws reaches the Console the way an Eval error does: one
    /// exception, the JSException's frames first and read through the
    /// runner's source map, under OneJS's "[Prefix] what failed" line.
    /// Before, the error was printed as an info line and the exception that
    /// followed only said "the JS callback threw an exception (see log)".
    /// </summary>
    [TestFixture]
    public class CallbackErrorPlaymodeTests {
        [TearDown]
        public void TearDown() {
            CallbackErrorHost.Fire = null;
            QuickJSNative.ClearAllHandles();
        }

        static List<(string condition, string stack, LogType type)> Capture(Action act) {
            var logged = new List<(string, string, LogType)>();
            Application.LogCallback capture = (c, s, t) => logged.Add((c, s, t));
            // Batchmode logs exceptions without a stack unless asked
            var stackType = Application.GetStackTraceLogType(LogType.Exception);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            Application.logMessageReceived += capture;
            try {
                act();
            } finally {
                Application.logMessageReceived -= capture;
                Application.SetStackTraceLogType(LogType.Exception, stackType);
            }
            return logged;
        }

        [Test]
        public void ThrowingOnPlay_LogsItsJsErrorAsTheException() {
            var host = new JsHost("Test", s => s.Replace("(app.js:", "(Assets/App/~/index.ts:"));
            try {
                host.Create(new VisualElement(), Application.persistentDataPath);
                host.Run("var __exports = {\n    onPlay: function () {\n        throw new Error('play boom')\n    },\n};", "app.js");

                LogAssert.Expect(LogType.Exception, new Regex(@"JSException: Error: play boom"));
                var logged = Capture(host.InvokeOnPlay);

                var entry = logged.Single(l => l.type == LogType.Exception);
                StringAssert.StartsWith("onPlay () (at Assets/App/~/index.ts:3)", entry.stack, "the first link opens the throwing line");
                StringAssert.Contains("Rethrow as Exception: [Test] onPlay() error", entry.stack);
                Assert.IsFalse(logged.Any(l => l.type == LogType.Log && l.condition.Contains("play boom")),
                    "the error is not also printed as an info line");
            } finally {
                host.Dispose();
            }
        }

        [Test]
        public void ThrowingDelegateHandler_LogsItsJsErrorAsTheException() {
            using (var ctx = new QuickJSContext()) {
                ctx.Eval("CS.OneJS.Tests.CallbackErrorHost.Fire = function () {\n    throw new Error('event boom')\n}", "handlers.js");

                LogAssert.Expect(LogType.Exception, new Regex(@"JSException: Error: event boom"));
                var logged = Capture(() => CallbackErrorHost.Fire());

                var entry = logged.Single(l => l.type == LogType.Exception);
                StringAssert.Contains("(at handlers.js:2)", entry.stack.Split('\n')[0], "the first link opens the throwing line");
                StringAssert.Contains("Rethrow as Exception: [QuickJS] callback failed", entry.stack);
                Assert.IsFalse(logged.Any(l => l.type == LogType.Log && l.condition.Contains("event boom")),
                    "the error is not also printed as an info line");
            }
        }
    }
}
