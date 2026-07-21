using System;
using System.Collections;
using NUnit.Framework;
using OneJS;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Tests for the 2D particle engine: wire-schema parsing/validation (the C#-JS
/// contract, parity fixtures mirrored in onejs-react's particles.test.ts),
/// deterministic simulation, imperative API semantics, and an end-to-end render
/// smoke test that verifies the premultiplied additive path through a
/// RenderTexture panel readback.
/// </summary>
[TestFixture]
public class ParticleTests {
    // Minimal valid doc - shared fixture shape with particles.test.ts.
    const string kMinimalDoc = @"{""v"":1,""max"":100,""emitters"":[{""rate"":10}]}";

    static string Doc(string emitters, int max = 100, int seed = 0, int space = 0) =>
        $@"{{""v"":1,""max"":{max},""seed"":{seed},""space"":{space},""emitters"":[{emitters}]}}";

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
    public void WireParse_SortsCurveKeys() {
        var doc = ParticleWire.Parse(Doc(
            @"{""rate"":1,""sizeKeys"":[{""t"":1,""v"":0},{""t"":0,""v"":1},{""t"":0.5,""v"":2}]}"));
        var keys = doc.emitters[0].sizeKeys;
        Assert.AreEqual(0f, keys[0].t);
        Assert.AreEqual(0.5f, keys[1].t);
        Assert.AreEqual(1f, keys[2].t);
    }

    [Test]
    public void WireParse_RejectsBadDocs() {
        Assert.Throws<ArgumentException>(() => ParticleWire.Parse(null), "empty");
        Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":2,""max"":10,""emitters"":[{}]}"), "bad version");
        Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":1,""max"":0,""emitters"":[{}]}"), "max too small");
        Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":1,""max"":10,""emitters"":[]}"), "no emitters");
        Assert.Throws<ArgumentException>(() => ParticleWire.Parse(@"{""v"":1,""max"":10,""emitters"":[{""shape"":7}]}"), "bad shape");
        Assert.Throws<ArgumentException>(() => ParticleWire.Parse(Doc(
            @"{""sizeKeys"":[{},{},{},{},{},{},{},{},{}]}")), "too many curve keys");
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

    // MARK: Render smoke (end to end: shader from Resources, premultiplied additive path)

    [UnityTest]
    public IEnumerator Render_AdditiveParticles_PreserveBackground() {
        LogAssert.ignoreFailingMessages = true; // PanelSettings theme warning
        const int W = 256, H = 256;

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        ps.targetTexture = rt;
        ps.scaleMode = PanelScaleMode.ConstantPixelSize;
        ps.scale = 1f;

        var go = new GameObject("ParticleRenderSmoke");
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = ps;
        var root = doc.rootVisualElement;

        var bg = new VisualElement();
        bg.style.position = Position.Absolute;
        bg.style.left = 0;
        bg.style.top = 0;
        bg.style.width = W;
        bg.style.height = H;
        bg.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        root.Add(bg);

        var host = new VisualElement();
        host.style.position = Position.Absolute;
        host.style.left = 0;
        host.style.top = 0;
        host.style.width = W;
        host.style.height = H;
        root.Add(host);

        // Slow green additive particles clustered at the center so sampling is robust.
        var sys = ParticleBridge.Create(host, Doc(
            @"{""rate"":0,""speedMin"":0,""speedMax"":5,""lifeMin"":30,""lifeMax"":30,""sizeMin"":60,""sizeMax"":60,""additiveness"":1,
               ""colorKeys"":[{""t"":0,""r"":0,""g"":1,""b"":0,""a"":1}]}", max: 300), null);
        sys.Burst(0, W / 2f, H / 2f, 200);

        for (int i = 0; i < 10; i++) {
            sys.Tick(1f / 60f);
            yield return null;
        }
        yield return new WaitForEndOfFrame();

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        var center = (Color32)tex.GetPixel(W / 2, H / 2);
        var corner = (Color32)tex.GetPixel(4, 4);
        Debug.Log($"[ParticleRenderSmoke] center=({center.r},{center.g},{center.b}) corner=({corner.r},{corner.g},{corner.b})");

        sys.Dispose();
        UnityEngine.Object.Destroy(go);
        UnityEngine.Object.Destroy(ps);
        rt.Release();
        UnityEngine.Object.Destroy(rt);

        Assert.AreEqual(51, corner.r, 3, "corner should stay background gray");
        Assert.Greater(center.g, 150, "green particles should render at center");
        // The additive fingerprint: destination red/blue survive under the glow.
        // Normal alpha blending (the fallback path) would pull red below ~40.
        Assert.GreaterOrEqual(center.r, 45, "background red must be preserved (additive blend)");
        Assert.GreaterOrEqual(center.b, 45, "background blue must be preserved (additive blend)");
    }
}
