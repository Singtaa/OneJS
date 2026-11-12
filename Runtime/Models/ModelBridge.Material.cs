using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OneJS.Models {
    /// <summary>
    /// A mesh model's surface: how rough and how metallic it is, its colour after it is made, and a
    /// see-through mesh's fade by the depth behind it (onejs-play's `scene.mesh({ roughness,
    /// metallic, soften, deepColor })` and `mesh.color`, `mesh.roughness` and `mesh.metallic`). Each is one value on the model's own material, which every actor and
    /// scattered copy of it draws, so a change shows on all of them at once and costs nothing a
    /// frame. ModelLit works out the sun's specular on every pixel already; a roughness of 1, a
    /// mesh's default, spreads it too wide to see.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int RoughnessId = Shader.PropertyToID("_Roughness");
        static readonly int DepthFadeId = Shader.PropertyToID("_DepthFade");
        static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        static readonly int LeafLightId = Shader.PropertyToID("_LeafLight");
        static readonly int LeafTintId = Shader.PropertyToID("_LeafTint");
        static readonly int LeafEdgeId = Shader.PropertyToID("_LeafEdge");
        static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
        static readonly int NormalScaleId = Shader.PropertyToID("_NormalScale");
        static readonly int NormalFlowId = Shader.PropertyToID("_NormalFlow");
        static readonly int EncodeId = Shader.PropertyToID("_Encode");
        static readonly Dictionary<Texture, GroundCopy> _normalCopies = new(new ByReference());
        static Material _normalCopier;

        /// <summary>
        /// A mesh model's roughness and metallic, each 0 to 1, as glTF has them. A mesh is made
        /// at roughness 1 and metallic 0. "" when it took them, or what was wrong.
        /// </summary>
        public static string SetMeshSurface(int model, float roughness, float metallic) {
            if (!Look(model, out var m, out var problem)) return problem;
            if (!(roughness >= 0 && roughness <= 1)) return $"roughness is {roughness}, and runs from 0 to 1";
            if (!(metallic >= 0 && metallic <= 1)) return $"metallic is {metallic}, and runs from 0 to 1";
            m.built.roughness = roughness;
            m.built.metallic = metallic;
            ApplySurface(m.built);
            return "";
        }

        /// <summary>
        /// A mesh model's leaf light, each 0 to 1 and 0 for none (ModelLeafLight.hlsl): `wrap` lets
        /// the sun's light reach past the shadow line, as it does through a leaf or moss, and
        /// `backLight` lets the sun show through from behind, toward the eye, as it does through
        /// a leaf held up to it, tinted by the surface's colour and by `r, g, b` (sRGB) and gathered
        /// round the sun's line by `focus` (1 to 16; 4 is a broad glow, 8 a tight one). The sun
        /// only, URP only, and not with a splat, which is lit by its vertex normals. Every actor
        /// and copy of the model changes with it. "" when it took them, or what was wrong.
        /// </summary>
        public static string SetMeshLeafLight(int model, float wrap, float backLight, float focus, float r, float g, float b) {
            if (!Look(model, out var m, out var problem)) return problem;
            if (!(wrap >= 0 && wrap <= 1)) return $"wrap is {wrap}, and runs from 0 to 1";
            if (!(backLight >= 0 && backLight <= 1)) return $"backLight is {backLight}, and runs from 0 to 1";
            if (!(focus >= 1 && focus <= 16)) return $"backLightFocus is {focus}, and runs from 1 to 16";
            if (!(r >= 0 && r <= 1 && g >= 0 && g <= 1 && b >= 0 && b <= 1)) return $"backLightColor is ({r}, {g}, {b}), and each runs from 0 to 1";
            if (m.built.splat) return "a splat is lit by its vertex normals, and takes no leaf light";
            m.built.leafLight = new Vector3(wrap, backLight, focus);
            m.built.leafTint = new Color(r, g, b, 1);
            ApplySurface(m.built);
            return "";
        }

        /// <summary>
        /// Dithers a mesh model's faces away as they turn edge on to the eye (ModelEdgeFade.hlsl):
        /// whole while a face meets the eye more squarely than `end` (|N.V| of the triangle's own
        /// face, 0 edge on and 1 face on), gone below `start`, dithered between. A crown of leaf
        /// cards then shows no dark lines where cards turn side on. In the camera's passes, not the
        /// shadow's. 0 and 0 take it off. Not with a splat; URP only. "" when it took it, or what
        /// was wrong.
        /// </summary>
        public static string SetMeshEdgeFade(int model, float start, float end) {
            if (!Look(model, out var m, out var problem)) return problem;
            bool off = start == 0 && end == 0;
            if (!off && !(start >= 0 && end > start && end <= 1)) return $"edgeFade is [{start}, {end}], and is [start, end] with 0 <= start < end <= 1";
            if (m.built.splat) return "a splat is ground, and takes no edge fade";
            m.built.edgeFade = new Vector2(start, end);
            ApplySurface(m.built);
            return "";
        }

        /// <summary>
        /// A mesh model's colour in sRGB, multiplied by its vertex colours, as CreateMesh took it.
        /// An alpha that crosses 1 moves it between opaque and see-through, as one given at
        /// creation does; a cutout or depth-only mesh keeps drawing as it does. "" when it took
        /// it, or what was wrong.
        /// </summary>
        public static string SetMeshColor(int model, float r, float g, float b, float a) {
            if (!_models.TryGetValue(model, out var m) || m.built == null) return $"there is no mesh model {model}";
            var built = m.built;
            built.baseColor = new Color(r, g, b, a);
            if (ModelMaterialGenerator.ModelShader == null) {
                built.material.color = built.baseColor;
                return "";
            }
            built.material.SetColor(BaseColorId, built.baseColor);
            bool seeThrough = a < 1;
            if (built.givesColors) for (int v = 0; v < built.vertexCount && !seeThrough; v++) seeThrough = built.colors[v].a < 1;
            if (seeThrough != built.seeThrough && !built.depthOnly && built.cutout < 0 && !built.Blended) {
                ModelMaterialGenerator.SetSeeThrough(built.material, seeThrough);
                built.seeThrough = seeThrough;
            }
            return "";
        }

        /// <summary>
        /// Fades a see-through mesh model by the opaque world behind each pixel (ModelSurface.hlsl),
        /// from how many metres of it the pixel's ray crosses: its alpha fades to nothing over
        /// `soften` metres at a shore, and its colour and alpha move toward `r, g, b, a` (sRGB) as the
        /// thickness runs to `metres`, 95% of the way there. `soften` or `metres` 0 leaves that one
        /// off. Only on a mesh whose colour has an alpha below 1, or an added or premultiplied one, without a
        /// cutout, not depth-only: an opaque mesh is in the depth it would read. URP only. The camera's depth texture is
        /// asked for while the mesh lives. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshDepthFade(int model, float soften, float r, float g, float b, float a, float metres) {
            if (!Look(model, out var m, out var problem)) return problem;
            if (!(soften >= 0) || float.IsInfinity(soften)) return $"soften is {soften}, and is metres from 0";
            if (!(metres >= 0) || float.IsInfinity(metres)) return $"deepColor's metres is {metres}, and is metres from 0";
            // The built-in pipeline's surface shader has no fade: read there, the values failed
            // Vulkan's compile of its forward pass, and the Play container is URP.
            if (CameraDepthPipeline == null) return "this project's render pipeline is not URP, and OneJS fades a mesh by the depth behind it on URP alone";
            var built = m.built;
            if (built.splat) return "a splat draws opaque, and there is nothing behind it to fade by";
            if (built.depthOnly) return "a depth-only mesh draws no colour to fade";
            if (built.cutout >= 0) return "a mesh with a cutout draws opaque, and there is nothing behind it to fade by";
            if (!(built.baseColor.a < 1) && !built.Blended) return "this mesh draws opaque (its colour's alpha is 1), and an opaque mesh is in the depth it would read: give its colour an alpha below 1";
            built.depthFade = new Vector4(soften > 0 ? 1 / soften : 0, metres > 0 ? 3 / metres : 0, 0, 0);
            built.deepColor = new Color(r, g, b, a);
            ApplySurface(built);
            WantDepth(built, soften > 0 || metres > 0);
            return "";
        }

        /// <summary>
        /// Bends a mesh model's lighting by a normal map read at its uvs: `picture` from tex or
        /// loadTexture, read as the data it is rather than as colour, repeated `tileU` by `tileV`
        /// times a uv of its own, bending `scale` times as far as it says (glTF's normal scale), and
        /// sliding `flowU, flowV` uvs a second by the scene's clock, before its repeats. A mesh has no
        /// tangents, so its frame is worked out per pixel from how its position and uvs change across
        /// the screen. Null takes it off. Needs uvs, not with a splat, and URP only. Every actor and
        /// copy of the model changes with it. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshNormalMap(int model, Texture picture, float tileU, float tileV, float scale, float flowU, float flowV) {
            if (!Look(model, out var m, out var problem)) return problem;
            var b = m.built;
            // URP registers the depth hook, so it says the pipeline is URP (UrpCameraDepth.cs).
            if (CameraDepthPipeline == null) return "this project's render pipeline is not URP, and OneJS draws a mesh's normal map on URP alone";
            if (b.splat) return "a splat is lit by its vertex normals, and takes no normal map";
            if (picture != null && !b.givesUvs) return "this model was made without uvs, which a normal map is read at: give uvs with its first vertices";
            problem = Tiling(tileU, tileV) ?? (picture != null ? Pixels(picture, "the normal map") : null);
            if (problem != null) return problem;
            if (!Finite(scale)) return $"scale is {scale}, and is a number";
            if (!Finite(flowU) || !Finite(flowV)) return $"flow is {flowU} by {flowV}, and is uvs a second";
            var copy = picture == null ? null : TakeNormalCopy(picture);
            ReleaseNormalCopy(b.normalMap);
            b.normalMap = copy;
            b.normalTiling = new Vector2(tileU, tileV);
            b.normalScale = scale;
            b.normalFlow = new Vector2(flowU, flowV);
            ApplySurface(b);
            return "";
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>
        /// The bridge's linear copy of a normal map, with its mips, made the first time a mesh model
        /// is given the picture and shared by every one given it after. The picture's bytes, unchanged:
        /// an sRGB picture is decoded as it is sampled, so the copy encodes it back (NormalCopy.shader).
        /// </summary>
        static RenderTexture TakeNormalCopy(Texture source) {
            if (!_normalCopies.TryGetValue(source, out var copy) || copy.texture == null) {
                var rt = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                    name = $"{source.name} (normal)",
                    useMipMap = true,
                    autoGenerateMips = true,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
                if (!Playing) rt.hideFlags = HideFlags.DontSave;
                rt.Create();
                copy = new GroundCopy { texture = rt };
                _normalCopies[source] = copy;
                CopyNormals(source, rt);
            }
            copy.users++;
            return copy.texture;
        }

        static void CopyNormals(Texture source, RenderTexture into) {
            if (_normalCopier == null) {
                _normalCopier = new Material(Resources.Load<Shader>("OneJS/NormalCopy")) { name = "normal copy", hideFlags = HideFlags.HideAndDontSave };
            }
            // Decoded as it is read only where the project is linear and the picture is sRGB.
            bool srgb = source is RenderTexture r ? r.sRGB : GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            _normalCopier.SetFloat(EncodeId, srgb && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            Graphics.Blit(source, into, _normalCopier);
        }

        static bool RefreshNormalCopy(Texture source) {
            if (!_normalCopies.TryGetValue(source, out var copy) || copy.texture == null) return false;
            CopyNormals(source, copy.texture);
            return true;
        }

        static void ReleaseNormalCopy(RenderTexture texture) {
            if (texture == null) return;
            Texture source = null;
            foreach (var pair in _normalCopies) {
                if (pair.Value.texture != texture) continue;
                if (--pair.Value.users > 0) return;
                source = pair.Key;
                break;
            }
            if (source is null) return;
            _normalCopies.Remove(source);
            texture.Release();
            Kill(texture);
        }

        /// <summary>How many pictures the bridge holds a normal map copy of.</summary>
        internal static int NormalCopyCount => _normalCopies.Count;

        /// <summary>A mesh model's roughness, metallic and depth fade, onto its material: again whenever the material is replaced (BecomeSplat).</summary>
        static void ApplySurface(BuiltMesh b) {
            if (b.material == null || ModelMaterialGenerator.ModelShader == null) return;
            b.material.SetFloat(RoughnessId, b.roughness);
            b.material.SetFloat(MetallicId, b.metallic);
            b.material.SetVector(DepthFadeId, b.depthFade);
            // SetColor takes the sRGB value and keeps it linear in a linear project, as _BaseColor.
            b.material.SetColor(DeepColorId, b.deepColor);
            // A splat's GroundLit has no leaf light, and is refused it.
            if (b.material.HasProperty(LeafLightId)) {
                b.material.SetVector(LeafLightId, b.leafLight);
                // SetColor takes the sRGB value and keeps it linear in a linear project, as _BaseColor.
                b.material.SetColor(LeafTintId, b.leafTint);
                b.material.SetVector(LeafEdgeId, b.edgeFade);
            }
            // A mesh without a normal map keeps the flat `bump` picture and its frame as it was.
            if (b.normalMap == null) {
                b.material.SetTexture(NormalMapId, null);
                b.material.SetVector(NormalFlowId, Vector4.zero);
                return;
            }
            b.material.SetTexture(NormalMapId, b.normalMap);
            b.material.SetTextureScale(NormalMapId, b.normalTiling);
            b.material.SetFloat(NormalScaleId, b.normalScale);
            // z says the frame is worked out per pixel (ModelSurface.hlsl), since a mesh has no tangents.
            b.material.SetVector(NormalFlowId, new Vector4(b.normalFlow.x, b.normalFlow.y, 1, 0));
        }
    }
}
