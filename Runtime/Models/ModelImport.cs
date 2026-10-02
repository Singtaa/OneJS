using GLTFast;
using GLTFast.Schema;

namespace OneJS.Models {
    /// <summary>
    /// glTFast's importer, minus the vertex colours ModelLit never reads.
    ///
    /// glTFast gives a mesh with colours four vertex streams: position, normal and tangent, then
    /// colours, then UVs, then bones. Unity skins from three (colours and UVs share one), so every
    /// skinned model with colours made it log "Skinned mesh attributes use wrong streams" as the
    /// mesh was built, an orange line in every cart's console on every Run. The colours are
    /// dropped once the JSON is parsed, before any mesh exists, so the mesh is built in the layout
    /// Unity skins from. Where glTFast's own materials draw instead (no ModelLit SubShader), they
    /// read vertex colours, so nothing is dropped there.
    /// </summary>
    class ModelImport : GltfImport {
        readonly bool _dropColors;

        public ModelImport(IDeferAgent deferAgent, ModelMaterialGenerator materials)
            : base(deferAgent: deferAgent, materialGenerator: materials) {
            _dropColors = ModelMaterialGenerator.ModelShader != null;
        }

        protected override RootBase ParseJson(string json) {
            var root = base.ParseJson(json);
            if (!_dropColors || root?.Meshes == null) return root;
            foreach (var mesh in root.Meshes) {
                if (mesh?.Primitives == null) continue;
                foreach (var primitive in mesh.Primitives) {
                    if (primitive?.attributes != null) primitive.attributes.COLOR_0 = -1;
                }
            }
            return root;
        }
    }
}
