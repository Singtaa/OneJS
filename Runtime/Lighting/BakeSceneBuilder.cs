using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Lighting {
    /// <summary>
    /// The scene's static triangles in world space, at their atlas uv, with each triangle's albedo:
    /// what a BakeScene is built from. A material's albedo waits on its textures' means, which on
    /// WebGPU are read back a frame or more later (ReadMeans, MeansReady).
    /// </summary>
    internal sealed class BakeSceneBuilder {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int MetallicMapId = Shader.PropertyToID("_MetallicRoughnessMap");

        readonly List<Vector3> _positions = new();
        readonly List<Vector3> _normals = new();
        readonly List<Vector2> _uvs = new();
        readonly List<int> _indices = new();
        // Per triangle: which of _materials it is drawn with, and its vertex colours' mean (white without).
        readonly List<int> _surface = new();
        readonly List<Color> _tint = new();
        readonly List<Material> _materials = new();
        readonly Dictionary<Material, int> _materialIndex = new();
        // Each texture's mean, read back from a 16 by 16 copy drawn through its mips.
        readonly Dictionary<Texture, (RenderTexture rt, TextureRead read)> _means = new();
        readonly List<Vector2> _layout = new();

        public bool Empty => _indices.Count == 0;

        /// <summary>A mesh drawn at `world` with `materials`, its TEXCOORD1 layout placed at `st` on the sheet.</summary>
        public void Add(Mesh mesh, Matrix4x4 world, Material[] materials, Vector4 st) {
            var normalMatrix = world.inverse.transpose;
            var at = _positions.Count;
            var normals = mesh.normals;
            var colors = mesh.colors;
            var vertices = mesh.vertices;
            mesh.GetUVs(1, _layout);
            for (int v = 0; v < vertices.Length; v++) {
                _positions.Add(world.MultiplyPoint3x4(vertices[v]));
                _normals.Add(normals.Length == vertices.Length ? normalMatrix.MultiplyVector(normals[v]).normalized : Vector3.up);
                var uv = v < _layout.Count ? _layout[v] : Vector2.zero;
                _uvs.Add(new Vector2(uv.x * st.x + st.z, uv.y * st.y + st.w));
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++) {
                var material = sub < materials.Length ? materials[sub] : null;
                var surface = SurfaceOf(material);
                var vertexColors = material != null && material.IsKeywordEnabled("_VERTEX_COLOR") && colors.Length == vertices.Length;
                var tris = mesh.GetTriangles(sub);
                for (int k = 0; k < tris.Length; k += 3) {
                    _indices.Add(at + tris[k]);
                    _indices.Add(at + tris[k + 1]);
                    _indices.Add(at + tris[k + 2]);
                    _surface.Add(surface);
                    _tint.Add(vertexColors ? (colors[tris[k]] + colors[tris[k + 1]] + colors[tris[k + 2]]) / 3f : Color.white);
                }
            }
        }

        int SurfaceOf(Material material) {
            // A null material is a surface too, the gray Albedo gives it, kept under index -1.
            if (material == null) return -1;
            if (_materialIndex.TryGetValue(material, out var i)) return i;
            _materialIndex[material] = _materials.Count;
            _materials.Add(material);
            return _materials.Count - 1;
        }

        /// <summary>Starts reading back the mean of every texture the albedo needs: base maps, and metallic maps where a material is metal.</summary>
        public void ReadMeans() {
            foreach (var material in _materials) {
                if (material.HasTexture(BaseMapId)) StartMean(material.GetTexture(BaseMapId));
                if (material.HasFloat(MetallicId) && material.HasTexture(MetallicMapId)) StartMean(material.GetTexture(MetallicMapId));
            }
        }

        void StartMean(Texture texture) {
            if (texture == null || _means.ContainsKey(texture)) return;
            var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, rt);
            _means[texture] = (rt, TextureRead.Start(rt));
        }

        /// <summary>Whether every mean ReadMeans started has landed.</summary>
        public bool MeansReady {
            get {
                foreach (var (_, read) in _means.Values) if (!read.Done) return false;
                return true;
            }
        }

        /// <summary>Each triangle's albedo, once MeansReady: its material's, times its vertex colours' mean.</summary>
        public void FillAlbedo(Color[] albedo) {
            var surfaces = new Color[_materials.Count];
            for (int i = 0; i < surfaces.Length; i++) surfaces[i] = Albedo(_materials[i]);
            for (int t = 0; t < albedo.Length; t++) {
                var c = (_surface[t] < 0 ? Albedo(null) : surfaces[_surface[t]]) * _tint[t];
                c.a = 1;
                albedo[t] = c;
            }
        }

        /// <summary>Gives back the means' render textures.</summary>
        public void Dispose() {
            foreach (var (rt, _) in _means.Values) RenderTexture.ReleaseTemporary(rt);
            _means.Clear();
        }

        /// <summary>
        /// A material's diffuse colour, linear: its base colour times its base map's mean, less the
        /// part that is metal (ModelLit's metallic is `_Metallic` times its map's blue), which
        /// reflects rather than scatters and so bounces nothing a lightmap holds. A property the
        /// material's shader lacks is left out rather than read.
        /// </summary>
        Color Albedo(Material material) {
            if (material == null) return Color.gray;
            var c = material.HasColor(BaseColorId) ? material.GetColor(BaseColorId) : material.HasColor("_Color") ? material.color : Color.white;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) c = c.linear;
            var map = material.HasTexture(BaseMapId) ? material.GetTexture(BaseMapId) : null;
            if (map != null) c *= Mean(map);
            if (material.HasFloat(MetallicId)) {
                var metal = material.HasTexture(MetallicMapId) ? material.GetTexture(MetallicMapId) : null;
                c *= 1 - material.GetFloat(MetallicId) * (metal != null ? Mean(metal).b : 1);
            }
            c.a = 1;
            return c;
        }

        // A texture's mean colour, linear, from its read (ReadMeans). White when the read failed, so the material's colour stands alone.
        Color Mean(Texture texture) {
            if (!_means.TryGetValue(texture, out var mean)) return Color.white;
            var colors = mean.read.Colors();
            if (colors == null) {
                Debug.LogWarning($"[OneJS] The bake could not read {texture.name}'s mean colour back ({mean.read.Error}), and bounces its material's colour alone.");
                return Color.white;
            }
            var sum = Color.clear;
            foreach (var p in colors) sum += p;
            return sum / colors.Length;
        }

        /// <summary>The scene, its albedo left for FillAlbedo.</summary>
        public BakeScene Build(int width, int height, int pages) => new BakeScene {
            Positions = _positions.ToArray(), Normals = _normals.ToArray(), Uvs = _uvs.ToArray(),
            Indices = _indices.ToArray(), Albedo = new Color[_surface.Count], AtlasWidth = width, AtlasHeight = height, Pages = pages,
        };
    }
}
