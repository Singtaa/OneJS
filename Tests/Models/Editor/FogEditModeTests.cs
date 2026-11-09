using System;
using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// The fog ModelLit, GroundLit and the Sky draw (ModelFog, OneJSFog.hlsl), read back pixel by
    /// pixel against what each law says a pixel at that distance and height takes. A black unlit
    /// ground under a white fog reads the fog's share directly. The ground is two triangles 2 km
    /// across, so a fog worked out at the vertices and spread across them shows. The expected share
    /// integrates the density along the pixel's own ray numerically, so the test checks the shader's
    /// closed form rather than repeating it.
    /// </summary>
    public class FogEditModeTests {
        const int Size = 129;
        // A half-float read-back of a value up to 1 is good to about 1/2048; this is under 1/255.
        const float Within = 0.0035f;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() {
            ModelBridge.DisposeAll();
            ModelFog.HeightBase = 0;
            ModelFog.HeightThickness = 0;
            RenderSettings.fog = false;
        }

        public enum Law { Linear, Exp, Exp2, ExpHeight, Exp2Height }

        static void Fog(Law law) {
            RenderSettings.fog = true;
            RenderSettings.fogColor = Color.white;
            RenderSettings.fogStartDistance = 20;
            RenderSettings.fogEndDistance = 300;
            RenderSettings.fogMode = law switch {
                Law.Linear => FogMode.Linear,
                Law.Exp or Law.ExpHeight => FogMode.Exponential,
                _ => FogMode.ExponentialSquared,
            };
            RenderSettings.fogDensity = law switch { Law.Exp => 0.008f, Law.Exp2 => 0.005f, Law.ExpHeight => 0.03f, _ => 0.012f };
            bool height = law is Law.ExpHeight or Law.Exp2Height;
            ModelFog.HeightBase = height ? -10 : 0;
            ModelFog.HeightThickness = height ? 25 : 0;
        }

        // The share of fog a surface `distance` metres along `ray` takes under the fog as set.
        static double Expected(Law law, Ray ray, double distance, Camera camera) {
            double near = RenderSettings.fogStartDistance, far = RenderSettings.fogEndDistance;
            if (law == Law.Linear) {
                // URP's, as 1.7 drew it: the eye depth past the near plane.
                double eye = distance * Vector3.Dot(ray.direction, camera.transform.forward);
                double z = Math.Max(eye - camera.nearClipPlane, 0);
                return 1 - Math.Clamp((far - z) / (far - near), 0, 1);
            }
            double density = RenderSettings.fogDensity, thickness = ModelFog.HeightThickness, floor = ModelFog.HeightBase;
            double Density(double t) {
                double y = ray.origin.y + ray.direction.y * t;
                return thickness > 0 ? density * Math.Exp(-(y - floor) / thickness) : density;
            }
            // Simpson's rule over the fogged stretch of the ray.
            double tau = 0;
            if (distance > near) {
                const int steps = 4000;
                double h = (distance - near) / steps;
                for (int k = 0; k <= steps; k++) tau += Density(near + k * h) * (k == 0 || k == steps ? 1 : k % 2 == 1 ? 4 : 2);
                tau *= h / 3;
            }
            if (law is Law.Exp2 or Law.Exp2Height) tau *= tau;
            return 1 - Math.Exp(-tau);
        }

        // A ground 2 km square in two triangles, black, drawn unlit, on ModelLit or GroundLit.
        static int Ground(bool splat, out Texture2D layer) {
            layer = null;
            int model = ModelBridge.CreateMesh(true, false, true, 0, 0, 0, 1);
            var corners = new Vector3[] { new(-1000, 0, -1000), new(-1000, 0, 1000), new(1000, 0, 1000), new(1000, 0, -1000) };
            var up = Enumerable.Repeat(Vector3.up, 4).ToArray();
            var uvs = new Vector2[] { new(0, 0), new(0, 1), new(1, 1), new(1, 0) };
            var triangles = new uint[] { 0, 1, 2, 0, 2, 3 };
            if (splat) {
                layer = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                layer.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
                layer.Apply();
                Assert.That(ModelBridge.SetMeshSplat(model, layer, null, null, null, 1, 1, 0), Is.Empty);
                Assert.That(ModelBridge.SetMesh(model, corners, triangles, up, null, uvs, new float[] { 1, 1, 1, 1 }), Is.Empty);
            } else {
                Assert.That(ModelBridge.SetMesh(model, corners, triangles, up, null, uvs), Is.Empty);
            }
            ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
            return model;
        }

        static Color[] Render(Camera camera) {
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
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void AGroundPixel_TakesItsLawsShare_AtItsDistanceAndHeight([Values] Law law, [Values(false, true)] bool splat) {
            Fog(law);
            int model = Ground(splat, out var layer);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            try {
                camera.transform.SetPositionAndRotation(new Vector3(0, 60, 0), Quaternion.Euler(12, 20, 0));
                camera.fieldOfView = 60;
                // Square as the target is, also once the target is gone and the rays are cast.
                camera.aspect = 1;
                camera.farClipPlane = 3000;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                var pixels = Render(camera);
                double worst = 0;
                string worstAt = "";
                int checkedPixels = 0;
                double lo = 1, hi = 0;
                for (int y = 2; y < Size; y += 6) {
                    for (int x = 2; x < Size; x += 6) {
                        var ray = camera.ViewportPointToRay(new Vector3((x + 0.5f) / Size, (y + 0.5f) / Size, 0));
                        if (ray.direction.y > -0.02f) continue;
                        double d = -ray.origin.y / ray.direction.y;
                        var hit = ray.GetPoint((float)d);
                        if (d > 1300 || Mathf.Abs(hit.x) > 990 || Mathf.Abs(hit.z) > 990) continue;
                        double want = Expected(law, ray, d, camera);
                        double got = pixels[y * Size + x].r;
                        lo = Math.Min(lo, want);
                        hi = Math.Max(hi, want);
                        checkedPixels++;
                        if (Math.Abs(got - want) > worst) {
                            worst = Math.Abs(got - want);
                            worstAt = $"pixel ({x}, {y}), {d:F0} m off: {got:F4} against {want:F4}";
                        }
                    }
                }
                TestContext.WriteLine($"{law}, {(splat ? "GroundLit" : "ModelLit")}: {checkedPixels} pixels from {lo:F3} to {hi:F3} fogged, worst {worst * 255:F2}/255 at {worstAt}");
                Assert.That(checkedPixels, Is.GreaterThan(100), "pixels on the ground");
                Assert.That(hi - lo, Is.GreaterThan(0.3), "the pixels span the fog's ramp, so a wrong law cannot pass by being flat");
                Assert.That(worst, Is.LessThan(Within), worstAt);
            } finally {
                ModelBridge.Destroy(actor);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                if (layer != null) UnityEngine.Object.DestroyImmediate(layer);
            }
        }

        [Test]
        public void ASkyPixel_WithHeightFog_TakesTheHazeToInfinity_AndBelowTheHorizonAllOfIt([Values(Law.ExpHeight, Law.Exp2Height)] Law law) {
            Fog(law);
            ModelBridge.SetSky(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var camera = ModelBridge.SceneCamera;
            foreach (float pitch in new[] { 4f, 15f, 40f, -5f }) {
                var look = new Vector3(0, Mathf.Sin(pitch * Mathf.Deg2Rad), Mathf.Cos(pitch * Mathf.Deg2Rad));
                ModelBridge.SetCamera(0, 30, 0, look.x, 30 + look.y, look.z, 60);
                double got = Render(camera)[Size / 2 * Size + Size / 2].r;
                var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                // Up, far enough along that the density left is e^-60 of the start's. Down, the fog thickens
                // without end, so all of it.
                double want = ray.direction.y > 0
                    ? Expected(law, ray, RenderSettings.fogStartDistance + 60 * ModelFog.HeightThickness / ray.direction.y, camera)
                    : 1;
                TestContext.WriteLine($"{law}, {pitch} degrees: {got:F4} against {want:F4}");
                Assert.That(got, Is.EqualTo(want).Within(Within), $"{pitch} degrees up");
            }
        }

        [Test]
        public void SetFogLaw_RefusesWhatItCannotDraw_SayingWhy() {
            Assert.That(ModelBridge.SetFogLaw("fog", 0.01f, 0, 0), Does.Contain("\"linear\", \"exp\" or \"exp2\""));
            Assert.That(ModelBridge.SetFogLaw("exp", -1, 0, 0), Does.Contain("density is -1"));
            Assert.That(ModelBridge.SetFogLaw("exp", float.NaN, 0, 0), Does.Contain("density is NaN"));
            Assert.That(ModelBridge.SetFogLaw("exp2", 0.01f, 0, -5), Does.Contain("thickness is -5"));
            Assert.That(ModelBridge.SetFogLaw("exp2", 0.01f, float.PositiveInfinity, 5), Does.Contain("base is"));
            Assert.That(ModelBridge.SetFogLaw("linear", 0.01f, 0, 45), Does.Contain("a linear ramp has no density"));
            Assert.That(ModelBridge.SetFogLaw("exp", 0.0017f, 0, 45), Is.Empty);
            Assert.That(ModelFog.HeightThickness, Is.EqualTo(0f), "edit mode changes nothing, as SetFog does not");
        }
    }
}
