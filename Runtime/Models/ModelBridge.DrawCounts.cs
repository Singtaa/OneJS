using Unity.Profiling;

namespace OneJS.Models {
    public static partial class ModelBridge {
        static ProfilerRecorder _drawCalls, _setPassCalls;

        /// <summary>
        /// The draw calls the last frame made, everything on screen counted (the UI's too), or -1
        /// until a frame has been counted. The first read starts the count, so a program that never
        /// asks pays nothing; it reads Unity's own render counters, which a release player keeps.
        /// </summary>
        public static int DrawCalls() => Last(ref _drawCalls, "Draw Calls Count");

        /// <summary>The set-pass calls the last frame made (each a change of shader pass), or -1 as <see cref="DrawCalls"/>.</summary>
        public static int SetPassCalls() => Last(ref _setPassCalls, "SetPass Calls Count");

        static int Last(ref ProfilerRecorder recorder, string counter) {
            if (!recorder.Valid) recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, counter, 1);
            return recorder.Valid && recorder.Count > 0 ? (int)recorder.LastValue : -1;
        }

        static void StopDrawCounts() {
            _drawCalls.Dispose();
            _setPassCalls.Dispose();
            _drawCalls = default;
            _setPassCalls = default;
        }
    }
}
