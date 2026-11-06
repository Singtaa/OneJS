using NUnit.Framework;
using OneJS.SL;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// SLProgramBridge.PendingCompiles, which onejs-play's container holds a cart's first frame on
    /// (runtime-1.3.md section 11). Outside a WebGL player a program compiles as it is made or
    /// there is no page, so nothing is ever pending and nothing natively waits on it.
    /// </summary>
    public class SLPendingCompilesTests {
        [Test]
        public void OutsideAWebGLPlayer_NothingIsPending_EvenWithProgramsHandedSource() {
            int a = SLProgramBridge.Upload("pending-a");
            int b = SLProgramBridge.Upload("pending-b");
            try {
                SLProgramBridge.SetWebSource(a, "@fragment fn sl_fs() {}", "void main() {}");
                Assert.That(SLProgramBridge.PendingCompiles, Is.EqualTo(0));
            } finally {
                SLProgramBridge.Release(a);
                SLProgramBridge.Release(b);
            }
        }
    }
}
