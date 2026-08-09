using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Helpers for RenderTextures that have to survive a graphics device reset.
    /// </summary>
    public static class RenderTextureUtils {
        /// <summary>
        /// Re-creates <paramref name="rt"/> if the graphics device dropped it.
        ///
        /// Unity treats a RenderTexture's GPU resource as "lost" after events
        /// like the system going into standby, an Android app being sent to the
        /// background, or a fullscreen toggle. The managed object survives and
        /// keeps its width/height, so a size comparison still says "no work to
        /// do", but IsCreated() has gone false and the pixels are gone.
        ///
        /// Blit and camera targets recover by themselves, because Unity creates
        /// a RenderTexture lazily when it is set as the active render target.
        /// Two uses never hit that path and so never recover:
        ///
        ///  - bound to a compute kernel as a UAV (ComputeShader.SetTexture),
        ///  - handed to UI Toolkit as a backgroundImage, which only samples it.
        ///
        /// Call this before either of those rather than only after a resize.
        /// Idempotent, and a live texture costs one bool check.
        /// </summary>
        public static void EnsureCreated(RenderTexture rt) {
            if (rt != null && !rt.IsCreated()) rt.Create();
        }
    }
}
