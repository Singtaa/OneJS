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
    /// Leaves the shader language VM (OneJS/FxProgram) out of a player that
    /// never draws with it, which is every player by default
    /// (<see cref="OneJS.SL.SLProgramBridge.VmAllowed"/>). It sits in Resources,
    /// so without this every build compiles and ships it.
    ///
    /// Every variant is stripped rather than the asset removed: Resources
    /// always includes the asset, and an empty shader is a few hundred bytes.
    /// A native build with ONEJS_SL_VM keeps it, because that build falls back
    /// to it for a program with no compiled shader; so does a WebGL build with
    /// ONEJS_SL_WEB_VM, which the parity harness measures against.
    /// </summary>
    public class SLVmShaderStripper : BuildPlayerProcessor, IPreprocessShaders, IPostprocessBuildWithReport {
        const string VmShaderName = "OneJS/FxProgram";
        const string KeepVmDefine = "ONEJS_SL_VM";
        const string KeepWebVmDefine = "ONEJS_SL_WEB_VM";

        static bool s_Strip;

        public override int callbackOrder => 0;

        /// <summary>The only hook that sees extraScriptingDefines, so the decision is made here.</summary>
        public override void PrepareForBuild(BuildPlayerContext context) {
            var opts = context.BuildPlayerOptions;
            var group = BuildPipeline.GetBuildTargetGroup(opts.target);
            var playerDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group));
            s_Strip = StripsVm(opts.target, opts.extraScriptingDefines, playerDefines);
        }

        /// <summary>True when a build for `target` with these defines has no use for the VM shader.</summary>
        public static bool StripsVm(BuildTarget target, string[] extraDefines, string playerDefines) {
            var keep = target == BuildTarget.WebGL ? KeepWebVmDefine : KeepVmDefine;
            if (extraDefines != null && extraDefines.Contains(keep)) return false;
            return !(playerDefines ?? "").Split(';', ',').Select(d => d.Trim()).Contains(keep);
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data) {
            if (s_Strip && shader.name == VmShaderName) data.Clear();
        }

        // Not left set for an asset bundle build that follows in the same session.
        public void OnPostprocessBuild(BuildReport report) => s_Strip = false;
    }
}
