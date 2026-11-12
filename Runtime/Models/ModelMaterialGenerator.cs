using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using UnityEngine.Rendering;
using Material = UnityEngine.Material;

namespace OneJS.Models {
    /// <summary>
    /// Builds every glTF material on OneJS's ModelLit shader: base colour, metallic and roughness,
    /// normal, occlusion and emission maps, alpha mask and blend, double sided and unlit, plus the
    /// dissolve. One shader means a cart's models look the same in the Play container and in a
    /// Unity project.
    ///
    /// Where ModelLit has no SubShader for the pipeline (HDRP), glTFast's own generator makes the
    /// material instead: the model still looks right, and only the dissolve is lost.
    ///
    /// Not carried: texture transforms, a second UV set, a file's vertex colours (ModelLit draws
    /// them only for a mesh model, ModelBridge.CreateMesh) and glTF's other extensions (clearcoat,
    /// transmission, specular-glossiness).
    /// </summary>
    public class ModelMaterialGenerator : IMaterialGenerator {
        static Shader _shader;
        static Shader _groundShader;
        static Material _default;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int MetallicRoughnessMapId = Shader.PropertyToID("_MetallicRoughnessMap");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int RoughnessId = Shader.PropertyToID("_Roughness");
        static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
        static readonly int NormalScaleId = Shader.PropertyToID("_NormalScale");
        static readonly int OcclusionMapId = Shader.PropertyToID("_OcclusionMap");
        static readonly int OcclusionStrengthId = Shader.PropertyToID("_OcclusionStrength");
        static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        static readonly int UnlitId = Shader.PropertyToID("_Unlit");
        static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int PremultipliedId = Shader.PropertyToID("_Premultiplied");

        IMaterialGenerator _fallback;
        ICodeLogger _logger;

        /// <summary>
        /// GroundLit, ModelLit's pass code painting a mesh model from tiling layers by weights a
        /// vertex (ModelBridge.Ground.cs), or null where it has no SubShader for this pipeline.
        /// </summary>
        public static Shader GroundShader {
            get {
                if (_groundShader == null) _groundShader = Resources.Load<Shader>("OneJS/GroundLit");
                return _groundShader != null && _groundShader.isSupported ? _groundShader : null;
            }
        }

        /// <summary>The shader, or null where it has no SubShader for this pipeline.</summary>
        public static Shader ModelShader {
            get {
                if (_shader == null) _shader = Resources.Load<Shader>("OneJS/ModelLit");
                return _shader != null && _shader.isSupported ? _shader : null;
            }
        }

        IMaterialGenerator Fallback {
            get {
                if (_fallback == null) {
                    _fallback = MaterialGenerator.GetDefaultMaterialGenerator();
                    _fallback.SetLogger(_logger);
                }
                return _fallback;
            }
        }

        public Material GetDefaultMaterial(bool pointsSupport = false) {
            if (ModelShader == null) return Fallback.GetDefaultMaterial(pointsSupport);
            if (_default == null) _default = new Material(ModelShader) { name = "ModelLit default", enableInstancing = true };
            return _default;
        }

