using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelLighting.hlsl is a copy of URP's UniversalFragmentPBR with the ground's shade added. A
    /// copy drifts without a sound when URP changes the original, so this holds the original to the
    /// versions the copy was compared with. When it fails, compare URP's function (its path is in
    /// the message) with ModelFragmentPBR, carry any change across, and add the new hash here.
    /// </summary>
    public class ModelLightingCopyTests {
        const string Signature = "half4 UniversalFragmentPBR(InputData inputData, SurfaceData surfaceData)";

        // The MD5 of the function's lines, each ending in "\n", from its signature to its closing brace.
        static readonly Dictionary<string, string> Compared = new() {
            ["d198cd703a53ae10d973ae3ecdd2ed25"] = "URP 17.1 to 17.3 (Unity 6000.1 to 6000.3)",
            ["a94330fcba1e981a85a92c9c9bcdf935"] = "URP 17.5 (Unity 6000.5), which adds Meta Quest branches the copy leaves out",
            ["e9cb36c00f37a1c9e53fd8b09b69d472"] = "URP 17.6 (Unity 6000.6), whose Quest branch differs again",
        };

        [Test]
        public void UrpsUniversalFragmentPBR_IsAVersionTheCopyWasComparedWith() {
            var urp = PackageInfo.FindForPackageName("com.unity.render-pipelines.universal");
            Assert.That(urp, Is.Not.Null, "URP is not installed, so there is nothing to compare");
            var path = Path.Combine(urp.resolvedPath, "ShaderLibrary", "Lighting.hlsl");
            var lines = File.ReadAllText(path).Split('\n');
            int start = System.Array.IndexOf(lines, Signature);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{path} has no `{Signature}`: URP {urp.version} moved or renamed it. ModelLighting.hlsl's copy needs comparing with whatever replaced it.");
            int end = System.Array.IndexOf(lines, "}", start);
            var body = string.Concat(lines.Skip(start).Take(end - start + 1).Select(l => l + "\n"));
            using var md5 = MD5.Create();
            var hash = string.Concat(md5.ComputeHash(Encoding.UTF8.GetBytes(body)).Select(b => b.ToString("x2")));
            Assert.That(Compared.ContainsKey(hash), Is.True,
                $"URP {urp.version}'s UniversalFragmentPBR ({path}, line {start + 1}) is not one OneJS's copy in ModelLighting.hlsl was compared with (MD5 {hash}). Compare the two, carry the change into ModelFragmentPBR, and add the hash to this test.");
        }
    }
}
