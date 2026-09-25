using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;

namespace OneJS.Editor {
    /// <summary>
    /// Leaves the shader language VM (OneJS/FxProgram) out of a WebGL player,
    /// which draws programs only compiled and never loads it
    /// (<see cref="OneJS.SL.SLProgramBridge.CompiledOnly"/>). It sits in
    /// Resources, so without this every WebGL build compiles and ships it.
    ///
    /// Every variant is stripped rather than the asset removed: Resources
    /// always includes the asset, and an empty shader is a few hundred bytes.
    /// A build with ONEJS_SL_WEB_VM keeps it, because that build falls back
    /// to it; so does every other target.
    /// </summary>
    public class SLVmShaderStripper : BuildPlayerProcessor, IPreprocessShaders, IPostprocessBuildWithReport {
        const string VmShaderName = "OneJS/FxProgram";
        const string KeepVmDefine = "ONEJS_SL_WEB_VM";

        static bool s_Strip;

        public override int callbackOrder => 0;

        /// <summary>The only hook that sees extraScriptingDefines, so the decision is made here.</summary>
        public override void PrepareForBuild(BuildPlayerContext context) {
            var opts = context.BuildPlayerOptions;
            var playerDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
            s_Strip = StripsVm(opts.target, opts.extraScriptingDefines, playerDefines);
        }

        /// <summary>True when a build for `target` with these defines has no use for the VM shader.</summary>
        public static bool StripsVm(BuildTarget target, string[] extraDefines, string playerDefines) {
            if (target != BuildTarget.WebGL) return false;
            if (extraDefines != null && extraDefines.Contains(KeepVmDefine)) return false;
            return !(playerDefines ?? "").Split(';', ',').Select(d => d.Trim()).Contains(KeepVmDefine);
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data) {
            if (s_Strip && shader.name == VmShaderName) data.Clear();
        }

        // Not left set for an asset bundle build that follows in the same session.
        public void OnPostprocessBuild(BuildReport report) => s_Strip = false;
    }
}
