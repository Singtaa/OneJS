using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("OneJS.Tests.Models")]
[assembly: InternalsVisibleTo("OneJS.Tests.Models.Editor")]

namespace OneJS.Models {
    /// <summary>
    /// A 3D scene for JS: load a .glb once, spawn it many times, move, animate, pick and dissolve
    /// the copies, under a sun, ambient light, fog and point lights. Everything is addressed by an
    /// integer handle, so JS never holds a GameObject and a game never names C#: oj's models module
    /// is the only caller.
    ///
    /// The bridge always draws through a camera of its own, placed above every other. What else it
    /// does depends on the mode:
    ///
    /// - In play mode and in a player the cart's scene is the scene. The bridge switches off the
    ///   cameras drawing to the screen and the suns already there, so nothing draws or lights
    ///   twice, sets the ambient light and fog the cart asked for, and gives all of it back when
    ///   the scene is disposed.
    /// - In the edit-mode preview a cart runs inside somebody's open scene, which a save would
    ///   keep. There the bridge only adds objects of its own, marked not to save, and changes
    ///   nothing that was already there: the scene's own sun and ambient light the models.
    ///
    /// Compiled only when com.unity.cloud.gltfast is installed (see the asmdef). JS looks the type
    /// up by name and says what to install when it is absent.
    /// </summary>
    public static class ModelBridge {
        const string RootName = "OneJS Models";
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int ReceiveShadowsId = Shader.PropertyToID("_ReceiveShadows");

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
            public float dissolve;
            public bool receive = true;
        }

        /// <summary>The lighting a play-mode scene changed, to put back.</summary>
        struct Lighting {
            public AmbientMode mode;
            public SphericalHarmonicsL2 probe;
            public Color ambient, sky, equator, ground;
            public bool fog;
            public FogMode fogMode;
            public Color fogColor;
            public float fogStart, fogEnd;
            public Light sun;

            public static Lighting Capture() => new Lighting {
                mode = RenderSettings.ambientMode, probe = RenderSettings.ambientProbe,
                ambient = RenderSettings.ambientLight, sky = RenderSettings.ambientSkyColor,
                equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor,
                fog = RenderSettings.fog, fogMode = RenderSettings.fogMode, fogColor = RenderSettings.fogColor,
                fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance, sun = RenderSettings.sun,
            };

            public void Restore() {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.ambientProbe = probe;
                RenderSettings.fog = fog;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
                RenderSettings.sun = sun;
            }
        }

        static readonly Dictionary<int, Model> _models = new();
        static readonly Dictionary<int, Actor> _actors = new();
        static readonly Dictionary<int, Light> _lights = new();
        static readonly List<Behaviour> _switchedOff = new();
        static Lighting? _saved;
        static int _next;
        // Bumped by DisposeAll, so a load that outlives its scene knows to throw its work away.
        static int _generation;
        static Transform _root;
        static Camera _camera;
        static Light _sun;

        static bool Playing => Application.isPlaying;

        static Transform Root {
            get {
                if (_root == null) {
                    // A domain reload forgets the root but not the objects, which are marked not to
                    // save in edit mode and so outlive it; a second camera would draw over this one.
                    foreach (var stale in Resources.FindObjectsOfTypeAll<Transform>()) {
                        if (stale.parent == null && stale.name == RootName && stale.gameObject.scene.IsValid()) Kill(stale.gameObject);
                    }
                    var go = new GameObject(RootName);
                    Mark(go);
                    _root = go.transform;
                }
                return _root;
            }
        }

        /// <summary>The camera the scene draws through. Null before BeginScene.</summary>
        public static Camera SceneCamera => _camera;

        // MARK: Models

