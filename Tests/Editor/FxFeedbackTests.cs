using System;
using NUnit.Framework;
using OneJS.Fx;
using UnityEngine;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// An fx chain fed back into the target it renders into, frame after frame,
    /// as an animation does with CreateTarget and ExecuteInto. Every pass reads
    /// the target before it at the same size, texel centre on texel centre, so a
    /// chain that changes nothing must hand back exactly what went in, however
    /// many frames it runs: the first frame is the golden.
    ///
    /// With anisotropic filtering forced on, a pooled target filtered at the
    /// default anisoLevel of 1 is filtered at 9, and Mesa's llvmpipe (the CI
    /// renderer) then reads a texel centre off by up to a fifth of a texel, as
    /// it did a shader element's history (ShaderEffectElement.EnsureHistory).
    /// A GPU reads it exactly either way. EditMode, so CI runs it on llvmpipe.
    /// </summary>
    [TestFixture]
    [Category("RequiresGraphics")]
    public class FxFeedbackTests {
        const int Size = 64;
        const int Frames = 60;

        // ops.ts in onejs-unity, which FxBridge decodes.
        const float WireVersion = 1, SourceTexture = 0, Multiply = 18, Flip = 114, Scalar = 0;

        AnisotropicFiltering _anisotropyBefore;
        Texture2D _seed;
        int _seedHandle, _target;

        [SetUp]
        public void SetUp() {
            _anisotropyBefore = QualitySettings.anisotropicFiltering;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            // Read once, on frame 0, and never filtered, so the first frame is
            // the seed exactly and only the targets are under test.
            _seed = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true) {
                filterMode = FilterMode.Point,
                anisoLevel = 0,
                wrapMode = TextureWrapMode.Clamp,
            };
            _seed.SetPixels(Checker());
            _seed.Apply();
            _seedHandle = FxBridge.WrapTexture(_seed);
            _target = FxBridge.CreateTarget(Size, Size);
        }

        [TearDown]
        public void TearDown() {
            FxBridge.Release(_target);
            FxBridge.Release(_seedHandle);
            UnityEngine.Object.DestroyImmediate(_seed);
            QualitySettings.anisotropicFiltering = _anisotropyBefore;
        }

        /// <summary>A one texel checker, red and green opposite, over a ramp in blue: a neighbour blended in shows at once.</summary>
        static Color[] Checker() {
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++) {
                for (int x = 0; x < Size; x++) {
                    float odd = (x + y) & 1;
                    px[y * Size + x] = new Color(odd, 1f - odd, y / (float)Size, 1f);
                }
            }
            return px;
        }

        [Test]
        public void AChainFedBackIntoItsTargetHoldsItsFirstFrame() {
            FxBridge.ExecuteInto(_target, new[] { WireVersion, 1, SourceTexture, 0, 1, _seedHandle });
            var golden = Read();
            Assert.AreEqual(Checker(), golden, "Frame 0 is not the seed, so there is nothing to hold.");

            // The target as the source, a fused pass that multiplies by 1, and
            // two spatial passes that flip and flip back: five reads a frame,
            // counting the copy in and the copy out.
            var chain = new[] {
                WireVersion, 4,
                SourceTexture, 0, 1, _target,
                Multiply, Scalar, 1, 1,
                Flip, 0, 2, 1, 0,
                Flip, 0, 2, 1, 0,
            };
            int firstDrift = -1;
            float worst = 0;
            int at = -1;
            for (int frame = 1; frame <= Frames; frame++) {
                FxBridge.ExecuteInto(_target, chain);
                if (firstDrift >= 0 && frame != Frames) continue;
                var px = Read();
                for (int t = 0; t < px.Length; t++) {
                    for (int c = 0; c < 4; c++) {
                        float d = Mathf.Abs(px[t][c] - golden[t][c]);
                        if (d <= worst) continue;
                        worst = d;
                        at = t;
                    }
                }
                if (worst > 0 && firstDrift < 0) firstDrift = frame;
            }
            Debug.Log($"[FxFeedbackTests] {SystemInfo.graphicsDeviceVersion} ({SystemInfo.graphicsDeviceName}), " +
                      $"anisotropic filtering {QualitySettings.anisotropicFiltering}, target anisoLevel " +
                      $"{FxBridge.GetTexture(_target).anisoLevel}, worst {worst} after {Frames} frames");
            Assert.AreEqual(0f, worst,
                $"A chain that changes nothing, fed back into its target, drifted from frame {firstDrift}: " +
                $"after {Frames} frames texel ({at % Size}, {at / Size}) is {worst} off.");
        }

        Color[] Read() {
            var rt = (RenderTexture)FxBridge.GetTexture(_target);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            var active = RenderTexture.active;
            try {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                return tex.GetPixels();
            } finally {
                RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
