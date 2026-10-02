using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// A JS error reaches the Console as a JSException whose stack is the JS
    /// frames in Unity's form, so the entry links to the script instead of to
    /// the C# line that logged it.
    /// </summary>
    public class JSExceptionTests {
        const char Marker = '\u0001';
        const char Source = '\u0002';

        const string Thrown = "TypeError: cannot read property 'x' of undefined\n" +
                              "    at onClick (app.js:120:15)\n" +
                              "    at <anonymous> (app.js:4:2)\n" +
                              "    at app.js:9";

        [SetUp]
        public void ResetTally() => JsLog.ResetErrorCount();

        [Test]
        public void FromText_KeepsTheMessageApartFromTheFrames() {
            var ex = JSException.FromText(Thrown);
            Assert.AreEqual("TypeError: cannot read property 'x' of undefined", ex.Message);
            StringAssert.StartsWith("    at onClick (app.js:120:15)", ex.JsStack);
        }

        [Test]
        public void StackTrace_IsTheJsFramesInUnitysLinkForm() {
            var ex = JSException.FromText(Thrown);
            Assert.AreEqual(
                "onClick () (at app.js:120)\n<anonymous> () (at app.js:4)\n<anonymous> () (at app.js:9)\n",
                ex.StackTrace);
        }

        [Test]
        public void Translate_RunsTheFramesThroughTheSourceMap() {
            var ex = JSException.FromText(Thrown).Translate(s => s.Replace("app.js:120:15", "Assets/App/~/index.tsx:12:3"));
            StringAssert.StartsWith("onClick () (at Assets/App/~/index.tsx:12)", ex.StackTrace);
            Assert.AreEqual("TypeError: cannot read property 'x' of undefined", ex.Message);
        }

        [Test]
        public void TextWithoutFrames_HasNone() {
            Assert.IsFalse(JSException.HasFrames("just a message"));
            Assert.IsTrue(JSException.HasFrames(Thrown));
            Assert.AreEqual("", JSException.FromText("SyntaxError: unexpected token").StackTrace);
        }

        [Test]
        public void ErrorLine_NamesItsBridge() {
            var level = JsLog.SplitLevel(Marker + "E" + Source + "7" + Source + "boom", out var body, out var id);
            Assert.AreEqual(JsLog.Level.Error, level);
            Assert.AreEqual("boom", body);
            Assert.AreEqual(7, id);
        }

        [Test]
        public void ErrorLineWithFrames_LogsAsAnExceptionLinkedToTheSource() {
            // Batchmode logs exceptions without a stack unless asked
            var stackType = Application.GetStackTraceLogType(LogType.Exception);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            var logged = new List<(string condition, string stack, LogType type)>();
            Application.LogCallback capture = (c, s, t) => logged.Add((c, s, t));
            JsLog.SetTranslator(91, s => s.Replace("app.js:120:15", "Assets/App/~/index.tsx:12:3"));
            Application.logMessageReceived += capture;
            try {
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex(@"JSException: \[QuickJS\] Event handler error: TypeError"));
                JsLog.Route(Marker + "E" + Source + "91" + Source + "Event handler error: " + Thrown);
            } finally {
                Application.logMessageReceived -= capture;
                JsLog.SetTranslator(91, null);
                Application.SetStackTraceLogType(LogType.Exception, stackType);
            }

            Assert.AreEqual(1, JsLog.ErrorCount);
            Assert.AreEqual(1, logged.Count);
            Assert.AreEqual(LogType.Exception, logged[0].type);
            StringAssert.StartsWith("onClick () (at Assets/App/~/index.tsx:12)", logged[0].stack);
        }

        [Test]
        public void ErrorLineWithoutFrames_StaysAPlainError() {
            LogAssert.Expect(LogType.Error, "[QuickJS] failed: 42");
            JsLog.Route(Marker + "E" + Source + "3" + Source + "failed: 42");
            Assert.AreEqual(1, JsLog.ErrorCount);
        }
    }
}
