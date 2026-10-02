using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OneJS.Models {
    /// <summary>
    /// glTF models for JS: load a .glb once, spawn it many times, move, animate, pick and
    /// dissolve the copies. Everything is addressed by an integer handle, so JS never holds a
    /// GameObject and a game never names C#: oj's models module is the only caller.
    ///
    /// Compiled only when com.unity.cloud.gltfast is installed (see the asmdef). JS looks the
    /// type up by name and says what to install when it is absent.
    /// </summary>
    public static class ModelBridge {
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        class Model {
            public GltfImport import;
            public GameObject template;
            public Bounds bounds;
            public string[] clips;
        }

        class Actor {
            public GameObject go;
            public Animation anim;
            public Renderer[] renderers;
            public MaterialPropertyBlock block;
        }

        static readonly Dictionary<int, Model> _models = new();
        static readonly Dictionary<int, Actor> _actors = new();
        static int _next;
        static Transform _root;

        static Transform Root {
            get {
                if (_root == null) {
                    var go = new GameObject("OneJS Models");
                    Mark(go);
                    _root = go.transform;
                }
                return _root;
            }
        }

        // MARK: Models

        /// <summary>
        /// Loads a .glb or .gltf from a URL or a file path and keeps it as an inactive template.
        /// Resolves to the model's handle, and rejects with glTFast's reason when it fails.
        /// </summary>
        public static async Task<int> Load(string url) {
            // glTFast's default agent spreads work over frames from a DontDestroyOnLoad object,
            // which edit mode refuses; the edit-mode preview loads in one go instead.
            var defer = Application.isPlaying ? null : new UninterruptedDeferAgent();
            var import = new GltfImport(deferAgent: defer, materialGenerator: new ModelMaterialGenerator());
            var settings = new ImportSettings { AnimationMethod = AnimationMethod.Legacy };
            if (!await import.Load(url, settings)) {
                import.Dispose();
                throw new Exception($"could not load the model at {url}");
            }
            var template = new GameObject("model");
            template.SetActive(false);
            template.transform.SetParent(Root, false);
            if (!await import.InstantiateMainSceneAsync(template.transform)) {
                Kill(template);
                import.Dispose();
                throw new Exception($"could not instantiate the model at {url}");
            }

            Mark(template);

            var clips = new List<string>();
            var anim = template.GetComponentInChildren<Animation>(true);
            if (anim != null) {
                foreach (AnimationState state in anim) clips.Add(state.name);
                anim.playAutomatically = false;
            }

            var id = ++_next;
            _models[id] = new Model {
                import = import, template = template, bounds = MeasureBounds(template), clips = clips.ToArray(),
            };
            return id;
        }

        /// <summary>The model's animation clip names, one per line.</summary>
        public static string Clips(int model) =>
            _models.TryGetValue(model, out var m) ? string.Join("\n", m.clips) : "";

        /// <summary>The model's height in its own units, for placing a label above it.</summary>
        public static float Height(int model) => _models.TryGetValue(model, out var m) ? m.bounds.size.y : 0;

        // MARK: Actors

        /// <summary>A copy of a model in the world. Resolves to the actor's handle.</summary>
        public static int Spawn(int model, float x, float y, float z, float yaw, float scale) {
            if (!_models.TryGetValue(model, out var m)) throw new ArgumentException($"no model {model}");
            var go = Object.Instantiate(m.template, Root);
            go.name = m.template.name;
            go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * scale;

            // One box from the template's bounds is what a click needs: a skinned mesh has no
            // collider of its own, and a mesh collider per copy costs far more than it buys.
            var box = go.AddComponent<BoxCollider>();
            box.center = m.bounds.center;
            box.size = m.bounds.size;
            Mark(go);
            go.SetActive(true);

            var id = ++_next;
            _actors[id] = new Actor {
                go = go,
                anim = go.GetComponentInChildren<Animation>(true),
                renderers = go.GetComponentsInChildren<Renderer>(true),
                block = new MaterialPropertyBlock(),
            };
            return id;
        }

        public static void Place(int actor, float x, float y, float z, float yaw) {
            if (!_actors.TryGetValue(actor, out var a)) return;
            a.go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, yaw, 0));
        }

        /// <summary>Plays a clip, crossfading from whatever was playing. Returns false for an unknown name.</summary>
        public static bool Play(int actor, string clip, bool loop, float fade) {
            if (!_actors.TryGetValue(actor, out var a) || a.anim == null) return false;
            var state = a.anim[clip];
            if (state == null) return false;
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            // A crossfade only progresses in Animation's own update, which edit mode never runs.
            if (fade > 0 && Application.isPlaying) a.anim.CrossFade(clip, fade);
            else a.anim.Play(clip);
            return true;
        }

        /// <summary>0 is whole, 1 is gone. Drawn by ModelLit's noise threshold.</summary>
        public static void SetDissolve(int actor, float amount) {
            if (!_actors.TryGetValue(actor, out var a)) return;
            foreach (var r in a.renderers) {
                r.GetPropertyBlock(a.block);
                a.block.SetFloat(DissolveId, amount);
                r.SetPropertyBlock(a.block);
            }
        }

        public static void Destroy(int actor) {
            if (_actors.Remove(actor, out var a) && a.go != null) Kill(a.go);
        }

        /// <summary>
        /// Advances every actor's clip by `dt` seconds. Edit mode only, where Animation does not
        /// update itself: the models module calls it each frame of the edit-mode preview.
        /// </summary>
        public static void Step(float dt) {
            if (Application.isPlaying) return;
            foreach (var a in _actors.Values) {
                if (a.anim == null) continue;
                foreach (AnimationState state in a.anim) {
                    if (state.enabled) state.time += dt * state.speed;
                }
                a.anim.Sample();
            }
        }

        // MARK: Screen

        /// <summary>
        /// Where a point `lift` units above the actor lands in the panel that `element` belongs
        /// to, in that panel's coordinates: what a label positioned absolutely in the root wants.
        /// </summary>
        public static Vector2 PanelPoint(VisualElement element, int actor, float lift) {
            var cam = Camera.main;
            if (cam == null || element?.panel == null || !_actors.TryGetValue(actor, out var a)) {
                return new Vector2(float.NaN, float.NaN);
            }
            var world = a.go.transform.position + Vector3.up * lift;
            if (cam.WorldToViewportPoint(world).z < 0) return new Vector2(float.NaN, float.NaN);
            return RuntimePanelUtils.CameraTransformWorldToPanel(element.panel, world, cam);
        }

        /// <summary>The actor under a panel point, or 0. Assumes the panel covers the screen.</summary>
        public static int Pick(VisualElement element, float x, float y) {
            var cam = Camera.main;
            var size = element?.panel?.visualTree.layout.size ?? Vector2.zero;
            if (cam == null || size.x <= 0 || size.y <= 0) return 0;
            var screen = new Vector3(x / size.x * Screen.width, Screen.height - y / size.y * Screen.height, 0);
            // Actors move by transform between physics steps, and a host may step physics by script.
            Physics.SyncTransforms();
            if (!Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 10000f)) return 0;
            foreach (var pair in _actors) {
                if (pair.Value.go == hit.collider.gameObject) return pair.Key;
            }
            return 0;
        }

        // MARK: Stage

        public static void SetCamera(float x, float y, float z, float lookX, float lookY, float lookZ, float fov) {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = new Vector3(x, y, z);
            cam.transform.LookAt(new Vector3(lookX, lookY, lookZ));
            if (fov > 0) cam.fieldOfView = fov;
        }

        public static void SetBackground(float r, float g, float b) {
            var cam = Camera.main;
            if (cam == null) return;
            // A host that draws only UI (the Play container) culls everything; a scene needs the world drawn.
            cam.cullingMask = ~0;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(r, g, b, 1);
        }

        /// <summary>One directional light pointing along (x, y, z), plus a flat ambient term.</summary>
        public static void SetLight(float x, float y, float z, float intensity, float ambient) {
            var light = RenderSettings.sun;
            if (light == null) light = Object.FindFirstObjectByType<Light>();
            if (light == null) {
                var go = new GameObject("OneJS Light");
                Mark(go);
                light = go.AddComponent<Light>();
                light.transform.SetParent(Root, false);
                light.type = LightType.Directional;
            }
            light.transform.rotation = Quaternion.LookRotation(new Vector3(x, y, z));
            light.intensity = intensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(ambient, ambient, ambient, 1);
        }

        /// <summary>Destroys every actor and model. The models module calls it when its scene unmounts.</summary>
        public static void DisposeAll() {
            foreach (var a in _actors.Values) if (a.go != null) Kill(a.go);
            _actors.Clear();
            foreach (var m in _models.Values) {
                if (m.template != null) Kill(m.template);
                m.import.Dispose();
            }
            _models.Clear();
            if (_root != null) Kill(_root.gameObject);
            _root = null;
        }

        public static int ActorCount => _actors.Count;

        // The edit-mode preview builds its world in the open scene, which must not save it.
        static void Mark(GameObject go) {
            if (Application.isPlaying) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        }

        static void Kill(Object o) {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        static Bounds MeasureBounds(GameObject template) {
            var renderers = template.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = renderers[0] is SkinnedMeshRenderer s0 ? WorldBounds(s0) : renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) {
                b.Encapsulate(renderers[i] is SkinnedMeshRenderer s ? WorldBounds(s) : renderers[i].bounds);
            }
            // The template sits at the origin unrotated, so its world bounds are its local ones.
            return b;
        }

        // An inactive skinned renderer reports empty bounds, so use the mesh's own, in the renderer's space.
        static Bounds WorldBounds(SkinnedMeshRenderer s) {
            var mesh = s.sharedMesh;
            if (mesh == null) return new Bounds(s.transform.position, Vector3.zero);
            var local = mesh.bounds;
            return new Bounds(s.transform.TransformPoint(local.center), Vector3.Scale(local.size, s.transform.lossyScale));
        }
    }
}
