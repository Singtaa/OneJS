using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    /// <summary>Settings for a single <see cref="PanelRecorder"/> run.</summary>
    public sealed class PanelRecordingOptions {
        /// <summary>
        /// Width the panel is rendered at. This is the panel's viewport, so with a
        /// ConstantPixelSize PanelSettings it decides how much of the UI is in
        /// frame, not just the resolution. Rounded up to an even number (yuv420p).
        /// </summary>
        public int Width = 1280;

        /// <summary>Height the panel is rendered at. See <see cref="Width"/>.</summary>
        public int Height = 720;

        /// <summary>
        /// Encoded width. 0 keeps the capture width. Set this smaller than
        /// <see cref="Width"/> to supersample: render at 2x and encode at 1x for
        /// visibly cleaner text and particle edges at a smaller file size.
        /// </summary>
        public int OutputWidth;

        /// <summary>Encoded height. 0 keeps the capture height. See <see cref="OutputWidth"/>.</summary>
        public int OutputHeight;

        /// <summary>Frames per second of both the virtual clock step and the output file.</summary>
        public int Fps = 60;

        /// <summary>Length of the recorded clip in virtual seconds.</summary>
        public double DurationSeconds = 5.0;

        /// <summary>
        /// Virtual seconds stepped before recording starts, so layout, effects and
        /// any entry transition settle instead of appearing in frame one.
        /// </summary>
        public double SettleSeconds = 0.25;

        /// <summary>x264 quality. Lower is better and larger; 18-28 is the useful range.</summary>
        public int Crf = 23;

        /// <summary>Absolute path of the .mp4 to write. Required.</summary>
        public string OutputPath;

        /// <summary>Absolute path to ffmpeg. Leave null to auto-detect.</summary>
        public string FfmpegPath;

        /// <summary>
        /// Optional scripted pointer and keyboard input, delivered as real UI Toolkit
        /// events. Track time 0 is the first recorded frame, after the settle pass.
        /// </summary>
        public InputTrack Input;

        /// <summary>Draw a cursor for <see cref="Input"/>. Ignored when Input is null.</summary>
        public bool ShowCursor = true;

        /// <summary>Multiplier on the cursor size, which otherwise scales with the capture height.</summary>
        public float CursorScale = 1f;
    }

    /// <summary>
    /// Renders a running JSRunner's UI to an mp4 by stepping it on
    /// <see cref="VirtualClock"/> and piping raw frames into ffmpeg.
    ///
    /// Nothing is captured from the screen: frames come from an offscreen
    /// RenderTexture, so output is exact-sized, free of editor chrome, unaffected
    /// by window occlusion or the cursor, and identical on macOS and Windows.
    ///
    /// Because the clock is virtual rather than wall time, frame N always lands on
    /// virtual time N/Fps. Duration and frame count come out exact however slow
    /// rendering actually is, and rendering is typically far faster than realtime
    /// (10s of footage in well under a second). Note this pins <em>timing</em>, not
    /// app state: successive clips are only identical if the UI itself is
    /// deterministic, which anything driven by RNG (particles) or by React state
    /// carried over from previous frames is not.
    ///
    /// The run is synchronous and blocks the editor. That is deliberate: it keeps
    /// stepping exact (nothing else can tick the bridge mid-run) and the whole
    /// thing is over in seconds. A cancelable progress bar keeps the editor from
    /// looking hung.
    /// </summary>
    public static class PanelRecorder {
        /// <summary>
        /// Records <paramref name="runner"/> and returns the output path.
        /// Throws <see cref="OperationCanceledException"/> if the user cancels
        /// (the partial file is removed), or on any setup/encode failure.
        /// </summary>
        public static string Record(JSRunner runner, PanelRecordingOptions options) {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(options.OutputPath))
                throw new ArgumentException("OutputPath is required.", nameof(options));
            if (options.Fps < 1) throw new ArgumentException("Fps must be >= 1.", nameof(options));
            if (options.DurationSeconds <= 0)
                throw new ArgumentException("DurationSeconds must be > 0.", nameof(options));

            var bridge = runner.Bridge;
            if (bridge == null)
                throw new InvalidOperationException(
                    "JSRunner has no live bridge. Start edit-mode preview or enter play mode first.");

            var panelSettings = runner.PanelSettingsAsset;
            if (panelSettings == null)
                throw new InvalidOperationException("JSRunner has no PanelSettings assigned.");

            // yuv420p needs even dimensions; round up rather than silently cropping.
            int width = options.Width + (options.Width & 1);
            int height = options.Height + (options.Height & 1);
            int outWidth = options.OutputWidth > 0 ? options.OutputWidth + (options.OutputWidth & 1) : width;
            int outHeight = options.OutputHeight > 0 ? options.OutputHeight + (options.OutputHeight & 1) : height;

            var ffmpeg = FfmpegLocator.Resolve(options.FfmpegPath);
            var outputPath = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            int totalFrames = Mathf.Max(1, Mathf.RoundToInt((float)(options.DurationSeconds * options.Fps)));
            int settleFrames = Mathf.Max(0, Mathf.CeilToInt((float)(options.SettleSeconds * options.Fps)));
            double step = 1.0 / options.Fps;

            if (options.Input != null && options.Input.Duration > options.DurationSeconds + 1e-6)
                Debug.LogWarning(
                    $"[OneJS] Input track runs {options.Input.Duration:0.##}s but the recording is " +
                    $"{options.DurationSeconds:0.##}s, so the end of the track will not appear. " +
                    "Raise DurationSeconds to at least the track duration.");

            var stderr = new StringBuilder();
            Process encoder = null;
            OffscreenPanelRenderer renderer = null;
            Texture2D readback = null;
            CursorOverlay cursor = null;
            var previousActive = RenderTexture.active;
            bool canceled = false;

            VirtualClock.Begin();
            try {
                encoder = StartEncoder(ffmpeg, width, height, outWidth, outHeight, options, outputPath, stderr);
                renderer = new OffscreenPanelRenderer(panelSettings, width, height);
                readback = new Texture2D(width, height, TextureFormat.RGBA32, false);

                if (options.Input != null && options.ShowCursor) {
                    // Scale with the capture so the cursor reads the same on screen
                    // whether the panel was rendered at 720p or 4K.
                    cursor = new CursorOverlay(
                        Mathf.RoundToInt(height * 0.035f * Mathf.Max(0.1f, options.CursorScale)));
                }

                var frameBuffer = new byte[width * height * 4];
                var stdin = encoder.StandardInput.BaseStream;
                var fullRect = new Rect(0, 0, width, height);

                for (int i = 0; i < settleFrames; i++) {
                    VirtualClock.Advance(step);
                    bridge.Tick();
                }

                // A scripted demo may key an absolute timeline off the moment
                // capture begins; the settle pass above would otherwise silently
                // consume its opening beat. Optional by design: demos without
                // the hook (or with phase-agnostic motion) are unaffected.
                bridge.Eval("globalThis.__captureStart && globalThis.__captureStart()");

                // Input and layout are in the panel's logical space, which is not the
                // capture resolution when PanelSettings applies a scale. The cursor
                // composites into the pixel buffer, so it needs the conversion.
                var rootWidth = bridge.Root.worldBound.width;
                var uiToPixel = rootWidth > 1f ? width / rootWidth : 1f;

                double trackTime = 0.0;
                for (int frame = 0; frame < totalFrames; frame++) {
                    VirtualClock.Advance(step);

                    // Input before the tick, so anything it triggers is processed and
                    // rendered in this same frame rather than lagging by one.
                    if (options.Input != null) {
                        var previousTrackTime = trackTime;
                        trackTime += step;
                        options.Input.Step(bridge.Root, previousTrackTime, trackTime);
                    }

                    bridge.Tick();
                    renderer.Render();

                    RenderTexture.active = renderer.Texture;
                    readback.ReadPixels(fullRect, 0, 0);
                    // No Apply(): ReadPixels fills the CPU-side buffer, which is all
                    // GetRawTextureData reads. Apply would only re-upload to the GPU.
                    readback.GetRawTextureData<byte>().CopyTo(frameBuffer);

                    if (cursor != null)
                        cursor.Composite(frameBuffer, width, height,
                            options.Input.PointerPosition * uiToPixel,
                            options.Input.PointerIsDown,
                            options.Input.TimeSincePress);

                    stdin.Write(frameBuffer, 0, frameBuffer.Length);

                    if (encoder.HasExited)
                        throw new InvalidOperationException(
                            $"ffmpeg exited early (code {encoder.ExitCode}).\n{stderr}");

                    if ((frame & 3) == 0 && ReportProgress(frame, totalFrames)) {
                        canceled = true;
                        break;
                    }
                }

                stdin.Flush();
                stdin.Close();
                encoder.WaitForExit();

                if (!canceled && encoder.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"ffmpeg failed (code {encoder.ExitCode}).\n{stderr}");
            } finally {
                // Order matters: release the clock and restore the panel before
                // anything else can tick, so a throw mid-run cannot leave the
                // editor frozen on a stopped clock or rendering to a dead texture.
                VirtualClock.End();
                RenderTexture.active = previousActive;
                renderer?.Dispose();
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                EditorUtility.ClearProgressBar();
                DisposeEncoder(encoder);
            }

            if (canceled) {
                TryDelete(outputPath);
                throw new OperationCanceledException("[OneJS] Recording canceled.");
            }

            AssetDatabase.Refresh();
            var size = new FileInfo(outputPath).Length / 1024.0;
            var dims = (outWidth == width && outHeight == height)
                ? $"{width}x{height}"
                : $"{width}x{height} -> {outWidth}x{outHeight}";
            Debug.Log($"[OneJS] Recorded {totalFrames} frames " +
                      $"({options.DurationSeconds:0.##}s @ {options.Fps}fps, {dims}) " +
                      $"to {outputPath} ({size:0.#} KB)");
            return outputPath;
        }

        static Process StartEncoder(string ffmpeg, int width, int height,
                                    int outWidth, int outHeight,
                                    PanelRecordingOptions options, string outputPath,
                                    StringBuilder stderr) {
            // ReadPixels hands back bottom-up rows, so every chain starts with vflip.
            var filters = "vflip";
            if (outWidth != width || outHeight != height)
                filters += string.Format(CultureInfo.InvariantCulture,
                    ",scale={0}:{1}:flags=lanczos", outWidth, outHeight);

            // rawvideo in, H.264 out. yuv420p + faststart is the combination every
            // browser can decode and start playing before the file finishes loading.
            var args = string.Format(CultureInfo.InvariantCulture,
                "-hide_banner -loglevel error -y " +
                "-f rawvideo -pixel_format rgba -video_size {0}x{1} -framerate {2} -i - " +
                "-vf {3} " +
                "-c:v libx264 -preset slow -crf {4} -pix_fmt yuv420p -movflags +faststart -an " +
                "\"{5}\"",
                width, height, options.Fps, filters, options.Crf, outputPath);

            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = ffmpeg,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = false,
            };
            // Drained on a background thread so a chatty ffmpeg can never fill the
            // stderr pipe and deadlock us mid-write.
            process.ErrorDataReceived += (_, e) => {
                if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data);
            };
            process.Start();
            process.BeginErrorReadLine();
            return process;
        }

        /// <summary>Returns true if the user asked to cancel.</summary>
        static bool ReportProgress(int frame, int totalFrames) {
            return EditorUtility.DisplayCancelableProgressBar(
                "OneJS: Recording",
                $"Frame {frame + 1} / {totalFrames}",
                (float)(frame + 1) / totalFrames);
        }

        static void DisposeEncoder(Process encoder) {
            if (encoder == null) return;
            try {
                if (!encoder.HasExited) {
                    encoder.Kill();
                    encoder.WaitForExit(2000);
                }
            } catch (Exception) {
                // Already gone, or never started.
            }
            encoder.Dispose();
        }

        static void TryDelete(string path) {
            try {
                if (File.Exists(path)) File.Delete(path);
            } catch (Exception e) {
                Debug.LogWarning($"[OneJS] Could not remove partial recording '{path}': {e.Message}");
            }
        }
    }
}
