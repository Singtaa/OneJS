using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using Material = UnityEngine.Material;

namespace OneJS.Models {
    /// <summary>
    /// Builds every glTF material on OneJS's own ModelLit shader instead of glTFast's Shader
    /// Graphs: base colour, normal and occlusion maps, a main light, flat ambient, and the
    /// dissolve threshold. One small shader looks the same on the web and in a Unity project,
    /// works in URP and the built-in pipeline, and keeps glTFast's graphs out of a build.
    /// Metallic, roughness, emission and transparency are not carried.
    /// </summary>
    public class ModelMaterialGenerator : IMaterialGenerator {
        static Shader _shader;
        static Material _default;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
        static readonly int OcclusionMapId = Shader.PropertyToID("_OcclusionMap");

        static Shader ModelShader {
            get {
                if (_shader == null) _shader = Resources.Load<Shader>("OneJS/ModelLit");
                return _shader;
            }
        }

        public Material GetDefaultMaterial(bool pointsSupport = false) {
            if (_default == null) _default = new Material(ModelShader) { name = "ModelLit default" };
            return _default;
        }

        public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false) {
            var material = new Material(ModelShader) { name = gltfMaterial.name };
            var pbr = gltfMaterial.PbrMetallicRoughness;
            if (pbr != null) {
                material.SetColor(BaseColorId, pbr.BaseColor);
                Assign(material, BaseMapId, pbr.BaseColorTexture, gltf);
            }
            if (Assign(material, NormalMapId, gltfMaterial.NormalTexture, gltf)) material.EnableKeyword("_NORMALMAP");
            if (Assign(material, OcclusionMapId, gltfMaterial.OcclusionTexture, gltf)) material.EnableKeyword("_OCCLUSIONMAP");
            if (gltfMaterial.doubleSided) material.SetFloat("_Cull", 0);
            return material;
        }

        public void SetLogger(ICodeLogger logger) { }

        static bool Assign(Material material, int id, TextureInfoBase info, IGltfReadable gltf) {
            if (info == null || info.index < 0) return false;
            var texture = gltf.GetTexture(info.index);
            if (texture == null) return false;
            material.SetTexture(id, texture);
            return true;
        }
    }
}
