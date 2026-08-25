using NUnit.Framework;
using OneJS;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The native console hands C# a bare string, so the bootstrap encodes the
    /// level into it. These cover the splitting and the routing, which is the
    /// part that decides whether a thrown React handler is visible to anything
    /// automated or arrives looking exactly like a debug print.
    /// </summary>
    public class JsLogSeverityTests {
        const char Marker = '\u0001';

        [SetUp]
        public void ResetTally() => JsLog.ResetErrorCount();

        [Test]
        public void PlainLine_IsLog_AndKeepsItsText() {
            var level = JsLog.SplitLevel("just a message", out var body);
            Assert.AreEqual(JsLog.Level.Log, level);
            Assert.AreEqual("just a message", body);
        }

        [Test]
        public void MarkedLines_SplitIntoLevelAndBody() {
            Assert.AreEqual(JsLog.Level.Error,
                JsLog.SplitLevel(Marker + "E" + "boom", out var err));
            Assert.AreEqual("boom", err);

            Assert.AreEqual(JsLog.Level.Warn,
                JsLog.SplitLevel(Marker + "W" + "careful", out var warn));
            Assert.AreEqual("careful", warn);
        }

        // An older bootstrap, or WebGL, sends unmarked text. It must still print
        // rather than lose its first characters to a marker that is not there.
        [Test]
        public void UnknownOrAbsentMarker_FallsBackToLogWithTextIntact() {
            Assert.AreEqual(JsLog.Level.Log,
                JsLog.SplitLevel(Marker + "Z" + "odd", out var odd));
            Assert.AreEqual(Marker + "Z" + "odd", odd);

            Assert.AreEqual(JsLog.Level.Log,
                JsLog.SplitLevel("", out var empty));
            Assert.AreEqual("", empty);

            Assert.AreEqual(JsLog.Level.Log,
                JsLog.SplitLevel(null, out var none));
            Assert.IsNull(none);
        }

        [Test]
        public void ErrorLine_LogsAsError_AndIsCounted() {
            LogAssert.Expect(LogType.Error, "[QuickJS] handler failed");
            JsLog.Route(Marker + "E" + "handler failed");

            Assert.AreEqual(1, JsLog.ErrorCount);
            Assert.AreEqual("handler failed", JsLog.LastError);
        }

        [Test]
        public void WarnLine_LogsAsWarning_AndIsNotCountedAsAnError() {
            LogAssert.Expect(LogType.Warning, "[QuickJS] deprecated");
            JsLog.Route(Marker + "W" + "deprecated");

            Assert.AreEqual(0, JsLog.ErrorCount);
            Assert.IsNull(JsLog.LastError);
        }

        // The assertion this whole change exists to make possible: drive
        // something, then ask whether JS raised anything.
        [Test]
        public void PlainLines_StayAtLogLevel_AndLeaveTheErrorTallyAtZero() {
            LogAssert.Expect(LogType.Log, "[QuickJS] mounting");
            LogAssert.Expect(LogType.Log, "[QuickJS] done");
            JsLog.Route("mounting");
            JsLog.Route("done");

            Assert.AreEqual(0, JsLog.ErrorCount);
        }
    }
}
