using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// A mesh model drawn as water (onejs-play's `scene.mesh({ water })`): what it reflects, its
    /// foam where it meets the world, and its swell. Three values on the model's own material, so
    /// every actor and scattered copy of it changes at once, and no new shader variant: ModelLit
    /// branches on them per draw (ModelSurface.hlsl, ModelWind.hlsl).
    ///
    /// Its colour, `deepColor`, `soften` and `normalMap` stay what they were, and water reads them:
    /// the normal map twice, the second time finer and crossing the first's flow, and the colour
    /// moves toward what it reflects by water's fresnel along the normal the ripples and swell bend.
    /// So its ripples show without a sun, in a scene lit by a bake or not lit at all.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int WaterId = Shader.PropertyToID("_Water");
        static readonly int WaterSkyId = Shader.PropertyToID("_WaterSky");
        static readonly int WaterFoamId = Shader.PropertyToID("_WaterFoam");

        /// <summary>The highest swell a mesh takes, in metres: past it, the waves' own slope folds them.</summary>
        public const float MaxSwell = 2;

        /// <summary>
        /// Draws a mesh model as water, or as it was with `on` false. It reflects `skyR, skyG, skyB`
        /// (sRGB) where `skyGiven`, else the scene's sky where the scene reflects it, else the
        /// ambient light from the way it reflects. `foamMetres` above 0 lays `foamR..foamA` (sRGB,
        /// with its alpha) over it where it is that shallow against the world behind, which needs a
        /// see-through mesh and asks for the camera's depth texture while it lives. `swell` lifts the
        /// surface by long waves rolling down the scene's wind, that many metres high, up to
        /// MaxSwell. Not with a splat, a depth-only mesh or an added one. URP only. "" when it took
        /// it, or what was wrong.
        /// </summary>
        public static string SetMeshWater(int model, bool on, bool skyGiven, float skyR, float skyG, float skyB,
            float foamR, float foamG, float foamB, float foamA, float foamMetres, float swell) {
            if (!Look(model, out var m, out var problem)) return problem;
            var b = m.built;
            if (!on) {
                b.water = Vector4.zero;
                ApplySurface(b);
                WantDepth(b.waterDepth, false);
                SwellBounds(m);
                return "";
            }
            if (CameraDepthPipeline == null) return "this project's render pipeline is not URP, and OneJS draws water on URP alone";
            if (b.splat) return "a splat is ground, and cannot be water";
            if (b.depthOnly) return "a depth-only mesh draws no colour to be water";
            if (b.additive) return "an added mesh adds light to what is behind it, and water covers what is behind it: leave blend as \"alpha\"";
            if (!Finite(skyR) || !Finite(skyG) || !Finite(skyB)) return "its sky is not a colour";
            if (!Finite(foamR) || !Finite(foamG) || !Finite(foamB) || !(foamA >= 0 && foamA <= 1)) return "its foam is not a colour";
            if (!(foamMetres >= 0) || float.IsInfinity(foamMetres)) return $"its foam's metres is {foamMetres}, and is metres from 0";
            if (!(swell >= 0 && swell <= MaxSwell)) return $"its swell is {swell}, and is metres from 0 to {MaxSwell}";
            if (foamMetres > 0) {
                if (b.cutout >= 0) return "a mesh with a cutout draws opaque, and foam is found by what is behind it";
                if (!b.seeThrough) return "this mesh draws opaque (its colour's alpha is 1), and foam is found by what is behind it: give its colour an alpha below 1";
            }
            b.water = new Vector4(1, foamMetres > 0 ? 1 / foamMetres : 0, swell, skyGiven ? 1 : 0);
            b.waterSky = new Color(skyR, skyG, skyB, 1);
            b.waterFoam = new Color(foamR, foamG, foamB, foamA);
            ApplySurface(b);
            WantDepth(b.waterDepth, foamMetres > 0);
            SwellBounds(m);
            return "";
        }

        /// <summary>A mesh model's water values, onto its material (from ApplySurface).</summary>
        static void ApplyWater(BuiltMesh b) {
            if (!b.material.HasProperty(WaterId)) return;
            b.material.SetVector(WaterId, b.water);
            // SetColor takes the sRGB value and keeps it linear in a linear project, as _BaseColor.
            b.material.SetColor(WaterSkyId, b.waterSky);
            b.material.SetColor(WaterFoamId, b.waterFoam);
        }

        /// <summary>
        /// The mesh's bounds as it draws: its vertices' box, made taller by as far as the swell can
        /// lift or drop it (1.4 times its height, both waves at a crest), so a camera culls it only
        /// where no wave of it shows.
        /// </summary>
        static void SwellBounds(Model m) {
            var b = m.built;
            if (b.mesh == null) return;
            var bounds = m.bounds;
            if (b.water.z > 0) bounds.Expand(new Vector3(0, 2.8f * b.water.z, 0));
            b.mesh.bounds = bounds;
        }
    }
}
