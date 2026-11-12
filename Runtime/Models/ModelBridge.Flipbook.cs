using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// A mesh model's base map played as a flipbook (onejs-play's `scene.mesh({ flipbook })`): a loop of
    /// pictures, each a ground copy as a texture is (repeating, mip-mapped), shown in turn on the scene's
    /// clock. ModelLit reads the frame showing and the next, and cross-fades them by how far the clock is
    /// between the two (_Flipbook), so the loop never steps. The frames are swapped on the model's own
    /// material when the clock moves (SetWindClock), two textures and a value a flipbook a frame, so
    /// every actor and scattered copy of it plays in step, and no shader variant is added.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int FrameNextId = Shader.PropertyToID("_FrameNext");
        static readonly int FlipbookId = Shader.PropertyToID("_Flipbook");
        static readonly HashSet<BuiltMesh> _flipbooks = new();
        static float _flipbookClock;

        /// <summary>The most frames a flipbook takes.</summary>
        public const int MaxFrames = 256;

        /// <summary>
        /// Starts a flipbook of `count` frames on a mesh model, playing once through every `seconds`,
        /// repeated `tileU` by `tileV` times across a uv of 0 to 1, in place of its texture; its frames
        /// come after, by SetMeshFlipbookFrame, and until each has come it shows the mesh's colour alone there. A
        /// count of 0 takes the flipbook off. The model must be made with uvs; not on a splat. "" when
        /// it took it, or what was wrong.
        /// </summary>
        public static string SetMeshFlipbook(int model, int count, float seconds, float tileU, float tileV) {
            if (!_models.TryGetValue(model, out var m) || m.built == null) return $"there is no mesh model {model}";
            var b = m.built;
            if (count == 0) {
                StopFlipbook(b);
                return "";
            }
            if (b.splat) return "this model is painted by a splat, which draws its layers, and takes no flipbook";
            if (!b.givesUvs) return "this model was made without uvs, which a flipbook is read at: give uvs with its first vertices";
            if (count < 2 || count > MaxFrames) return $"a flipbook has {count} frames, and has from 2 to {MaxFrames}";
            if (!(seconds > 0) || float.IsInfinity(seconds)) return $"a flipbook plays through in {seconds} seconds, and takes more than 0";
            var problem = Tiling(tileU, tileV);
            if (problem != null) return problem;

            StopFlipbook(b);
            ReleaseGroundCopy(b.texture);
            b.texture = null;
            b.frames = new RenderTexture[count];
            b.frameSeconds = seconds;
            _flipbooks.Add(b);
            if (ModelMaterialGenerator.ModelShader != null) {
                b.material.SetTextureScale(BaseMapId, new Vector2(tileU, tileV));
                b.material.SetTextureOffset(BaseMapId, Vector2.zero);
            }
            ShowFlipbook(b);
            return "";
        }

        /// <summary>
        /// Gives a mesh model's flipbook its frame at `index`, from 0, any picture from loadTexture or
        /// tex. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshFlipbookFrame(int model, int index, Texture frame) {
            if (!_models.TryGetValue(model, out var m) || m.built == null) return $"there is no mesh model {model}";
            var b = m.built;
            if (b.frames == null) return "this model has no flipbook: start one with SetMeshFlipbook";
            if (index < 0 || index >= b.frames.Length) return $"frame {index} is not one of its {b.frames.Length}, which run from 0";
            if (frame == null) return "the frame is not a picture";
            var problem = Pixels(frame, "the frame");
            if (problem != null) return problem;
            var copy = TakeGroundCopy(frame);
            ReleaseGroundCopy(b.frames[index]);
            b.frames[index] = copy;
            ShowFlipbook(b);
            return "";
        }

        /// <summary>A model's flipbook off, and its frames' copies let go: its base map is none after, as a mesh without a texture.</summary>
        static void StopFlipbook(BuiltMesh b) {
            if (b.frames == null) return;
            foreach (var frame in b.frames) ReleaseGroundCopy(frame);
            b.frames = null;
            _flipbooks.Remove(b);
            if (b.material != null && ModelMaterialGenerator.ModelShader != null) {
                b.material.SetTexture(BaseMapId, null);
                b.material.SetTexture(FrameNextId, null);
                b.material.SetVector(FlipbookId, Vector4.zero);
            }
        }

        /// <summary>Every flipbook at the scene's clock, from SetWindClock.</summary>
        static void PlayFlipbooks(float seconds) {
            _flipbookClock = seconds;
            foreach (var b in _flipbooks) ShowFlipbook(b);
        }

        /// <summary>
        /// The frame a flipbook shows at the clock, the next, and how far between them, onto its
        /// material: the clock's place in the loop, as frames, split into which and how far.
        /// </summary>
        static void ShowFlipbook(BuiltMesh b) {
            if (b.material == null || ModelMaterialGenerator.ModelShader == null) return;
            int count = b.frames.Length;
            float at = Mathf.Repeat(_flipbookClock / b.frameSeconds, 1) * count;
            int index = Mathf.Min((int)at, count - 1);
            b.material.SetTexture(BaseMapId, b.frames[index]);
            b.material.SetTexture(FrameNextId, b.frames[(index + 1) % count]);
            b.material.SetVector(FlipbookId, new Vector4(1, at - index, 0, 0));
        }

        /// <summary>How many flipbooks are playing: the scene's, for its tests.</summary>
        internal static int FlipbookCount => _flipbooks.Count;
    }
}
