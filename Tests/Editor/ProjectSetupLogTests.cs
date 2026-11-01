using NUnit.Framework;
using OneJS.Editor;
using UnityEngine;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// ProjectSetup.Initialize is mostly called from a batch-mode editor, whose log
    /// prints a stack trace under every info line. A 31 Oct cold agent read the
    /// traces under "Created default file" as trouble and cloned the OneJS repo to
    /// find out why. Info lines go out bare during setup; warnings and errors keep
    /// their traces, and everything is put back afterwards.
    /// </summary>
    public class ProjectSetupLogTests {
        [Test]
        public void SetupLogsInfoLinesWithoutStackTracesAndPutsTracesBack() {
            var info = Application.GetStackTraceLogType(LogType.Log);
            var warning = Application.GetStackTraceLogType(LogType.Warning);
            var error = Application.GetStackTraceLogType(LogType.Error);
            var during = info;
            var warningDuring = StackTraceLogType.None;
            var errorDuring = StackTraceLogType.None;

            ProjectSetup.WithBareInfoLogs(() => {
                during = Application.GetStackTraceLogType(LogType.Log);
                warningDuring = Application.GetStackTraceLogType(LogType.Warning);
                errorDuring = Application.GetStackTraceLogType(LogType.Error);
            });

            Assert.AreEqual(StackTraceLogType.None, during, "info lines carry no trace during setup");
            Assert.AreEqual(warning, warningDuring, "warnings keep theirs");
            Assert.AreEqual(error, errorDuring, "errors keep theirs");
            Assert.AreEqual(info, Application.GetStackTraceLogType(LogType.Log), "and info lines get theirs back");
        }

        [Test]
        public void PutsTracesBackWhenSetupThrows() {
            var info = Application.GetStackTraceLogType(LogType.Log);
            Assert.Throws<System.InvalidOperationException>(() =>
                ProjectSetup.WithBareInfoLogs(() => throw new System.InvalidOperationException("setup failed")));
            Assert.AreEqual(info, Application.GetStackTraceLogType(LogType.Log));
        }
    }
}
