using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OneJS.Models {
    /// <summary>
    /// Pictures on a mesh model: ground that tiles one texture under its vertex colours
    /// (onejs-play's `scene.mesh({ texture, tiling })`), or paints up to four by weights a vertex
    /// (`scene.mesh({ splat })`), which draws on GroundLit.
    ///
    /// A cart's picture is loaded for the UI, clamped, and on the web without mips, which a texture
    /// repeated across 64 m of ground needs or it shimmers to noise in the distance. So the bridge
    /// draws its own copy of each picture: repeating, trilinear, anisotropic, with a mip chain the
    /// GPU makes once. Every mesh model given the same picture shares one copy, which goes when
    /// the last of them does; the picture itself is the cart's and is left as it was.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int HeightBlendId = Shader.PropertyToID("_HeightBlend");
        static readonly int SplatTriplanarId = Shader.PropertyToID("_SplatTriplanar");
        static readonly int SplatFarId = Shader.PropertyToID("_SplatFar");
        static readonly int[] SplatLayerIds = {
            Shader.PropertyToID("_SplatLayer0"), Shader.PropertyToID("_SplatLayer1"),
            Shader.PropertyToID("_SplatLayer2"), Shader.PropertyToID("_SplatLayer3"),
        };

        /// <summary>The most layers a splat blends: one weight each, four a vertex.</summary>
        public const int SplatLayers = 4;

        sealed class GroundCopy {
            public RenderTexture texture;
            public int users;
        }

        // By the picture's reference, not Unity's equality, which calls every destroyed picture null
        // and so equal to each other.
        sealed class ByReference : IEqualityComparer<Texture> {
            public bool Equals(Texture a, Texture b) => ReferenceEquals(a, b);
            public int GetHashCode(Texture t) => RuntimeHelpers.GetHashCode(t);
        }

        static readonly Dictionary<Texture, GroundCopy> _groundCopies = new(new ByReference());

        /// <summary>
        /// Draws a mesh model's base map with `texture`, repeated `tileU` by `tileV` times across a
        /// uv of 0 to 1, multiplied by its colour and vertex colours as before. Null takes the
        /// texture off. The model must be made with uvs, which the texture is read at. Every actor
        /// and scatter of the model changes with it. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshTexture(int model, Texture texture, float tileU, float tileV) {
            if (!_models.TryGetValue(model, out var m) || m.built == null) return $"there is no mesh model {model}";
            var b = m.built;
            if (b.splat) return "this model is painted by a splat, which draws its layers: give the texture as a layer";
            if (texture != null && !b.givesUvs) return "this model was made without uvs, which a texture is read at: give uvs with its first vertices";
            var problem = Tiling(tileU, tileV) ?? (texture != null ? Pixels(texture, "the texture") : null);
            if (problem != null) return problem;

            // A texture given takes the place of a flipbook, as a flipbook takes a texture's.
            StopFlipbook(b);
            var copy = texture == null ? null : TakeGroundCopy(texture);
            ReleaseGroundCopy(b.texture);
            b.texture = copy;
            var material = b.material;
            if (ModelMaterialGenerator.ModelShader != null) {
                material.SetTexture(BaseMapId, copy);
                material.SetTextureScale(BaseMapId, new Vector2(tileU, tileV));
                material.SetTextureOffset(BaseMapId, Vector2.zero);
            } else {
                // HDRP, where glTFast's default material names its main texture for itself.
                material.mainTexture = copy;
                material.mainTextureScale = new Vector2(tileU, tileV);
            }
            return "";
        }

        /// <summary>
        /// Paints a mesh model from up to four tiling layers, mixed at each pixel by the weights
        /// SetMesh gives a vertex, one a layer, and multiplied by its colour and vertex colours. A
        /// layer's alpha is its height, which `heightBlend` above 0 blends by (ModelSplat.hlsl); at 0
        /// the weights alone mix them. The layers come in order: `layer1` to `layer3` may be null
        /// for fewer. The model draws on GroundLit from then on.
        ///
        /// Given before the model's first vertices, since it adds the weights to its vertices (and
        /// colours, white, to a model made without them); given again later, it changes the layers,
        /// the tiling and the blend, and keeps the number of layers. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshSplat(int model, Texture layer0, Texture layer1, Texture layer2, Texture layer3, float tileU, float tileV, float heightBlend) {
            if (!_models.TryGetValue(model, out var m) || m.built == null) return $"there is no mesh model {model}";
            var b = m.built;
            var given = new[] { layer0, layer1, layer2, layer3 };
            int count = 0;
            while (count < SplatLayers && given[count] != null) count++;
            if (count == 0) return "a splat needs at least one layer";
            for (int i = count; i < SplatLayers; i++) if (given[i] != null) return $"layer {i} comes after an empty layer {count}: the layers come in order";
            if (!b.givesUvs) return "this model was made without uvs, which a splat's layers are read at: give uvs with its first vertices";
            if (b.depthOnly) return "this model draws depth alone, and no colour to paint";
            if (b.additive) return "this model is added to what is behind it, which a splat's ground is not";
            if (b.texture != null) return "this model has a texture: a splat's layers replace it, so make the model with the splat alone";
            if (!b.splat && b.written) return "a splat is given before the model's first vertices, since it adds weights to them";
            if (b.splat && b.written && count != b.layerCount) return $"this splat has {b.layerCount} layers and its vertices {b.layerCount} weights each, so it keeps {b.layerCount}";
            var problem = Tiling(tileU, tileV);
            if (problem != null) return problem;
            if (!(heightBlend >= 0 && heightBlend <= 1)) return $"heightBlend is {heightBlend}, and runs from 0 (the weights alone) to 1";
            for (int i = 0; i < count; i++) {
                problem = Pixels(given[i], $"layer {i}");
                if (problem != null) return problem;
            }

            if (!b.splat) BecomeSplat(model, m, b);
            b.layerCount = count;
            var layers = new RenderTexture[count];
            for (int i = 0; i < count; i++) layers[i] = TakeGroundCopy(given[i]);
            if (b.layers != null) foreach (var old in b.layers) ReleaseGroundCopy(old);
            b.layers = layers;

            var material = b.material;
            if (ModelMaterialGenerator.GroundShader != null && material.shader == ModelMaterialGenerator.GroundShader) {
                for (int i = 0; i < SplatLayers; i++) material.SetTexture(SplatLayerIds[i], i < count ? layers[i] : null);
                material.SetTextureScale(BaseMapId, new Vector2(tileU, tileV));
                material.SetTextureOffset(BaseMapId, Vector2.zero);
                material.SetFloat(HeightBlendId, heightBlend);
            } else {
                // HDRP, where neither shader has a SubShader: glTFast's default material draws the first layer.
                material.mainTexture = layers[0];
                material.mainTextureScale = new Vector2(tileU, tileV);
            }
            return "";
        }

        /// <summary>
        /// Projects a splat's layers from the sides in world space, rather than reading them at the
        /// mesh's uvs: each of `metres0` to `metres3` is how far one repeat of that layer's picture
        /// spans, and 0 keeps that layer on the uvs and the splat's tiling. For ground too steep for
        /// uvs laid flat, where they stretch a picture down a cliff: rock, say. Each projection is
        /// a read, three at most for a layer, and a face takes only those it leans toward. Given
        /// after SetMeshSplat, and again whenever it changes. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshSplatTriplanar(int model, float metres0, float metres1, float metres2, float metres3) {
            if (!_models.TryGetValue(model, out var m) || m.built == null) return $"there is no mesh model {model}";
            var b = m.built;
            if (!b.splat) return "this model has no splat: give its layers first";
            var metres = new[] { metres0, metres1, metres2, metres3 };
            var perMetre = Vector4.zero;
            for (int i = 0; i < SplatLayers; i++) {
                var v = metres[i];
                if (!(v >= 0) || float.IsInfinity(v)) return $"layer {i}'s triplanar is {v}: metres one repeat spans, above 0, or 0 for its uvs";
                if (v > 0 && i >= b.layerCount) return $"layer {i} is triplanar, and this splat has {b.layerCount} layers";
                perMetre[i] = v > 0 ? 1 / v : 0;
            }
            var material = b.material;
            if (material.HasVector(SplatTriplanarId)) material.SetVector(SplatTriplanarId, perMetre);
            return "";
        }

        /// <summary>
        /// How a splat looks far off. Past `fadeDistance` metres from the camera its layers read
        /// coarser mips, two more each time the distance doubles, so a repeat far enough off draws
        /// its picture's mean colour rather than its motif in a grid across a far slope; 0 reads
        /// every layer at its own mip however far. `variation`, 0 to 1, varies the ground's
        /// brightness across the world by up to that much either way, in patches about 40 m and
        /// 160 m across; 0 keeps it even. Neither costs a texture read. Given after SetMeshSplat,
        /// and again whenever it changes. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshSplatFar(int model, float fadeDistance, float variation) {
            if (!_models.TryGetValue(model, out var m) || m.built == null) return $"there is no mesh model {model}";
            var b = m.built;
            if (!b.splat) return "this model has no splat: give its layers first";
            if (!(fadeDistance >= 0) || float.IsInfinity(fadeDistance)) return $"fadeDistance is {fadeDistance}: metres from the camera, above 0, or 0 for no fade";
            if (!(variation >= 0 && variation <= 1)) return $"variation is {variation}: from 0 to 1";
            var material = b.material;
            if (material.HasVector(SplatFarId)) material.SetVector(SplatFarId, new Vector4(fadeDistance > 0 ? 1 / fadeDistance : 0, variation, 0, 0));
            return "";
        }

        // A mesh model's first splat: GroundLit for its material, and weights (and colours) in its vertices.
        static void BecomeSplat(int model, Model m, BuiltMesh b) {
            b.splat = true;
            var layout = new List<VertexAttributeDescriptor>(b.layout);
            if (b.colorStream < 0) {
                b.colorStream = layout.Count;
                layout.Add(new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, b.colorStream));
            }
            // In the colour's stream, after it (ColorWeights). Unity takes a layout in its attributes' order.
            layout.Add(new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4, b.colorStream));
            layout.Sort((x, y) => x.attribute.CompareTo(y.attribute));
            b.layout = layout.ToArray();

            var shader = ModelMaterialGenerator.GroundShader;
            if (shader == null) return;
            var old = b.material;
            b.material = new Material(shader) { name = "ground", enableInstancing = true };
            b.material.SetColor(BaseColorId, b.baseColor);
            if (!Playing) b.material.hideFlags = HideFlags.DontSave;
            ApplyLook(b);
            foreach (var r in m.template.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = b.material;
            foreach (var a in _actors.Values) {
                if (a.model != model) continue;
                foreach (var r in a.go.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = b.material;
            }
            Kill(old);
        }

        static string Tiling(float u, float v) =>
            u > 0 && v > 0 && !float.IsInfinity(u) && !float.IsInfinity(v) ? null : $"tiling is {u} by {v}, and is a number above 0";

        static string Pixels(Texture texture, string what) =>
            texture.width > 0 && texture.height > 0 ? null : $"{what} has no pixels";

        /// <summary>The bridge's copy of a picture, made the first time a mesh model is given it.</summary>
        static RenderTexture TakeGroundCopy(Texture source) {
            if (!_groundCopies.TryGetValue(source, out var copy) || copy.texture == null) {
                var rt = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                    name = $"{source.name} (ground)",
                    useMipMap = true,
                    autoGenerateMips = true,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
                if (!Playing) rt.hideFlags = HideFlags.DontSave;
                rt.Create();
                // A picture that is not sRGB (a C# texture made linear) is copied as it reads.
                Graphics.Blit(source, rt);
                copy = new GroundCopy { texture = rt };
                _groundCopies[source] = copy;
            }
            copy.users++;
            return copy.texture;
        }

        static void ReleaseGroundCopy(RenderTexture texture) {
            if (texture == null) return;
            Texture source = null;
            foreach (var pair in _groundCopies) {
                if (pair.Value.texture != texture) continue;
                if (--pair.Value.users > 0) return;
                source = pair.Key;
                break;
            }
            if (source is null) return;
            _groundCopies.Remove(source);
            texture.Release();
            Kill(texture);
        }

        /// <summary>
        /// Copies a picture again into the ground copy every mesh model given it reads, after
        /// its pixels changed: a procedural texture handed to a mesh before it finished drawing.
        /// False when no mesh holds a copy of it, which is not an error.
        /// </summary>
        public static bool RefreshGroundCopy(Texture source) {
            if (source == null) return false;
            bool refreshed = RefreshNormalCopy(source);
            if (!_groundCopies.TryGetValue(source, out var copy) || copy.texture == null) return refreshed;
            Graphics.Blit(source, copy.texture);
            return true;
        }

        /// <summary>How many pictures the bridge holds a ground copy of.</summary>
        internal static int GroundCopyCount => _groundCopies.Count;

        internal static Texture MeshTextureOf(int model) => _models.TryGetValue(model, out var m) ? m.built?.texture : null;

        internal static RenderTexture[] SplatLayersOf(int model) => _models.TryGetValue(model, out var m) ? m.built?.layers : null;
    }
}