        /// <summary>
        /// Loads a .glb or .gltf from a URL or a file path and keeps it as an inactive template.
        /// Resolves to the model's handle, and rejects with glTFast's reason when it fails.
        /// </summary>
        public static async Task<int> Load(string url) {
            // glTFast's default agent spreads work over frames from a DontDestroyOnLoad object,
            // which edit mode refuses; the edit-mode preview loads in one go instead.
            var defer = Playing ? null : new UninterruptedDeferAgent();
            var import = new GltfImport(deferAgent: defer, materialGenerator: new ModelMaterialGenerator());
            var settings = new ImportSettings { AnimationMethod = AnimationMethod.Legacy };
            var generation = _generation;
            var loaded = await import.Load(url, settings);
            Abandoned(generation, import, null);
            if (!loaded) {
                import.Dispose();
                throw new Exception($"could not load the model at {url}");
            }
            var template = new GameObject("model");
            template.SetActive(false);
            template.transform.SetParent(Root, false);
            var instantiated = await import.InstantiateMainSceneAsync(template.transform);
            Abandoned(generation, import, template);
            if (!instantiated) {
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

        // Pressing Play, or a hot reload, disposes the scene while a load may still be running; what
        // the load made after that would outlive the scene, so it is destroyed and the load cancelled.
        static void Abandoned(int generation, GltfImport import, GameObject template) {
            if (generation == _generation) return;
            if (template != null) Kill(template);
            import.Dispose();
            throw new OperationCanceledException("the scene was disposed while the model loaded");
        }

        /// <summary>The model's animation clip names, one per line.</summary>
        public static string Clips(int model) =>
            _models.TryGetValue(model, out var m) ? string.Join("\n", m.clips) : "";

        /// <summary>The model's height in its own units, for placing a label above it.</summary>
        public static float Height(int model) => _models.TryGetValue(model, out var m) ? m.bounds.size.y : 0;

        // MARK: Actors

        /// <summary>A copy of a model in the world. Returns the actor's handle.</summary>
        public static int Spawn(int model, float x, float y, float z, float yaw, float scale, bool castShadows, bool receiveShadows) {
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
            var actor = new Actor {
                go = go,
                anim = go.GetComponentInChildren<Animation>(true),
                renderers = go.GetComponentsInChildren<Renderer>(true),
                block = new MaterialPropertyBlock(),
            };
            _actors[id] = actor;
            Shadows(actor, castShadows, receiveShadows);
            return id;
        }

        public static void Place(int actor, float x, float y, float z, float yaw) {
            if (!_actors.TryGetValue(actor, out var a)) return;
            a.go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, yaw, 0));
        }

        /// <summary>Whether an actor casts shadows and has shadows cast on it.</summary>
        public static void SetShadows(int actor, bool castShadows, bool receiveShadows) {
            if (_actors.TryGetValue(actor, out var a)) Shadows(a, castShadows, receiveShadows);
        }

        /// <summary>Plays a clip, crossfading from whatever was playing. Returns false for an unknown name.</summary>
        public static bool Play(int actor, string clip, bool loop, float fade) {
            if (!_actors.TryGetValue(actor, out var a) || a.anim == null) return false;
            var state = a.anim[clip];
            if (state == null) return false;
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            // A crossfade only progresses in Animation's own update, which edit mode never runs.
            if (fade > 0 && Playing) a.anim.CrossFade(clip, fade);
            else a.anim.Play(clip);
            return true;
        }

        /// <summary>0 is whole, 1 is gone. Drawn by ModelLit's noise threshold.</summary>
        public static void SetDissolve(int actor, float amount) {
            if (!_actors.TryGetValue(actor, out var a)) return;
            a.dissolve = amount;
            Apply(a);
        }

        public static void Destroy(int actor) {
            if (_actors.Remove(actor, out var a) && a.go != null) Kill(a.go);
        }

        /// <summary>
        /// Advances every actor's clip by `dt` seconds. Edit mode only, where Animation does not
        /// update itself: the models module calls it each frame of the edit-mode preview.
        /// </summary>
        public static void Step(float dt) {
            if (Playing) return;
            foreach (var a in _actors.Values) {
                if (a.anim == null) continue;
                foreach (AnimationState state in a.anim) {
                    if (state.enabled) state.time += dt * state.speed;
                }
                a.anim.Sample();
            }
        }

        internal static GameObject ActorObject(int actor) => _actors.TryGetValue(actor, out var a) ? a.go : null;

        // MARK: Screen

        /// <summary>
        /// Where a point `lift` units above the actor lands in `element`, in its own coordinates:
        /// what a label positioned absolutely inside it wants. NaN when behind the camera. Assumes
        /// the panel covers the camera's view, which a cart's stage does.
        /// </summary>
        public static Vector2 PanelPoint(VisualElement element, int actor, float lift) {
            var none = new Vector2(float.NaN, float.NaN);
            var size = PanelSize(element);
            if (_camera == null || size.x <= 0 || !_actors.TryGetValue(actor, out var a)) return none;
            var screen = _camera.WorldToScreenPoint(a.go.transform.position + Vector3.up * lift);
            if (screen.z < 0) return none;
            var rect = _camera.pixelRect;
            var panel = new Vector2((screen.x - rect.x) / rect.width * size.x, (rect.height - (screen.y - rect.y)) / rect.height * size.y);
            return element.WorldToLocal(panel);
        }

