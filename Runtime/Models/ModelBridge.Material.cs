using UnityEngine;

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
            if (seeThrough != built.seeThrough && !built.depthOnly && built.cutout < 0) {
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
        /// off. Only on a mesh whose colour has an alpha below 1, without a cutout, not depth-only:
        /// an opaque mesh is in the depth it would read. URP only. The camera's depth texture is
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
            if (!(built.baseColor.a < 1)) return "this mesh draws opaque (its colour's alpha is 1), and an opaque mesh is in the depth it would read: give its colour an alpha below 1";
            built.depthFade = new Vector4(soften > 0 ? 1 / soften : 0, metres > 0 ? 3 / metres : 0, 0, 0);
            built.deepColor = new Color(r, g, b, a);
            ApplySurface(built);
            WantDepth(built, soften > 0 || metres > 0);
            return "";
        }

        /// <summary>A mesh model's roughness, metallic and depth fade, onto its material: again whenever the material is replaced (BecomeSplat).</summary>
        static void ApplySurface(BuiltMesh b) {
            if (b.material == null || ModelMaterialGenerator.ModelShader == null) return;
            b.material.SetFloat(RoughnessId, b.roughness);
            b.material.SetFloat(MetallicId, b.metallic);
            b.material.SetVector(DepthFadeId, b.depthFade);
            // SetColor takes the sRGB value and keeps it linear in a linear project, as _BaseColor.
            b.material.SetColor(DeepColorId, b.deepColor);
        }
    }
}