        public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false) {
            if (ModelShader == null) return Fallback.GenerateMaterial(gltfMaterial, gltf, pointsSupport);

            // Instanced, so actors that carry values of their own (ModelBridge.Apply) still draw
            // together; the rest draw through the SRP Batcher, which takes precedence. A build keeps
            // the instancing variants this needs because Resources/OneJS/ModelLitInstancing.mat,
            // which nothing loads, asks for them: these materials are made at runtime, after
            // stripping has decided.
            var material = new Material(ModelShader) { name = gltfMaterial.name, enableInstancing = true };
            var pbr = gltfMaterial.PbrMetallicRoughness;
            if (pbr != null) {
                // glTF factors are linear; SetColor takes the gamma value and converts it back.
                material.SetColor(BaseColorId, pbr.BaseColor.gamma);
                material.SetFloat(MetallicId, pbr.metallicFactor);
                material.SetFloat(RoughnessId, pbr.roughnessFactor);
                Assign(material, BaseMapId, pbr.BaseColorTexture, gltf);
                Assign(material, MetallicRoughnessMapId, pbr.MetallicRoughnessTexture, gltf);
            }
            if (Assign(material, NormalMapId, gltfMaterial.NormalTexture, gltf)) {
                material.SetFloat(NormalScaleId, gltfMaterial.NormalTexture.scale);
            }
            if (Assign(material, OcclusionMapId, gltfMaterial.OcclusionTexture, gltf)) {
                material.SetFloat(OcclusionStrengthId, gltfMaterial.OcclusionTexture.strength);
            }
            // The emission map multiplies the factor, so a map with no factor (glTF's default is
            // black) would be invisible; a lone map means "emit this".
            var emissive = gltfMaterial.Emissive;
            if (Assign(material, EmissionMapId, gltfMaterial.EmissiveTexture, gltf) && emissive == Color.black) emissive = Color.white;
            material.SetColor(EmissionColorId, emissive.gamma);

            if (gltfMaterial.Extensions?.KHR_materials_unlit != null) SetUnlit(material);
            if (gltfMaterial.doubleSided) material.SetFloat(CullId, (float)CullMode.Off);

            switch (gltfMaterial.GetAlphaMode()) {
                case MaterialBase.AlphaMode.Mask:
                    material.SetFloat(AlphaClipId, 1);
                    material.SetFloat(CutoffId, gltfMaterial.alphaCutoff);
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                    break;
                case MaterialBase.AlphaMode.Blend:
                    SetSeeThrough(material, true);
                    break;
                default:
                    material.renderQueue = (int)RenderQueue.Geometry;
                    break;
            }
            return material;
        }

        /// <summary>
        /// A ModelLit material blended over what is behind it, by its alpha, or drawn opaque: a glTF
        /// material in Blend mode, and a mesh model whose colour has an alpha below 1.
        /// </summary>
        public static void SetSeeThrough(Material material, bool seeThrough) {
            material.SetFloat(SurfaceId, seeThrough ? 1 : 0);
            material.SetFloat(SrcBlendId, (float)(seeThrough ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat(DstBlendId, (float)(seeThrough ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat(ZWriteId, seeThrough ? 0 : 1);
            material.SetOverrideTag("RenderType", seeThrough ? "Transparent" : "Opaque");
            material.renderQueue = (int)(seeThrough ? RenderQueue.Transparent : RenderQueue.Geometry);
        }

        /// <summary>
        /// A ModelLit material drawn in its own colours, with no sun, ambient, shadow or point light
        /// on it: a glTF material with KHR_materials_unlit, and a mesh model made `unlit`.
        /// </summary>
        public static void SetUnlit(Material material) => material.SetFloat(UnlitId, 1);

        /// <summary>
        /// A ModelLit material drawn after the opaques, writing no depth, and blended `blend`. Added
        /// (`blend: "add"`), by its alpha, as light is, and faded out by fog rather than toward its
        /// colour (the shader reads `_DstBlend` for that). Premultiplied (`blend: "premultiplied"`
        /// and `"premultipliedTexture"`), source One and destination OneMinusSrcAlpha, with
        /// `_Premultiplied` 1 where the shader multiplies the picture's colour by its alpha and 2
        /// where the picture comes multiplied already. Alpha leaves it as SetSeeThrough does.
        /// </summary>
        public static void SetBlend(Material material, MeshBlend blend) {
            SetSeeThrough(material, blend != MeshBlend.Alpha);
            material.SetFloat(PremultipliedId, blend == MeshBlend.Premultiplied ? 1 : blend == MeshBlend.PremultipliedTexture ? 2 : 0);
            if (blend == MeshBlend.Add) material.SetFloat(DstBlendId, (float)BlendMode.One);
            if (blend == MeshBlend.Premultiplied || blend == MeshBlend.PremultipliedTexture) material.SetFloat(SrcBlendId, (float)BlendMode.One);
        }

        public void SetLogger(ICodeLogger logger) {
            _logger = logger;
            _fallback?.SetLogger(logger);
        }

        static bool Assign(Material material, int id, TextureInfoBase info, IGltfReadable gltf) {
            if (info == null || info.index < 0) return false;
            var texture = gltf.GetTexture(info.index);
            if (texture == null) return false;
            material.SetTexture(id, texture);
            return true;
        }
    }
}
