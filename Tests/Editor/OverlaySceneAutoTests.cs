using NUnit.Framework;
using OneJS.Editor;
using UnityEngine;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Scene: Auto, with nothing selected, previews the runner closest to the
    /// Scene camera. A runner with a Renderer (a world-space panel's UIRenderer,
    /// which takes in its elements wherever their transforms moved them) is
    /// measured to its bounds, so a runner whose content is in front of the
    /// camera wins over one whose origin merely is. Without one, the origin.
    /// </summary>
    public class OverlaySceneAutoTests {
        GameObject _camera;
        GameObject _a;
        GameObject _b;

        [SetUp]
        public void SetUp() {
            _camera = new GameObject("SceneAutoCamera", typeof(Camera));
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in new[] { _camera, _a, _b })
                if (go != null) Object.DestroyImmediate(go);
        }

        static GameObject Runner(string name, Vector3 position) {
            var go = new GameObject(name);
            go.transform.position = position;
            go.AddComponent<JSRunner>();
            return go;
        }

        [Test]
        public void ARunnerWithARenderer_IsMeasuredToItsBounds() {
            // Origin 5 units ahead, nothing drawn elsewhere
            _a = Runner("OriginNear", new Vector3(0, 0, 5));
            // Origin 60 units ahead, but what it draws reaches 2 units from the camera
            _b = Runner("ContentNear", new Vector3(0, 0, 60));
            _b.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            _b.AddComponent<MeshRenderer>();
            _b.transform.localScale = new Vector3(4, 4, 116);

            var camera = _camera.GetComponent<Camera>();
            Assert.AreEqual(4f, OneJSOverlayUpdateModeBridge.SqrDistanceToRunner(_b.GetComponent<JSRunner>(), Vector3.zero), 0.01f,
                "measured to the bounds' nearest point, 2 units away");
            Assert.AreSame(_b.GetComponent<JSRunner>(), OneJSOverlayUpdateModeBridge.GetClosestRunnerToCamera(camera),
                "the runner whose content is closest is previewed");
        }

        [Test]
        public void ARunnerWithoutARenderer_IsMeasuredToItsOrigin() {
            _a = Runner("Near", new Vector3(0, 0, 5));
            _b = Runner("Far", new Vector3(0, 0, 10));

            var camera = _camera.GetComponent<Camera>();
            Assert.AreEqual(25f, OneJSOverlayUpdateModeBridge.SqrDistanceToRunner(_a.GetComponent<JSRunner>(), Vector3.zero), 0.01f);
            Assert.AreSame(_a.GetComponent<JSRunner>(), OneJSOverlayUpdateModeBridge.GetClosestRunnerToCamera(camera));
        }
    }
}
