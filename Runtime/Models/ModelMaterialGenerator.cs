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
    /// Not carried: texture transforms, a second UV set, vertex colours and glTF's other
    /// extensions (clearcoat, transmission, specular-glossiness).
    /// </summary>
    public class ModelMaterialGenerator : IMaterialGenerator {
        static Shader _shader;
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

        IMaterialGenerator _fallback;
        ICodeLogger _logger;

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
            if (_default == null) _default = new Material(ModelShader) { name = "ModelLit default" };
            return _default;
        }

        public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false) {
            if (ModelShader == null) return Fallback.GenerateMaterial(gltfMaterial, gltf, pointsSupport);

            var material = new Material(ModelShader) { name = gltfMaterial.name };
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

            if (gltfMaterial.Extensions?.KHR_materials_unlit != null) material.SetFloat(UnlitId, 1);
            if (gltfMaterial.doubleSided) material.SetFloat(CullId, (float)CullMode.Off);

            switch (gltfMaterial.GetAlphaMode()) {
                case MaterialBase.AlphaMode.Mask:
                    material.SetFloat(AlphaClipId, 1);
                    material.SetFloat(CutoffId, gltfMaterial.alphaCutoff);
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                    break;
                case MaterialBase.AlphaMode.Blend:
                    material.SetFloat(SurfaceId, 1);
                    material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
                    material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat(ZWriteId, 0);
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                    break;
                default:
                    material.renderQueue = (int)RenderQueue.Geometry;
                    break;
            }
            return material;
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
