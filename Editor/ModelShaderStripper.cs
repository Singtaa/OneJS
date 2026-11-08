using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Editor {
    /// <summary>
    /// Leaves ModelLit's and GroundLit's built-in pipeline SubShaders out of builds that only ever render through a
    /// scriptable pipeline. Unity compiles every SubShader a shader has, whichever pipeline the
    /// project uses, so a URP build otherwise carries variants it can never draw.
    /// </summary>
    public class ModelShaderStripper : IPreprocessShaders {
        static readonly string[] ShaderNames = { "OneJS/ModelLit", "OneJS/GroundLit" };
        static readonly ShaderTagId PipelineTag = new("RenderPipeline");

        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data) {
            if (Ours(shader) && Strips(shader, snippet.pass.SubshaderIndex, ScriptableOnly())) data.Clear();
        }

        /// <summary>Whether a SubShader of `shader` is dead weight in a build.</summary>
        public static bool Strips(Shader shader, uint subshader, bool scriptableOnly) {
            if (!scriptableOnly || !Ours(shader)) return false;
            return shader.FindSubshaderTagValue((int)subshader, PipelineTag) == ShaderTagId.none;
        }

        static bool Ours(Shader shader) => shader != null && System.Array.IndexOf(ShaderNames, shader.name) >= 0;

        // True when no quality level falls back to the built-in pipeline.
        static bool ScriptableOnly() {
            if (GraphicsSettings.defaultRenderPipeline != null) return true;
            for (var i = 0; i < QualitySettings.count; i++) {
                if (QualitySettings.GetRenderPipelineAssetAt(i) == null) return false;
            }
            return true;
        }
    }
}
