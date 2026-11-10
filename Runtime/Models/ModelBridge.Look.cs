using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Models {
    /// <summary>
    /// How a mesh model draws besides its colours and pictures: cut out by its alpha, seen from both
    /// sides, and swayed by the scene's wind (onejs-play's `scene.mesh({ cutout, twoSided, sway })`
    /// and `scene.wind`). Foliage wants all three: a leaf card is a quad cut to a leaf's outline, lit
    /// alike from either side, that moves in the wind. Each holds through scatter, since its copies
    /// draw the model's own material.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int SwayId = Shader.PropertyToID("_Sway");
        static readonly int KeepBackNormalId = Shader.PropertyToID("_KeepBackNormal");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        static readonly int WindId = Shader.PropertyToID("_OjWind");
        static readonly int WindClockId = Shader.PropertyToID("_OjWindClock");

        /// <summary>A swaying mesh model's first stream: each vertex's position, then how far it may sway (TEXCOORD3).</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct PositionSway {
            public Vector3 position;
            public float sway;
        }

        /// <summary>
        /// Cuts a mesh model's pixels away where its alpha (picture, colour and vertex colours
        /// multiplied) is below `cutout`, in its shadow and depth as in its colour. It draws opaque,
        /// with no sorting, even where an alpha below 1 would have drawn it see-through. "" when it
        /// took it, or what was wrong.
        /// </summary>
        public static string SetMeshCutout(int model, float cutout) {
            if (!Look(model, out var m, out var problem)) return problem;
            if (!(cutout >= 0 && cutout <= 1)) return $"cutout is {cutout}, and runs from 0 to 1";
            var b = m.built;
            if (b.splat) return "a splat has no alpha to cut out";
            b.cutout = cutout;
            ApplyLook(b);
            return "";
        }

        /// <summary>
        /// Draws both faces of a mesh model's triangles, in its shadow too, and lights a back face
        /// with its normal as given rather than flipped: a foliage card's normals bend out from its
        /// crown, and flipped, half the crown would darken. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshTwoSided(int model) {
            if (!Look(model, out var m, out var problem)) return problem;
            m.built.twoSided = true;
            ApplyLook(m.built);
            return "";
        }

        /// <summary>
        /// Gives a mesh model a sway a vertex, which every SetMesh then carries (its last argument):
        /// how far, in the model's own units, the scene's wind moves that vertex at a strength of 1.
        /// 0 at a trunk's foot, more toward a crown's rim. Before the model's first vertices, since
        /// it adds a stream to them. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshSway(int model) {
            if (!Look(model, out var m, out var problem)) return problem;
            var b = m.built;
            if (b.sways) return "";
            if (b.written) return "sway is given before the model's first vertices";
            b.sways = true;
            var layout = new List<VertexAttributeDescriptor>(b.layout) {
                // Beside the position, in its stream: a mesh has four streams, and a splat with uvs and colours uses them all.
                new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 1, 0),
            };
            layout.Sort((x, y) => x.attribute.CompareTo(y.attribute));
            b.layout = layout.ToArray();
            ApplyLook(b);
            return "";
        }

        static bool Look(int model, out Model m, out string problem) {
            problem = null;
            if (!_models.TryGetValue(model, out m) || m.built == null) problem = $"there is no mesh model {model}";
            else if (ModelMaterialGenerator.ModelShader == null) problem = "this host draws models with a renderer OneJS's ModelLit has no pass for (HDRP)";
            return problem == null;
        }

        /// <summary>A mesh model's cutout, sides, sway and surface, onto its material: again whenever the material is replaced (BecomeSplat).</summary>
        static void ApplyLook(BuiltMesh b) {
            var material = b.material;
            if (material == null || ModelMaterialGenerator.ModelShader == null) return;
            if (b.cutout >= 0) {
                if (b.seeThrough) {
                    ModelMaterialGenerator.SetSeeThrough(material, false);
                    b.seeThrough = false;
                }
                material.SetFloat(AlphaClipId, 1);
                material.SetFloat(CutoffId, b.cutout);
                if (!b.depthOnly) material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            if (b.twoSided) {
                material.SetFloat(CullId, (float)CullMode.Off);
                material.SetFloat(KeepBackNormalId, 1);
            }
            if (b.sways) material.SetFloat(SwayId, 1);
            ApplySurface(b);
        }

        /// <summary>
        /// The scene's wind, which moves every swaying mesh model: `dx` and `dz` the way it blows over
        /// the ground (any length but 0), `strength` how far it pushes (1 moves a vertex by its sway),
        /// and `gust` how much a slow swell passing along it adds and takes away (0 to 1). "" when it
        /// took it, or what was wrong.
        /// </summary>
        public static string SetWind(float dx, float dz, float strength, float gust) {
            var way = new Vector2(dx, dz);
            if (!(way.magnitude > 0) || float.IsInfinity(way.magnitude)) return $"the wind's direction is [{dx}, {dz}], which points nowhere";
            if (!(strength >= 0) || float.IsInfinity(strength)) return $"the wind's strength is {strength}, and is 0 or more";
            if (!(gust >= 0 && gust <= 1)) return $"the wind's gust is {gust}, and runs from 0 to 1";
            way.Normalize();
            Shader.SetGlobalVector(WindId, new Vector4(way.x, way.y, strength, gust));
            return "";
        }

        /// <summary>The scene's clock the wind sways by, in seconds: onejs-play's own, so a paused scene's trees stand still.</summary>
        public static void SetWindClock(float seconds) => Shader.SetGlobalVector(WindClockId, new Vector4(seconds, 0, 0, 0));

        /// <summary>No wind, as before any scene: the scene's teardown.</summary>
        static void StopWind() {
            Shader.SetGlobalVector(WindId, Vector4.zero);
            Shader.SetGlobalVector(WindClockId, Vector4.zero);
        }

        /// <summary>The wind the shaders read (ModelWind.hlsl): direction x and z, strength, gust.</summary>
        internal static Vector4 Wind => Shader.GetGlobalVector(WindId);
    }
}
