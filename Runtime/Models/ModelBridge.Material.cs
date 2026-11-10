using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// A mesh model's surface: how rough and how metallic it is, and its colour after it is made
    /// (onejs-play's `scene.mesh({ roughness, metallic })` and `mesh.color`, `mesh.roughness` and
    /// `mesh.metallic`). Each is one value on the model's own material, which every actor and
    /// scattered copy of it draws, so a change shows on all of them at once and costs nothing a
    /// frame. ModelLit works out the sun's specular on every pixel already; a roughness of 1, a
    /// mesh's default, spreads it too wide to see.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int RoughnessId = Shader.PropertyToID("_Roughness");

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

        /// <summary>A mesh model's roughness and metallic, onto its material: again whenever the material is replaced (BecomeSplat).</summary>
        static void ApplySurface(BuiltMesh b) {
            if (b.material == null || ModelMaterialGenerator.ModelShader == null) return;
            b.material.SetFloat(RoughnessId, b.roughness);
            b.material.SetFloat(MetallicId, b.metallic);
        }
    }
}
