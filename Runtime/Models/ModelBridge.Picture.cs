using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// An actor's own picture: a texture drawn on it in place of the one its file carries, such
    /// as a look a cart composed at run time. The bridge keeps one mipmapped copy of a picture
    /// however many actors wear it (TakeGroundCopy, which a texture refreshed later updates), and
    /// one material for each of the model's materials and each picture, so actors that wear the
    /// same picture share it. Materials of one shader still batch under the SRP Batcher; what a
    /// picture costs is its memory and its material, not a property block on every draw.
    /// </summary>
    public static partial class ModelBridge {
        class Worn {
            public Material material;
            public int users;
        }

        // By the model's material and the picture's copy, each by reference.
        sealed class ByPair : IEqualityComparer<(Material, RenderTexture)> {
            public bool Equals((Material, RenderTexture) a, (Material, RenderTexture) b) => ReferenceEquals(a.Item1, b.Item1) && ReferenceEquals(a.Item2, b.Item2);
            public int GetHashCode((Material, RenderTexture) k) => RuntimeHelpers.GetHashCode(k.Item1) * 31 + RuntimeHelpers.GetHashCode(k.Item2);
        }

        static readonly Dictionary<(Material, RenderTexture), Worn> _worn = new(new ByPair());

        /// <summary>
        /// Draws `picture` on the actor in place of its model's own, or its model's own again with
        /// null. "" when set, or what was wrong.
        /// </summary>
        public static string SetPicture(int actor, Texture picture) {
            if (!_actors.TryGetValue(actor, out var a) || a.go == null) return "";
            if (picture == a.pictureSource) return "";
            if (_models.TryGetValue(a.model, out var m) && m.built != null) return "a mesh model takes its texture through scene.mesh, which every actor of it shares";
            if (picture != null) {
                if (ModelMaterialGenerator.ModelShader == null) return "this project draws models with glTFast's own materials, which take no picture of an actor's own";
                var problem = Pixels(picture, "the texture");
                if (problem != null) return problem;
            }
            Unpicture(a);
            if (picture == null) return "";
            var copy = TakeGroundCopy(picture);
            a.pictureSource = picture;
            a.picture = copy;
            a.ownMaterials = new Material[a.renderers.Length][];
            for (int i = 0; i < a.renderers.Length; i++) {
                var r = a.renderers[i];
                var own = r.sharedMaterials;
                a.ownMaterials[i] = own;
                var worn = new Material[own.Length];
                for (int k = 0; k < own.Length; k++) worn[k] = Wear(own[k], copy);
                r.sharedMaterials = worn;
            }
            return "";
        }

        /// <summary>The picture an actor wears, null for its model's own.</summary>
        internal static Texture PictureOf(int actor) => _actors.TryGetValue(actor, out var a) ? a.picture : null;

        /// <summary>How many materials the bridge made for pictures actors wear.</summary>
        internal static int WornMaterialCount => _worn.Count;

        // The model's material with the picture as its base map, made once for every actor wearing it.
        static Material Wear(Material own, RenderTexture copy) {
            if (own == null || own.shader != ModelMaterialGenerator.ModelShader) return own;
            var key = (own, copy);
            if (!_worn.TryGetValue(key, out var worn) || worn.material == null) {
                var material = new Material(own) { name = $"{own.name} ({copy.name})" };
                if (!Playing) material.hideFlags = HideFlags.DontSave;
                material.SetTexture(BaseMapId, copy);
                worn = new Worn { material = material };
                _worn[key] = worn;
            }
            worn.users++;
            return worn.material;
        }

        // Puts back the model's own materials and lets go of the picture: on SetPicture and Destroy.
        static void Unpicture(Actor a) {
            if (a.picture == null) return;
            for (int i = 0; i < a.renderers.Length; i++) {
                var r = a.renderers[i];
                if (r != null) {
                    foreach (var material in r.sharedMaterials) Unwear(material);
                    r.sharedMaterials = a.ownMaterials[i];
                }
            }
            ReleaseGroundCopy(a.picture);
            a.picture = null;
            a.pictureSource = null;
            a.ownMaterials = null;
        }

        static void Unwear(Material material) {
            if (material == null) return;
            foreach (var pair in _worn) {
                if (pair.Value.material != material) continue;
                if (--pair.Value.users > 0) return;
                _worn.Remove(pair.Key);
                Kill(material);
                return;
            }
        }

        static void StopPictures() {
            foreach (var a in _actors.Values) Unpicture(a);
            foreach (var worn in _worn.Values) if (worn.material != null) Kill(worn.material);
            _worn.Clear();
        }
    }
}
