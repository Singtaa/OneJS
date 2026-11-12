using System.Collections.Generic;
using GLTFast;
using GLTFast.Schema;

namespace OneJS.Models {
    /// <summary>
    /// glTFast's importer, minus a skinned mesh's vertex colours.
    ///
    /// glTFast gives a mesh with colours four vertex streams: position, normal and tangent, then
    /// colours, then UVs, then bones. Unity skins from three (colours and UVs share one), so every
    /// skinned model with colours made it log "Skinned mesh attributes use wrong streams" as the
    /// mesh was built, an orange line in every cart's console on every Run. A skinned mesh's colours
    /// are dropped once the JSON is parsed, before any mesh exists, so it is built in the layout
    /// Unity skins from. Any other mesh keeps them, and ModelBridge.Load gives its materials a copy
    /// that multiplies them into the base colour (COLOR_0, as glTF means it). Where glTFast's own
    /// materials draw instead (no ModelLit SubShader), they read vertex colours, so nothing is
    /// dropped there.
    /// </summary>
    class ModelImport : GltfImport {
        readonly bool _dropColors;

        public ModelImport(IDeferAgent deferAgent, ModelMaterialGenerator materials)
            : base(deferAgent: deferAgent, materialGenerator: materials) {
            _dropColors = ModelMaterialGenerator.ModelShader != null;
        }

        protected override RootBase ParseJson(string json) {
            var root = base.ParseJson(json);
            if (!_dropColors || root?.Meshes == null || root.Nodes == null) return root;
            var skinned = new HashSet<int>();
            foreach (var node in root.Nodes) if (node != null && node.skin >= 0 && node.mesh >= 0) skinned.Add(node.mesh);
            for (int i = 0; i < root.Meshes.Count; i++) {
                var mesh = root.Meshes[i];
                if (mesh?.Primitives == null || !skinned.Contains(i)) continue;
                foreach (var primitive in mesh.Primitives) {
                    if (primitive?.attributes != null) primitive.attributes.COLOR_0 = -1;
                }
            }
            return root;
        }
    }
}