        /// <summary>The actor under a panel point (a pointer event's x and y), or 0.</summary>
        public static int Pick(VisualElement element, float x, float y) {
            var size = PanelSize(element);
            if (_camera == null || size.x <= 0) return 0;
            var rect = _camera.pixelRect;
            var screen = new Vector3(rect.x + x / size.x * rect.width, rect.y + rect.height - y / size.y * rect.height, 0);
            // Actors move by transform between physics steps, and a host may step physics by script.
            Physics.SyncTransforms();
            if (!Physics.Raycast(_camera.ScreenPointToRay(screen), out var hit, _camera.farClipPlane)) return 0;
            foreach (var pair in _actors) {
                if (pair.Value.go == hit.collider.gameObject) return pair.Key;
            }
            return 0;
        }

        // MARK: Stage

        /// <summary>
        /// Makes the camera the scene draws through, above every other. In play mode the cameras
        /// already drawing to the screen are switched off until the scene is disposed.
        /// </summary>
        public static void BeginScene() {
            if (_camera != null) return;
            var others = Camera.allCameras;
            float depth = 0;
            foreach (var c in others) depth = Mathf.Max(depth, c.depth + 1);

            var go = new GameObject("OneJS Camera");
            go.transform.SetParent(Root, false);
            Mark(go);
            _camera = go.AddComponent<Camera>();
            _camera.depth = depth;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 1000f;

            if (!Playing) return;
            foreach (var c in others) {
                if (c != _camera && c.enabled && c.targetTexture == null) SwitchOff(c);
            }
        }

        public static void SetCamera(float x, float y, float z, float lookX, float lookY, float lookZ, float fov) {
            BeginScene();
            _camera.transform.position = new Vector3(x, y, z);
            _camera.transform.LookAt(new Vector3(lookX, lookY, lookZ));
            if (fov > 0) _camera.fieldOfView = fov;
        }

        /// <summary>The colour behind the world, as an sRGB colour.</summary>
        public static void SetBackground(float r, float g, float b) {
            BeginScene();
            _camera.backgroundColor = new Color(r, g, b, 1);
        }

