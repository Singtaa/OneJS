using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge.SetSky, read back from the scene's camera: the gradient's three colours where
    /// they belong, the sun's disc where the sun shines from and moving with it, the horizon fading
    /// into the fog, stars, and SetSky(false) giving the background colour back.
    /// </summary>
    public class SkyEditModeTests {
        // Odd, so the centre pixel looks exactly where the camera does.
        const int Size = 65;
        static readonly Color Top = new(0.1f, 0.2f, 0.6f);
        static readonly Color Horizon = new(0.95f, 0.65f, 0.4f);
        static readonly Color Bottom = new(0.3f, 0.3f, 0.3f);

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        static void Sky(float sunSize = 0, float sunGlow = 0, float stars = 0) => ModelBridge.SetSky(true,
            Top.r, Top.g, Top.b, Horizon.r, Horizon.g, Horizon.b, Bottom.r, Bottom.g, Bottom.b, sunSize, sunGlow, stars);

        static Color[] Render(Vector3 look) {
            ModelBridge.SetCamera(0, 0, 0, look.x, look.y, look.z, 60);
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            try {
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = was;
                return read.GetPixels();
            } finally {
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        static Color Centre(Vector3 look) => Render(look)[Size / 2 * Size + Size / 2];

        // The read-back is linear; the colours were given as sRGB.
        static void Near(Color got, Color srgb, string what, float within = 0.04f) {
            var want = srgb.linear;
            Assert.That(Mathf.Abs(got.r - want.r) + Mathf.Abs(got.g - want.g) + Mathf.Abs(got.b - want.b), Is.LessThan(within * 3),
                $"{what}: {got} against {want}");
        }

        [Test]
        public void TheGradient_IsTheTopOverhead_TheHorizonLevel_AndTheBottomUnderfoot() {
            Sky();
            Assert.That(ModelBridge.SceneCamera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));
            Near(Centre(new Vector3(0, 100, 1)), Top, "overhead");
            Near(Centre(new Vector3(0, 0, 10)), Horizon, "level");
            Near(Centre(new Vector3(0, -100, 1)), Bottom, "underfoot");
        }

        [Test]
        public void TheSunsDisc_IsWhereTheSunShinesFrom_InItsColour_AndMovesWithIt() {
            // Shining down and along +z, so it sits up and behind, toward -z.
            ModelBridge.SetSun(true, 0, -1, 1, 1, 0, 0, 1, false);
            Sky(sunSize: 4);
            var toSun = new Vector3(0, 1, -1);
            Near(Centre(toSun), Color.red, "the disc");
            Near(Centre(new Vector3(0, 1, 1)), Color.Lerp(Horizon.linear, Top.linear, Mathf.Sqrt(0.7071f)).gamma, "the sky opposite", 0.06f);

            ModelBridge.SetSun(true, 0, -1, -1, 1, 0, 0, 1, false);
            Near(Centre(new Vector3(0, 1, 1)), Color.red, "the disc, moved with the sun");
            Assert.That(Centre(toSun).r, Is.LessThan(0.5f), "and gone from where it was");
        }

        [Test]
        public void NoSun_DrawsNoDisc() {
            ModelBridge.SetSun(true, 0, -1, 1, 1, 0, 0, 1, false);
            Sky(sunSize: 0, sunGlow: 0);
            Assert.That(Centre(new Vector3(0, 1, -1)).r, Is.LessThan(0.5f), "size 0");
            Sky(sunSize: 4);
            ModelBridge.SetSun(false, 0, -1, 1, 1, 0, 0, 1, false);
            Assert.That(Centre(new Vector3(0, 1, -1)).r, Is.LessThan(0.5f), "the sun switched off");
        }

        [Test]
        public void WithFog_TheHorizonIsTheFogsColour() {
            Sky();
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.2f, 0.8f, 0.3f);
            Near(Centre(new Vector3(0, 0, 10)), RenderSettings.fogColor, "level, in fog");
            Near(Centre(new Vector3(0, 100, 1)), Top, "overhead, above it");
            RenderSettings.fog = false;
            Near(Centre(new Vector3(0, 0, 10)), Horizon, "level, the fog gone");
        }

        [Test]
        public void Stars_LightPointsOverhead_OnlyWhenAskedFor() {
            int Lit(Color[] pixels) {
                int n = 0;
                var top = Top.linear;
                foreach (var p in pixels) if (p.r + p.g + p.b > top.r + top.g + top.b + 0.3f) n++;
                return n;
            }
            Sky(stars: 0);
            Assert.That(Lit(Render(new Vector3(0, 100, 1))), Is.EqualTo(0));
            Sky(stars: 1);
            Assert.That(Lit(Render(new Vector3(0, 100, 1))), Is.GreaterThan(0));
        }

        [Test]
        public void SkyFalse_GivesTheBackgroundColourBack_AndKeepsNothing() {
            ModelBridge.SetBackground(0, 0, 1);
            Sky();
            var material = ModelBridge.SkyMaterial;
            Assert.That(ModelBridge.SkyHooked, Is.True, "the sun and fog are read each frame while it is up");
            ModelBridge.SetSky(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var camera = ModelBridge.SceneCamera;
            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(camera.TryGetComponent<Skybox>(out _), Is.False);
            Assert.That(material == null, Is.True, "the material is freed");
            Assert.That(ModelBridge.SkyHooked, Is.False, "and nothing is read a frame");
            Near(Centre(new Vector3(0, 100, 1)), Color.blue, "the background");
        }

        [Test]
        public void TheSceneGoing_TakesTheSkyAlong_AndTheOpenScenesSkyboxIsNeverTouched() {
            var skybox = RenderSettings.skybox;
            Sky();
            Assert.That(RenderSettings.skybox, Is.SameAs(skybox));
            var material = ModelBridge.SkyMaterial;
            ModelBridge.DisposeAll();
            Assert.That(material == null, Is.True);
            Assert.That(ModelBridge.SkyHooked, Is.False);
        }
    }
}
