using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// Shine: a small picture looked up by the direction a pixel's surface faces the camera (its
    /// view-space normal), so a glint moves over a blade as the view turns whatever the sun does.
    /// The look games call a matcap. Two pictures for the whole scene, one a mode
    /// (SetShinePictures): `add` puts its picture onto the lit colour, for weapons; `multiply`
    /// multiplies the lit colour by twice its picture, for armour, so a mid grey changes nothing.
    ///
    /// A model or mesh model takes one mode at a strength (SetModelShine, SetMeshShine), held on
    /// its material as _Shine, where the picture's alpha says how much of each texel shines (an
    /// opaque picture's alpha is otherwise unused). An actor scales its model's strength by its
    /// own (SetActorShine), carried in its colour row's first texel's alpha, so a shining actor
    /// stays in the SRP Batcher with every other. The pictures are globals, so shine adds no
    /// material, no draw and no shader variant. URP's lit pass only (ModelLitForward.hlsl).
    /// </summary>
    public static partial class ModelBridge {
        static readonly int ShineId = Shader.PropertyToID("_Shine");
        static readonly int ShineAddId = Shader.PropertyToID("_OjShineAdd");
        static readonly int ShineMultiplyId = Shader.PropertyToID("_OjShineMultiply");
        static readonly int ShineOnId = Shader.PropertyToID("_OjShineOn");

        // The bridge's linear copies of the two pictures (TakeNormalCopy): the reference reads
        // their bytes as amounts, not as colours, so mid grey is half whatever the picture's format.
        static RenderTexture _shineAdd, _shineMultiply;

        /// <summary>
        /// The scene's two shine pictures: the one `add` models read, and the one `multiply`
        /// models read. Either may be null, which shines nothing in its mode. Their bytes are read
        /// as they are, not decoded as colours, and up is the top of the picture.
        /// </summary>
        public static void SetShinePictures(Texture add, Texture multiply) {
            var addCopy = add == null ? null : TakeNormalCopy(add);
            var multiplyCopy = multiply == null ? null : TakeNormalCopy(multiply);
            ReleaseNormalCopy(_shineAdd);
            ReleaseNormalCopy(_shineMultiply);
            _shineAdd = addCopy;
            _shineMultiply = multiplyCopy;
            SendShine();
        }

        // A mode without a picture shines nothing (_OjShineOn's x for add, y for multiply, 1 where
        // there is one), so a model given its mode before the scene has a picture draws as it did.
        static void SendShine() {
            Shader.SetGlobalTexture(ShineAddId, _shineAdd != null ? _shineAdd : Texture2D.blackTexture);
            Shader.SetGlobalTexture(ShineMultiplyId, _shineMultiply != null ? _shineMultiply : Texture2D.blackTexture);
            Shader.SetGlobalVector(ShineOnId, new Vector4(_shineAdd != null ? 1 : 0, _shineMultiply != null ? 1 : 0, 0, 0));
        }

        static void StopShine() {
            ReleaseNormalCopy(_shineAdd);
            ReleaseNormalCopy(_shineMultiply);
            _shineAdd = null;
            _shineMultiply = null;
            SendShine();
        }

        // "add" and "multiply" as _Shine's x and y; "" or "none" for neither.
        static string ShineOf(string mode, float strength, out Vector4 shine) {
            shine = Vector4.zero;
            if (!(strength >= 0 && strength <= 1)) return $"the shine's strength is {strength}, and runs from 0 to 1";
            switch (mode) {
                case "add": shine.x = strength; return "";
                case "multiply": shine.y = strength; return "";
                case "":
                case "none": return "";
                default: return $"the shine's mode is \"{mode}\", and is \"add\" or \"multiply\"";
            }
        }

        /// <summary>
        /// A loaded model's shine: `mode` "add" or "multiply" ("none" for neither), at `strength`
        /// from 0 to 1, onto every ModelLit material it draws with, so every actor of it shines
        /// alike, scaled by its own (SetActorShine). "" when set, or what was wrong.
        /// </summary>
        public static string SetModelShine(int model, string mode, float strength) {
            if (!_models.TryGetValue(model, out var m) || m.template == null) return "there is no such model";
            if (m.built != null) return SetMeshShine(model, mode, strength);
            if (ModelMaterialGenerator.ModelShader == null) return "this project draws models with glTFast's own materials, which have no shine";
            var problem = ShineOf(mode, strength, out var shine);
            if (problem != "") return problem;
            foreach (var r in m.template.GetComponentsInChildren<Renderer>(true)) {
                foreach (var material in r.sharedMaterials) {
                    if (material == null || material.shader != ModelMaterialGenerator.ModelShader) continue;
                    material.SetVector(ShineId, shine);
                }
            }
            return "";
        }

        /// <summary>A mesh model's shine, as SetModelShine's. "" when set, or what was wrong.</summary>
        public static string SetMeshShine(int model, string mode, float strength) {
            if (!Look(model, out var m, out var problem)) return problem;
            if (m.built.splat) return "a splat ground has no shine";
            problem = ShineOf(mode, strength, out var shine);
            if (problem != "") return problem;
            m.built.shine = shine;
            ApplySurface(m.built);
            return "";
        }

        /// <summary>
        /// How much of its model's shine an actor has, 0 to 1: 1, the default, is all of it, 0
        /// none. Carried in the alpha of its colour row's tint, so the actor keeps batching.
        /// "" when set, or what was wrong.
        /// </summary>
        public static string SetActorShine(int actor, float strength) {
            if (!_actors.TryGetValue(actor, out var a)) return "";
            if (!(strength >= 0 && strength <= 1)) return $"the actor's shine is {strength}, and runs from 0 to 1";
            a.tint.a = strength;
            return Recolor(a);
        }

        /// <summary>The actor's shine, 1 for an actor with no colour row.</summary>
        internal static float ActorShine(int actor) => _actors.TryGetValue(actor, out var a) ? a.tint.a : 1;
    }
}