        /// <summary>
        /// The sun: one directional light shining along (dx, dy, dz), an sRGB colour, and soft
        /// shadows or none. In play mode it replaces the scene's suns; in the edit-mode preview a
        /// scene that has a sun keeps it, and only a scene without one gets this.
        /// </summary>
        public static void SetSun(bool enabled, float dx, float dy, float dz, float r, float g, float b, float intensity, bool shadows) {
            BeginScene();
            if (Playing) {
                Save();
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) {
                    if (light != _sun && light.type == LightType.Directional && light.enabled) SwitchOff(light);
                }
            } else if (SceneHasASun()) {
                return;
            }
            if (!enabled) {
                if (_sun != null) _sun.enabled = false;
                if (Playing) RenderSettings.sun = null;
                return;
            }
            if (_sun == null) {
                var go = new GameObject("OneJS Sun");
                go.transform.SetParent(Root, false);
                Mark(go);
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
            }
            _sun.enabled = true;
            var direction = new Vector3(dx, dy, dz);
            _sun.transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0 ? direction : Vector3.down);
            _sun.color = new Color(r, g, b, 1);
            _sun.intensity = intensity;
            _sun.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            _sun.shadowStrength = 1;
            if (Playing) RenderSettings.sun = _sun;
        }

        /// <summary>
        /// Light from every direction: an sRGB sky colour from above fading to a ground colour from
        /// below, times `intensity`. Play mode only; the edit-mode preview keeps the scene's.
        /// </summary>
        public static void SetAmbient(float skyR, float skyG, float skyB, float groundR, float groundG, float groundB, float intensity) {
            if (!Playing) return;
            Save();
            var sky = new Color(skyR, skyG, skyB, 1);
            var ground = new Color(groundR, groundG, groundB, 1);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * intensity;
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, ground, 0.5f) * intensity;
            RenderSettings.ambientGroundColor = ground * intensity;
            RenderSettings.ambientProbe = HemisphereProbe(sky, ground, intensity);
        }

        /// <summary>Linear fog from `near` to `far` in an sRGB colour, or none. Play mode only.</summary>
        public static void SetFog(bool enabled, float r, float g, float b, float near, float far) {
            if (!Playing) return;
            Save();
            RenderSettings.fog = enabled;
            if (!enabled) return;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(r, g, b, 1);
            RenderSettings.fogStartDistance = near;
            RenderSettings.fogEndDistance = far;
        }

        /// <summary>A point light at (x, y, z) in an sRGB colour, reaching `range` units. Returns its handle.</summary>
        public static int AddPointLight(float x, float y, float z, float r, float g, float b, float intensity, float range) {
            BeginScene();
            var go = new GameObject("OneJS Light");
            go.transform.SetParent(Root, false);
            go.transform.position = new Vector3(x, y, z);
            Mark(go);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            var id = ++_next;
            _lights[id] = light;
            SetLight(id, r, g, b, intensity, range);
            return id;
        }

        public static void PlaceLight(int light, float x, float y, float z) {
            if (_lights.TryGetValue(light, out var l)) l.transform.position = new Vector3(x, y, z);
        }

        public static void SetLight(int light, float r, float g, float b, float intensity, float range) {
            if (!_lights.TryGetValue(light, out var l)) return;
            l.color = new Color(r, g, b, 1);
            l.intensity = intensity;
            l.range = range;
        }

        public static void DestroyLight(int light) {
            if (_lights.Remove(light, out var l) && l != null) Kill(l.gameObject);
        }

        /// <summary>
        /// The ambient probe for a sky colour above and a ground colour below (sRGB), blending
        /// through their average at the horizon. Two bands of SH hold that exactly. Unity keeps the
        /// basis constants inside its coefficients (AddAmbientLight(white) sets the first to 1), so
        /// the light is the average plus half the difference times the normal's height.
        /// </summary>
        public static SphericalHarmonicsL2 HemisphereProbe(Color sky, Color ground, float intensity) {
            var s = sky.linear * intensity;
            var g = ground.linear * intensity;
            var sh = new SphericalHarmonicsL2();
            for (var c = 0; c < 3; c++) {
                sh[c, 0] = (s[c] + g[c]) * 0.5f;
                sh[c, 1] = (s[c] - g[c]) * 0.5f;
            }
            return sh;
        }

        /// <summary>
        /// Destroys every actor, model and light, and gives back what play mode switched off or
        /// changed. The models module calls it when its scene unmounts.
        /// </summary>
        public static void DisposeAll() {
            _generation++;
            foreach (var a in _actors.Values) if (a.go != null) Kill(a.go);
            _actors.Clear();
            foreach (var m in _models.Values) {
                if (m.template != null) Kill(m.template);
                m.import.Dispose();
            }
            _models.Clear();
            _lights.Clear();
            if (_root != null) Kill(_root.gameObject);
            _root = null;
            _camera = null;
            _sun = null;
            foreach (var b in _switchedOff) if (b != null) b.enabled = true;
            _switchedOff.Clear();
            _saved?.Restore();
            _saved = null;
        }

        public static int ActorCount => _actors.Count;

        static void Shadows(Actor a, bool cast, bool receive) {
            a.receive = receive;
            foreach (var r in a.renderers) {
                r.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
                // The built-in pipeline reads the renderer's flag; ModelLit in URP reads the block.
                r.receiveShadows = receive;
            }
            Apply(a);
        }

        static void Apply(Actor a) {
            foreach (var r in a.renderers) {
                r.GetPropertyBlock(a.block);
                a.block.SetFloat(DissolveId, a.dissolve);
                a.block.SetFloat(ReceiveShadowsId, a.receive ? 1 : 0);
                r.SetPropertyBlock(a.block);
            }
        }

        static Vector2 PanelSize(VisualElement element) => element?.panel?.visualTree.layout.size ?? Vector2.zero;

        static bool SceneHasASun() {
            // FindObjectsByType skips objects marked DontSave, so this sees the scene's lights, not ours.
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) {
                if (light.type == LightType.Directional && light.isActiveAndEnabled) return true;
            }
            return false;
        }

        static void SwitchOff(Behaviour b) {
            b.enabled = false;
            _switchedOff.Add(b);
        }

        static void Save() => _saved ??= Lighting.Capture();

        // The edit-mode preview builds its world in the open scene, which must not save it.
        static void Mark(GameObject go) {
            if (Playing) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        }

        static void Kill(Object o) {
            if (Playing) Object.Destroy(o);
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
