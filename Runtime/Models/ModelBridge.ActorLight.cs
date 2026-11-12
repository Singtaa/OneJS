using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Models {
    /// <summary>
    /// An actor's own light (onejs-play's `actor.light`): an ambient colour and one directional light
    /// that light that actor alone, in place of the scene's ambient, its probes and its baked light.
    /// A cart sets it each frame from what is round the actor (the colour of the floor under its
    /// feet), so a body indoors is lit as its room is, for one SH a renderer through the property
    /// block the probes already use, with no new shader variant. The scene's sun and point lights
    /// still fall on it as on anything else, so a scene lit this way usually has no sun.
    /// </summary>
    public static partial class ModelBridge {
        /// <summary>
        /// Lights an actor by an ambient colour (`ar, ag, ab`, sRGB) from every side and a directional
        /// light of colour `dr, dg, db` (sRGB) times `intensity`, shining along `dx, dy, dz`. A surface
        /// facing the light is lit the ambient plus the light; it is held as spherical harmonics, as a
        /// probe is, so the light wraps softly past the side facing it rather than ending on a line,
        /// and the far side keeps about a twentieth of it. "" when set, or what was wrong.
        /// </summary>
        public static string SetActorLight(int actor, float ar, float ag, float ab, float dr, float dg, float db, float intensity, float dx, float dy, float dz) {
            if (!_actors.TryGetValue(actor, out var a)) return "";
            if (!Finite(ar) || !Finite(ag) || !Finite(ab)) return $"the ambient is {ar}, {ag}, {ab}, and is a colour";
            if (!Finite(dr) || !Finite(dg) || !Finite(db)) return $"the light's colour is {dr}, {dg}, {db}, and is a colour";
            if (!(intensity >= 0) || float.IsInfinity(intensity)) return $"the light's intensity is {intensity}, and is 0 or more";
            var toward = -new Vector3(dx, dy, dz);
            if (!(toward.magnitude > 0) || float.IsInfinity(toward.magnitude)) return $"the light's direction is [{dx}, {dy}, {dz}], which points nowhere";
            var ambient = new Color(Mathf.Max(0, ar), Mathf.Max(0, ag), Mathf.Max(0, ab)).linear;
            var direct = new Color(Mathf.Max(0, dr), Mathf.Max(0, dg), Mathf.Max(0, db)).linear * intensity;
            a.light = ActorLightProbe(ambient, direct, toward.normalized);
            a.lit = true;
            Apply(a);
            return "";
        }

        /// <summary>Gives an actor back to the scene's ambient, probes and baked light.</summary>
        public static void ClearActorLight(int actor) {
            if (!_actors.TryGetValue(actor, out var a) || !a.lit) return;
            a.lit = false;
            a.light = default;
            Apply(a);
        }

        /// <summary>
        /// Linear `ambient` from every side plus linear `direct` from `l` (a unit vector toward the
        /// light) on a surface facing it, as the coefficients Unity keeps (HemisphereProbe): the light
        /// is c0 + c1 y + c2 z + c3 x + c4 xy + c5 yz + c6 (3z² - 1) + c7 xz + c8 (x² - y²) at a
        /// normal (x, y, z). A directional light lights a surface max(0, n·l); its first three bands
        /// are 3/32 + t/2 + 15/32 t² at t = n·l, 17/16 facing it, so that is divided by 17/16 to make
        /// a surface facing the light take exactly `direct`. Edge on it takes 3/34, the far side 1/17.
        /// </summary>
        internal static SphericalHarmonicsL2 ActorLightProbe(Color ambient, Color direct, Vector3 l) {
            const float Facing = 17f / 16f;
            float x = l.x, y = l.y, z = l.z;
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++) {
                float d = direct[c] / Facing;
                sh[c, 0] = ambient[c] + d * 0.25f;
                sh[c, 1] = d * 0.5f * y;
                sh[c, 2] = d * 0.5f * z;
                sh[c, 3] = d * 0.5f * x;
                sh[c, 4] = d * (15f / 16f) * x * y;
                sh[c, 5] = d * (15f / 16f) * y * z;
                sh[c, 6] = d * (5f / 64f) * (3 * z * z - 1);
                sh[c, 7] = d * (15f / 16f) * x * z;
                sh[c, 8] = d * (15f / 64f) * (x * x - y * y);
            }
            return sh;
        }
    }
}
