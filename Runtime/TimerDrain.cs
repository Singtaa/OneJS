using System;
using UnityEngine.Scripting;

namespace OneJS {
    /// <summary>
    /// The host's half of the bootstrap's timer drain on QuickJS. Timers due in
    /// one frame run one at a time, as a browser runs each timer as a task of
    /// its own, and between two of them the drain calls <see cref="RunPendingJobs"/>
    /// so the first one's awaits resume before the second starts. JavaScript
    /// cannot run QuickJS's job queue itself. On WebGL the drain runs each timer
    /// as a browser task instead and calls neither member.
    /// </summary>
    [Preserve]
    public static class TimerDrain {
        /// <summary>
        /// Runs the calling context's pending jobs (microtasks), as a browser
        /// does after each task. Does nothing outside a call from JavaScript.
        /// Returns how many ran, or -1 when one threw (already logged).
        /// </summary>
        [Preserve]
        public static int RunPendingJobs() {
            IntPtr ctx = QuickJSNative.CurrentContextPtr;
            return ctx == IntPtr.Zero ? 0 : QuickJSNative.qjs_execute_pending_jobs(ctx);
        }

        /// <summary>
        /// Whether the drain holds a frame's timer callbacks to its time budget.
        /// False while an offline renderer drives <see cref="VirtualClock"/>, so a
        /// recording does not depend on how fast the machine made it.
        /// </summary>
        [Preserve]
        public static bool Budgeted => !VirtualClock.IsActive;
    }
}
