using System;
using System.Collections;
using NUnit.Framework;
using OneJS;
using OneJS.Tests;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// Tests for the 2D particle engine: wire-schema parsing/validation (the C#-JS
    /// contract, parity fixtures mirrored in onejs-react's particles.test.ts),
    /// deterministic simulation, imperative API semantics, and end-to-end render
    /// smoke tests that verify the premultiplied additive path and the non-square
    /// quad basis through a RenderTexture panel readback.
    /// </summary>
    [TestFixture]
    public class ParticleTests {
        // Minimal valid doc: shared fixture shape with particles.test.ts.
        const string kMinimalDoc = @"{""v"":4,""max"":100,""emitters"":[{""rate"":10}]}";

        static string Doc(string emitters, int max = 100, int seed = 0, int space = 0, int v = 4) =>
            $@"{{""v"":{v},""max"":{max},""seed"":{seed},""space"":{space},""emitters"":[{emitters}]}}";

        // MARK: Wire parsing

        [Test]
        public void WireParse_MinimalDoc_AppliesDefaults() {
            var doc = ParticleWire.Parse(kMinimalDoc);
            Assert.AreEqual(100, doc.max);
            Assert.AreEqual(1, doc.emitters.Length);
            var e = doc.emitters[0];
            Assert.AreEqual(10f, e.rate);
            Assert.IsTrue(e.emitting, "emitting should default to true");
            Assert.AreEqual(360f, e.angleMax, "angleMax should default to 360");
            Assert.AreEqual(1, e.colorKeys.Length, "colorKeys should default to one white key");
            Assert.AreEqual(1f, e.colorKeys[0].a);
            Assert.AreEqual(1, e.sizeKeys.Length, "sizeKeys should default to one key of 1");
            Assert.AreEqual(1f, e.sizeKeys[0].v);
        }

        [Test]
        public void WireParse_V2Fields_DefaultToV1Behavior() {
            // A v1 document from an older onejs-react must still describe a v1 system.
            var doc = ParticleWire.Parse(Doc(@"{""rate"":10}", v: 1));
            var e = doc.emitters[0];
            Assert.AreEqual(1f, e.aspectMin, "aspect defaults to square");
            Assert.AreEqual(1f, e.aspectMax);
            Assert.AreEqual(0f, e.attractStrength, "attraction is off by default");
            Assert.AreEqual(0, e.edge, "edge defaults to none");
            Assert.AreEqual(0.5f, e.bounciness);
            Assert.IsNull(e.tintPalette, "no palette means no per-particle tint");
        }

        [Test]
        public void WireParse_SortsCurveKeys() {
            var doc = ParticleWire.Parse(Doc(
                @"{""rate"":1,""sizeKeys"":[{""t"":1,""v"":0},{""t"":0,""v"":1},{""t"":0.5,""v"":2}]}"));
            var keys = doc.emitters[0].sizeKeys;
            Assert.AreEqual(0f, keys[0].t);
            Assert.AreEqual(0.5f, keys[1].t);
            Assert.AreEqual(1f, keys[2].t);
        }

        [Test]
        public void WireParse_ClampsAndOrdersRanges() {
            var doc = ParticleWire.Parse(Doc(
                @"{""aspectMin"":0.4,""aspectMax"":0.1,""attractStrength"":5,""bounciness"":-2}"));
            var e = doc.emitters[0];
            Assert.AreEqual(0.4f, e.aspectMax, "aspectMax is raised to aspectMin when inverted");
            Assert.AreEqual(1f, e.attractStrength, "attractStrength clamps to 0..1");
            Assert.AreEqual(0f, e.bounciness, "bounciness clamps to 0..1");
        }

        [Test]
        public void WireParse_V3Sheet_ResolvesFrameCountFromGrid() {
            // JS sends 0 and lets C# own cols*rows, so the two sides can't disagree.
            var e = ParticleWire.Parse(Doc(@"{""sheetCols"":4,""sheetRows"":4}")).emitters[0];
            Assert.AreEqual(16, e.sheetFrames, "0 should resolve to the full grid");

            var padded = ParticleWire.Parse(Doc(@"{""sheetCols"":4,""sheetRows"":4,""sheetFrames"":13}")).emitters[0];
            Assert.AreEqual(13, padded.sheetFrames, "an explicit count survives");

            var none = ParticleWire.Parse(Doc(@"{""rate"":1}")).emitters[0];
            Assert.AreEqual(1, none.sheetCols, "no sheet means a 1x1 grid");
            Assert.AreEqual(1, none.sheetFrames, "which resolves to a single frame, i.e. the v2 fast path");
            Assert.AreEqual(24f, none.sheetFps);
            Assert.IsFalse(none.sheetRandomStart);
        }

        [Test]
        public void WireParse_RejectsBadDocs() {
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(null), "empty");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":5,""max"":10,""emitters"":[{}]}"), "future version");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":0,""max"":10,""emitters"":[{}]}"), "version too old");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":2,""max"":0,""emitters"":[{}]}"), "max too small");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":2,""max"":10,""emitters"":[]}"), "no emitters");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":2,""max"":10,""emitters"":[{""shape"":7}]}"), "bad shape");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(@"{""edge"":9}")), "bad edge mode");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(@"{""attractEase"":9}")), "bad attract ease");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(
                @"{""sizeKeys"":[{},{},{},{},{},{},{},{},{}]}")), "too many curve keys");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(
                @"{""tintPalette"":[{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{}]}")), "too many palette colors");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(@"{""sheetCols"":0}")), "sheet dim below 1");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(@"{""sheetRows"":99}")), "sheet dim above the cap");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(@"{""sheetMode"":5}")), "bad sheet mode");
            Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(
                @"{""sheetCols"":2,""sheetRows"":2,""sheetFrames"":9}")), "frame count exceeds the grid");
        }

        // MARK: Flipbook frame selection (pure, no GPU)

        [Test]
        public void Sheet_FrameSelection_LifeModeSpreadsFramesOverLifetime() {
            var cfg = ParticleWire.Parse(Doc(@"{""sheetCols"":4,""sheetRows"":1}")).emitters[0];
            Assert.AreEqual(4, cfg.sheetFrames);
            // t is normalized life, so each quarter of the lifetime gets one frame.
            Assert.AreEqual(0, ParticleSystem2D.SheetFrame(cfg, 0f, 0f, 0));
            Assert.AreEqual(0, ParticleSystem2D.SheetFrame(cfg, 0f, 0.24f, 0));
            Assert.AreEqual(1, ParticleSystem2D.SheetFrame(cfg, 0f, 0.25f, 0));
            Assert.AreEqual(2, ParticleSystem2D.SheetFrame(cfg, 0f, 0.6f, 0));
            Assert.AreEqual(3, ParticleSystem2D.SheetFrame(cfg, 0f, 0.99f, 0));
        }

        [Test]
        public void Sheet_FrameSelection_FpsModeLoopsAndStaysInRange() {
            var cfg = ParticleWire.Parse(Doc(
                @"{""sheetCols"":2,""sheetRows"":2,""sheetMode"":1,""sheetFps"":10}")).emitters[0];
            Assert.AreEqual(4, cfg.sheetFrames);
            // Sampled mid-frame rather than on the boundary: the frame comes from a
            // truncation, so life*fps landing exactly on an integer is at the mercy of
            // float rounding (5.10f*10f is 50.999996, i.e. frame 50, not 51). Being one
            // frame early for one tick is invisible in an effect, but it makes exact
            // boundary assertions meaningless.
            Assert.AreEqual(0, ParticleSystem2D.SheetFrame(cfg, 0.05f, 0f, 0));
            Assert.AreEqual(2, ParticleSystem2D.SheetFrame(cfg, 0.25f, 0f, 0));
            Assert.AreEqual(3, ParticleSystem2D.SheetFrame(cfg, 0.35f, 0f, 0));
            Assert.AreEqual(0, ParticleSystem2D.SheetFrame(cfg, 0.45f, 0f, 0), "wraps after the last frame");
            Assert.AreEqual(3, ParticleSystem2D.SheetFrame(cfg, 5.15f, 0f, 0), "still in range far into life");
            for (float life = 0f; life < 3f; life += 0.017f)
                Assert.That(ParticleSystem2D.SheetFrame(cfg, life, 0f, 0), Is.InRange(0, 3));
        }

        [Test]
        public void Sheet_FrameSelection_RandomStartOffsetsWithoutEscapingRange() {
            var cfg = ParticleWire.Parse(Doc(@"{""sheetCols"":4,""sheetRows"":1}")).emitters[0];
            Assert.AreEqual(3, ParticleSystem2D.SheetFrame(cfg, 0f, 0f, 3), "offset applies at t=0");
            Assert.AreEqual(1, ParticleSystem2D.SheetFrame(cfg, 0f, 0.5f, 3), "2 + 3 wraps to 1");
            for (int start = 0; start < 4; start++)
                for (float t = 0f; t < 1f; t += 0.05f)
                    Assert.That(ParticleSystem2D.SheetFrame(cfg, 0f, t, start), Is.InRange(0, 3));
        }

        [Test]
        public void Sheet_FrameSelection_NoSheetAlwaysFrameZero() {
            var cfg = ParticleWire.Parse(Doc(@"{""rate"":1}")).emitters[0];
            Assert.AreEqual(0, ParticleSystem2D.SheetFrame(cfg, 9f, 0.9f, 7), "a 1x1 grid never animates");
        }

        // MARK: Simulation

        static ParticleSystem2D CreateSystem(string emitters, int max = 1000, int seed = 42) {
            var ve = new VisualElement();
            return ParticleBridge.Create(ve, Doc(emitters, max, seed), null);
        }

        [Test]
        public void Sim_EmitsAtConfiguredRate() {
            var sys = CreateSystem(@"{""rate"":100,""lifeMin"":10,""lifeMax"":10}");
            try {
                for (int i = 0; i < 60; i++)
                    sys.Tick(1f / 60f);
                // 100/s for 1s = ~100 alive (accumulator rounding allows +-1)
                Assert.That(sys.AliveCount, Is.InRange(99, 101));
            } finally {
                sys.Dispose();
            }
        }

        [Test]
        public void Sim_ParticlesDieAtLifetime() {
            var sys = CreateSystem(@"{""rate"":100,""lifeMin"":0.2,""lifeMax"":0.2}");
            try {
                for (int i = 0; i < 30; i++)
                    sys.Tick(1f / 60f); // 0.5s: population reaches steady state ~20
                Assert.That(sys.AliveCount, Is.InRange(15, 25));
                sys.StopEmitter(0);
                for (int i = 0; i < 30; i++)
                    sys.Tick(1f / 60f); // another 0.5s: everything expires
                Assert.AreEqual(0, sys.AliveCount);
            } finally {
                sys.Dispose();
            }
        }

        [Test]
        public void Sim_SameSeed_IsDeterministic() {
            var a = CreateSystem(@"{""rate"":200,""speedMin"":50,""speedMax"":150,""gravityY"":100}", seed: 7);
            var b = CreateSystem(@"{""rate"":200,""speedMin"":50,""speedMax"":150,""gravityY"":100}", seed: 7);
            try {
                for (int i = 0; i < 45; i++) {
                    a.Tick(1f / 60f);
                    b.Tick(1f / 60f);
                }
                Assert.Greater(a.AliveCount, 0);
                Assert.AreEqual(a.AliveCount, b.AliveCount);
                for (int i = 0; i < a.AliveCount; i++) {
                    Assert.AreEqual(a.GetParticleX(i), b.GetParticleX(i), 1e-5f);
                    Assert.AreEqual(a.GetParticleY(i), b.GetParticleY(i), 1e-5f);
                }
            } finally {
                a.Dispose();
                b.Dispose();
            }
        }

        // MARK: Attraction

        const string kAttractEmitter =
            @"{""rate"":0,""lifeMin"":1,""lifeMax"":1,""attractX"":100,""attractY"":0,""attractStrength"":1}";

        [Test]
        public void Attract_ParticleArrivesAtTargetByEndOfLife() {
            var sys = CreateSystem(kAttractEmitter);
            try {
                sys.Burst(0, 0f, 0f, 1);
                for (int i = 0; i < 59; i++) // stop one step short of expiry
                    sys.Tick(1f / 60f);
                Assert.AreEqual(1, sys.AliveCount);
                Assert.AreEqual(100f, sys.GetParticleX(0), 2f, "should land on the target x");
                Assert.AreEqual(0f, sys.GetParticleY(0), 2f, "should land on the target y");
            } finally {
                sys.Dispose();
            }
        }

        [Test]
        public void Attract_ZeroStrength_LeavesParticleOnFreePath() {
            var sys = CreateSystem(
                @"{""rate"":0,""lifeMin"":1,""lifeMax"":1,""attractX"":100,""attractY"":0,""attractStrength"":0}");
            try {
                sys.Burst(0, 0f, 0f, 1);
                for (int i = 0; i < 59; i++)
                    sys.Tick(1f / 60f);
                Assert.AreEqual(0f, sys.GetParticleX(0), 1e-4f, "no speed, no attraction: nothing moves");
            } finally {
                sys.Dispose();
            }
        }

        [Test]
        public void Attract_RetargetsAtRuntime() {
            var sys = CreateSystem(kAttractEmitter);
            try {
                sys.SetEmitterAttractor(0, -50f, 25f);
                sys.Burst(0, 0f, 0f, 1);
                for (int i = 0; i < 59; i++)
                    sys.Tick(1f / 60f);
                Assert.AreEqual(-50f, sys.GetParticleX(0), 2f);
                Assert.AreEqual(25f, sys.GetParticleY(0), 2f);
            } finally {
                sys.Dispose();
            }
        }

        // MARK: Texture grouping

        [Test]
        public void Textures_PerEmitterOverridesFormDrawGroups() {
            var ve = new VisualElement();
            var sys = ParticleBridge.Create(ve, Doc(@"{""rate"":1},{""rate"":1},{""rate"":1}"), null);
            var texA = new Texture2D(2, 2);
            var texB = new Texture2D(2, 2);
            try {
                Assert.AreEqual(1, sys.TextureGroupCount, "all emitters share the system texture");

                sys.SetEmitterTexture(1, texA);
                Assert.AreEqual(2, sys.TextureGroupCount);

                sys.SetEmitterTexture(2, texA);
                Assert.AreEqual(2, sys.TextureGroupCount, "emitters sharing a texture share a group");

                sys.SetEmitterTexture(2, texB);
                Assert.AreEqual(3, sys.TextureGroupCount);

                sys.SetEmitterTexture(1, null);
                sys.SetEmitterTexture(2, null);
                Assert.AreEqual(1, sys.TextureGroupCount, "null restores the system texture");
            } finally {
                sys.Dispose();
                UnityEngine.Object.DestroyImmediate(texA);
                UnityEngine.Object.DestroyImmediate(texB);
            }
        }

        [Test]
        public void Api_BurstClearPauseSemantics() {
            var sys = CreateSystem(@"{""rate"":0,""lifeMin"":10,""lifeMax"":10}");
            try {
                sys.Burst(0, 50, 50, 25);
                Assert.AreEqual(25, sys.AliveCount);

                sys.Pause();
                sys.Tick(1f);
                Assert.AreEqual(25, sys.AliveCount, "paused tick must not advance life");

                sys.Resume();
                sys.Clear();
                Assert.AreEqual(0, sys.AliveCount);

                sys.Burst(0, 0, 0, 999999);
                Assert.AreEqual(1000, sys.AliveCount, "burst must clamp at capacity");

                LogAssert.Expect(LogType.Warning, "[OneJS Particles] emitter index 5 out of range (0..0).");
                sys.Burst(5, 0, 0, 1); // out of range warns, does not throw
            } finally {
                sys.Dispose();
            }
        }

        [Test]
        public void Lifecycle_DisposeUnregisters() {
            var before = ParticleBridge.LiveSystemCount;
            var sys = CreateSystem(@"{""rate"":10}");
            Assert.AreEqual(before + 1, ParticleBridge.LiveSystemCount);
            sys.Dispose();
            Assert.AreEqual(before, ParticleBridge.LiveSystemCount);
            sys.Dispose(); // idempotent
            sys.Tick(1f); // no-op after dispose
            Assert.AreEqual(0, sys.AliveCount);
        }

        // MARK: Edge behavior
        // Edge collision and render readback need a real rect, so these run against
        // OneJS.Tests.PanelHost (Fixtures/PanelHost.cs), shared with ShaderFXTests.

        // Straight down at 200 px/s from the middle of a 200x200 host: unbounded it
        // would exit the bottom in half a second.
        static string EdgeEmitter(string edge) =>
            $@"{{""rate"":0,""lifeMin"":10,""lifeMax"":10,""angleMin"":90,""angleMax"":90,
                 ""speedMin"":200,""speedMax"":200,{edge}}}";

        [UnityTest]
        public IEnumerator Edge_KillReapsParticlesLeavingTheRect() {
            LogAssert.ignoreFailingMessages = true; // PanelSettings theme warning
            var ph = PanelHost.Create(200, 200);
            var host = ph.AddRect(200, 200);
            yield return null;
            yield return null; // let layout resolve contentRect

            var sys = ParticleBridge.Create(host, Doc(EdgeEmitter(@"""edge"":1")), null);
            sys.Burst(0, 100f, 100f, 5);
            Assert.AreEqual(5, sys.AliveCount);

            for (int i = 0; i < 60; i++)
                sys.Tick(1f / 60f);

            Assert.AreEqual(0, sys.AliveCount, "particles past the bottom edge should be reaped");

            sys.Dispose();
            ph.Destroy();
        }

        [UnityTest]
        public IEnumerator Edge_BounceKeepsParticlesInsideTheRect() {
            LogAssert.ignoreFailingMessages = true;
            var ph = PanelHost.Create(200, 200);
            var host = ph.AddRect(200, 200);
            yield return null;
            yield return null;

            var sys = ParticleBridge.Create(host, Doc(EdgeEmitter(@"""edge"":2,""bounciness"":0.5")), null);
            sys.Burst(0, 100f, 100f, 1);

            for (int i = 0; i < 120; i++) {
                sys.Tick(1f / 60f);
                Assert.That(sys.GetParticleY(0), Is.InRange(0f, 200f), $"escaped the rect on tick {i}");
            }
            Assert.AreEqual(1, sys.AliveCount, "bounce must not kill particles");
            Assert.Less(sys.GetParticleY(0), 200f, "should have rebounded off the bottom edge");

            sys.Dispose();
            ph.Destroy();
        }

        [UnityTest]
        public IEnumerator Edge_StickFreezesParticlesOnContact() {
            LogAssert.ignoreFailingMessages = true;
            var ph = PanelHost.Create(200, 200);
            var host = ph.AddRect(200, 200);
            yield return null;
            yield return null;

            var sys = ParticleBridge.Create(host, Doc(EdgeEmitter(@"""edge"":3,""gravityY"":500")), null);
            sys.Burst(0, 100f, 100f, 1);

            for (int i = 0; i < 60; i++)
                sys.Tick(1f / 60f);
            float settled = sys.GetParticleY(0);
            Assert.AreEqual(200f, settled, 0.001f, "should be pinned to the bottom edge");

            for (int i = 0; i < 60; i++)
                sys.Tick(1f / 60f);
            Assert.AreEqual(settled, sys.GetParticleY(0), 0.001f, "a stuck particle must not drift");
            Assert.AreEqual(1, sys.AliveCount, "it still ages and fades rather than dying on contact");

            sys.Dispose();
            ph.Destroy();
        }

        // MARK: Host styling guard

        // The panel emits its own theme warning, so match on our message rather than
        // asserting on the whole log stream.
        static System.Collections.Generic.List<string> CaptureLogs(Action action) {
            var msgs = new System.Collections.Generic.List<string>();
            Application.LogCallback handler = (cond, trace, type) => msgs.Add(cond);
            Application.logMessageReceived += handler;
            try { action(); } finally { Application.logMessageReceived -= handler; }
            return msgs;
        }

        const string kHostWarning = "[OneJS Particles] host element";

        [UnityTest]
        public IEnumerator Host_WarnsOnceWhenStyledWithBorderOrRadius() {
            LogAssert.ignoreFailingMessages = true;
            var ph = PanelHost.Create(200, 200);
            var host = ph.AddRect(200, 200);
            host.name = "StyledHost";
            host.style.borderTopLeftRadius = new StyleLength(8);
            host.style.borderTopWidth = new StyleFloat(1f);
            yield return null;
            yield return null;

            var sys = ParticleBridge.Create(host, Doc(@"{""rate"":10}"), null);
            var logs = CaptureLogs(() => {
                for (int i = 0; i < 5; i++) sys.Tick(1f / 60f);
            });

            var hits = logs.FindAll(m => m.Contains(kHostWarning));
            Assert.AreEqual(1, hits.Count, "styled host should warn exactly once, not once per tick");
            StringAssert.Contains("StyledHost", hits[0], "the warning should name the offending element");

            sys.Dispose();
            ph.Destroy();
        }

        [UnityTest]
        public IEnumerator Host_StaysQuietWhenUnstyled() {
            LogAssert.ignoreFailingMessages = true;
            var ph = PanelHost.Create(200, 200);
            var host = ph.AddRect(200, 200); // no border, no radius
            yield return null;
            yield return null;

            var sys = ParticleBridge.Create(host, Doc(@"{""rate"":10}"), null);
            var logs = CaptureLogs(() => {
                for (int i = 0; i < 5; i++) sys.Tick(1f / 60f);
            });

            Assert.IsEmpty(logs.FindAll(m => m.Contains(kHostWarning)),
                "an unstyled host is the documented correct usage and must not warn");

            sys.Dispose();
            ph.Destroy();
        }

        // MARK: Render smoke (end to end: shader from Resources, premultiplied additive path)

        [UnityTest]
        [Category("RequiresGraphics")]
        public IEnumerator Render_AdditiveParticles_PreserveBackground() {
            LogAssert.ignoreFailingMessages = true; // PanelSettings theme warning
            const int W = 256, H = 256;

            var ph = PanelHost.Create(W, H);
            ph.AddRect(W, H, new Color(0.2f, 0.2f, 0.2f, 1f));
            var host = ph.AddRect(W, H);
            yield return null;

            // Slow green additive particles clustered at the center so sampling is robust.
            var sys = ParticleBridge.Create(host, Doc(
                @"{""rate"":0,""speedMin"":0,""speedMax"":5,""lifeMin"":30,""lifeMax"":30,""sizeMin"":60,""sizeMax"":60,""additiveness"":1,
                   ""colorKeys"":[{""t"":0,""r"":0,""g"":1,""b"":0,""a"":1}]}", max: 300), null);
            sys.Burst(0, W / 2f, H / 2f, 200);

            for (int i = 0; i < 10; i++) {
                sys.Tick(1f / 60f);
                yield return null;
            }
            // Plain yields, never WaitForEndOfFrame, which batchmode may never
            // evoke: see Render_Sheet_AdvancesFrameOverLife and the CLAUDE.md
            // gotcha. Two frame boundaries commit the render for the readback.
            yield return null;
            yield return null;

            var center = ph.ReadPixel(W / 2, H / 2);
            var corner = ph.ReadPixel(4, 4);
            Debug.Log($"[ParticleRenderSmoke] center=({center.r},{center.g},{center.b}) corner=({corner.r},{corner.g},{corner.b})");

            sys.Dispose();
            ph.Destroy();

            Assert.AreEqual(51, corner.r, 3, "corner should stay background gray");
            Assert.Greater(center.g, 150, "green particles should render at center");
            // The additive fingerprint: destination red/blue survive under the glow.
            // Normal alpha blending (the fallback path) would pull red below ~40.
            Assert.GreaterOrEqual(center.r, 45, "background red must be preserved (additive blend)");
            Assert.GreaterOrEqual(center.b, 45, "background blue must be preserved (additive blend)");
        }

        /// <summary>
        /// The "BlendAdd" contract game VFX artists rely on: with Blend One OneMinusSrcAlpha,
        /// a single premultiplied sprite mixes additive and occluding regions per *texel* -
        /// alpha is the occlusion channel, RGB is the light contribution. A texel with
        /// rgb &gt; 0 and a = 0 adds to the background; a texel with a = 1 replaces it.
        /// This is independent of the per-emitter additiveness knob (held at 0 here).
        /// </summary>
        [UnityTest]
        [Category("RequiresGraphics")]
        public IEnumerator Render_PremultipliedTexture_MixesAdditiveAndOccludingPerTexel() {
            LogAssert.ignoreFailingMessages = true;
            const int W = 256, H = 256;
            const int CX = W / 2, CY = H / 2;

            // Green everywhere; left half a=0 (additive), right half a=255 (occluding).
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    px[y * 64 + x] = new Color32(0, 255, 0, (byte)(x < 32 ? 0 : 255));
            tex.SetPixels32(px);
            tex.Apply(false, false);

            var ph = PanelHost.Create(W, H);
            ph.AddRect(W, H, new Color(0.2f, 0.2f, 0.2f, 1f));
            var host = ph.AddRect(W, H);
            yield return null;

            // additiveness 0: the texture alone decides additive vs occluding.
            var sys = ParticleBridge.Create(host, Doc(
                @"{""rate"":0,""speedMin"":0,""speedMax"":0,""lifeMin"":30,""lifeMax"":30,
                   ""sizeMin"":200,""sizeMax"":200,""additiveness"":0,
                   ""colorKeys"":[{""t"":0,""r"":1,""g"":1,""b"":1,""a"":1}]}", max: 8), tex);
            sys.Burst(0, CX, CY, 1);

            for (int i = 0; i < 10; i++) {
                sys.Tick(1f / 60f);
                yield return null;
            }
            // Plain yields, never WaitForEndOfFrame, which batchmode may never
            // evoke: see Render_Sheet_AdvancesFrameOverLife and the CLAUDE.md
            // gotcha. Two frame boundaries commit the render for the readback.
            yield return null;
            yield return null;

            var additive = ph.ReadPixel(CX - 60, CY);  // left half of the quad
            var occluding = ph.ReadPixel(CX + 60, CY); // right half
            Debug.Log($"[ParticleBlendAdd] additive=({additive.r},{additive.g},{additive.b}) " +
                      $"occluding=({occluding.r},{occluding.g},{occluding.b})");

            sys.Dispose();
            ph.Destroy();
            UnityEngine.Object.DestroyImmediate(tex);

            Assert.Greater(additive.g, 150, "both halves should render green");
            Assert.Greater(occluding.g, 150, "both halves should render green");
            // The fingerprint: a=0 texels leave the destination intact underneath the glow,
            // a=1 texels replace it. Same sprite, same particle, same draw call.
            Assert.AreEqual(51, additive.r, 6, "a=0 texels must ADD (background red survives)");
            Assert.Less(occluding.r, 12, "a=1 texels must OCCLUDE (background red replaced)");
        }

        /// <summary>
        /// Flipbook: the quad's UVs narrow to one cell of the sheet, chosen from the
        /// particle's age. Two frames side by side (red, then green) let a readback
        /// prove the frame actually advances rather than sampling the whole sheet.
        /// </summary>
        [UnityTest]
        [Category("RequiresGraphics")]
        public IEnumerator Render_Sheet_AdvancesFrameOverLife() {
            LogAssert.ignoreFailingMessages = true;
            const int W = 256, H = 256;
            const int CX = W / 2, CY = H / 2;

            // 2x1 sheet: frame 0 red (left cell), frame 1 green (right cell).
            var sheet = new Texture2D(64, 32, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[64 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 64; x++)
                    px[y * 64 + x] = x < 32 ? new Color32(255, 0, 0, 255) : new Color32(0, 255, 0, 255);
            sheet.SetPixels32(px);
            sheet.Apply(false, false);

            var ph = PanelHost.Create(W, H);
            ph.AddRect(W, H, new Color(0f, 0f, 0.2f, 1f));
            var host = ph.AddRect(W, H);
            yield return null;

            // lifetime 1s, 2 frames over life: frame 0 while t < 0.5, frame 1 after.
            var sys = ParticleBridge.Create(host, Doc(
                @"{""rate"":0,""speedMin"":0,""speedMax"":0,""lifeMin"":1,""lifeMax"":1,
                   ""sizeMin"":90,""sizeMax"":90,""additiveness"":0,
                   ""sheetCols"":2,""sheetRows"":1,
                   ""colorKeys"":[{""t"":0,""r"":1,""g"":1,""b"":1,""a"":1}]}", max: 8), sheet);

            // Two particles of different ages in ONE frame, rather than one particle
            // read twice: batchmode never evokes WaitForEndOfFrame, so a second
            // readback would just re-read the first render.
            const int LEFT = 64, RIGHT = 192;
            sys.Burst(0, LEFT, CY, 1);                                   // ages to t ~ 0.6
            for (int i = 0; i < 36; i++) { sys.Tick(1f / 60f); yield return null; }
            sys.Burst(0, RIGHT, CY, 1);                                  // fresh, t ~ 0
            sys.Tick(1f / 60f);
            yield return null;
            yield return null;

            var older = ph.ReadPixel(LEFT, CY);
            var fresh = ph.ReadPixel(RIGHT, CY);
            Debug.Log($"[ParticleSheet] older=({older.r},{older.g},{older.b}) fresh=({fresh.r},{fresh.g},{fresh.b})");

            sys.Dispose();
            ph.Destroy();
            UnityEngine.Object.DestroyImmediate(sheet);

            Assert.Greater(fresh.r, 150, "a fresh particle samples frame 0 (red)");
            Assert.Less(fresh.g, 60, "frame 0 is not green");
            Assert.Greater(older.g, 150, "past half life it samples frame 1 (green)");
            Assert.Less(older.r, 60, "frame 1 is not red");
        }

        /// <summary>
        /// Pivot decides which point of the sprite sits on the particle position.
        /// Bottom-anchoring (pivot y = 0.5) is what stops a flame or fountain hanging
        /// below its source: the quad must sit entirely ABOVE the spawn point.
        /// </summary>
        [UnityTest]
        [Category("RequiresGraphics")]
        public IEnumerator Render_Pivot_BottomAnchorsTheQuadAboveTheSpawnPoint() {
            LogAssert.ignoreFailingMessages = true;
            const int W = 256, H = 256;
            const int CX = W / 2, CY = H / 2;

            var ph = PanelHost.Create(W, H);
            ph.AddRect(W, H, new Color(0f, 0f, 0f, 1f));
            var host = ph.AddRect(W, H);
            yield return null;

            // Two particles, size 80: one centered (default pivot), one bottom-anchored.
            // Spawned far apart horizontally so one readback covers both.
            const int CENTERED = 70, ANCHORED = 186;
            var sys = ParticleBridge.Create(host, Doc(
                @"{""rate"":0,""speedMin"":0,""speedMax"":0,""lifeMin"":30,""lifeMax"":30,
                   ""sizeMin"":80,""sizeMax"":80,""additiveness"":1,
                   ""colorKeys"":[{""t"":0,""r"":0,""g"":1,""b"":0,""a"":1}]},
                  {""rate"":0,""speedMin"":0,""speedMax"":0,""lifeMin"":30,""lifeMax"":30,
                   ""sizeMin"":80,""sizeMax"":80,""additiveness"":1,""pivotY"":0.5,
                   ""colorKeys"":[{""t"":0,""r"":0,""g"":1,""b"":0,""a"":1}]}", max: 8), null);
            sys.Burst(0, CENTERED, CY, 1);
            sys.Burst(1, ANCHORED, CY, 1);

            for (int i = 0; i < 6; i++) { sys.Tick(1f / 60f); yield return null; }
            // Plain yields, never WaitForEndOfFrame, which batchmode may never
            // evoke: see Render_Sheet_AdvancesFrameOverLife and the CLAUDE.md
            // gotcha. Two frame boundaries commit the render for the readback.
            yield return null;
            yield return null;

            // ReadPixel is top-down, so +26 is genuinely below the spawn row on screen:
            // inside a centered quad, outside a bottom-anchored one.
            var centeredBelow = ph.ReadPixel(CENTERED, CY + 26);
            var anchoredBelow = ph.ReadPixel(ANCHORED, CY + 26);
            var anchoredAbove = ph.ReadPixel(ANCHORED, CY - 26);
            Debug.Log($"[ParticlePivot] centeredBelow.g={centeredBelow.g} " +
                      $"anchoredBelow.g={anchoredBelow.g} anchoredAbove.g={anchoredAbove.g}");

            sys.Dispose();
            ph.Destroy();

            Assert.Greater(centeredBelow.g, 25, "a centered quad extends below the spawn point");
            Assert.Less(anchoredBelow.g, 8, "pivot y 0.5 must keep the quad off the area below the spawn point");
            Assert.Greater(anchoredAbove.g, 25, "the anchored quad still renders above it");
        }

        [UnityTest]
        [Category("RequiresGraphics")]
        public IEnumerator Render_Aspect_StretchesQuadsHorizontally() {
            LogAssert.ignoreFailingMessages = true;
            const int W = 256, H = 256;
            const int CX = W / 2, CY = H / 2;

            var ph = PanelHost.Create(W, H);
            ph.AddRect(W, H, new Color(0f, 0f, 0f, 1f));
            var host = ph.AddRect(W, H);
            yield return null;

            // size 40 with aspect 4 -> a 160x40 quad: 50px right of center is well
            // inside it, 50px below is well outside.
            var sys = ParticleBridge.Create(host, Doc(
                @"{""rate"":0,""speedMin"":0,""speedMax"":0,""lifeMin"":30,""lifeMax"":30,
                   ""sizeMin"":40,""sizeMax"":40,""aspectMin"":4,""aspectMax"":4,""additiveness"":1,
                   ""colorKeys"":[{""t"":0,""r"":0,""g"":1,""b"":0,""a"":1}]}", max: 64), null);
            sys.Burst(0, CX, CY, 40);

            for (int i = 0; i < 10; i++) {
                sys.Tick(1f / 60f);
                yield return null;
            }
            // Plain yields, never WaitForEndOfFrame, which batchmode may never
            // evoke: see Render_Sheet_AdvancesFrameOverLife and the CLAUDE.md
            // gotcha. Two frame boundaries commit the render for the readback.
            yield return null;
            yield return null;

            var right = ph.ReadPixel(CX + 50, CY);
            var below = ph.ReadPixel(CX, CY + 50);
            Debug.Log($"[ParticleAspect] right.g={right.g} below.g={below.g}");

            sys.Dispose();
            ph.Destroy();

            Assert.Greater(right.g, 30, "the quad should extend horizontally past 50px");
            Assert.Less(below.g, 10, "the quad should not extend vertically past 50px");
        }
    }
}
