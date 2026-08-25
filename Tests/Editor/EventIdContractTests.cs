using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Event type ids are hand-copied between QuickJSUIBridge.cs and the
    /// bootstrap. Unlike the painter opcodes there is no unknown-value branch to
    /// catch a mistake: __EVT_TYPE_NAMES maps every id to a name, so a shifted
    /// id does not fail, it delivers a pointerdown as a click.
    ///
    /// Both sides live in this package, so unlike the cross-package contracts
    /// this guard needs nothing outside it and runs in this package's own CI.
    /// </summary>
    public class EventIdContractTests {
        const string CsRel = "Runtime/QuickJSUIBridge.cs";
        const string JsRel = "Resources/OneJS/QuickJSBootstrap.js.txt";

        static string PackageRoot {
            get {
                // This file is at <package>/Tests/Editor/, so the package root is
                // two levels up. Derived rather than hardcoded so the guard
                // survives the package being installed at a different path.
                var here = Directory.GetFiles(Application.dataPath, "EventIdContractTests.cs",
                    SearchOption.AllDirectories).FirstOrDefault();
                Assert.IsNotNull(here, "Could not locate this test file to derive the package root.");
                return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", ".."));
            }
        }

        static string Read(string rel) {
            var full = Path.Combine(PackageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(full), $"Contract source missing: {rel}");
            return File.ReadAllText(full);
        }

        [Test]
        public void EventTypeIds_AgreeBetweenTheBridgeAndTheBootstrap() {
            var cs = new Dictionary<string, int>();
            foreach (Match m in Regex.Matches(Read(CsRel), @"const\s+int\s+EVT_([A-Z0-9_]+)\s*=\s*(\d+)\s*;"))
                cs[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);

            var js = new Dictionary<string, int>();
            foreach (Match m in Regex.Matches(Read(JsRel), @"const\s+__EVT_([A-Z0-9_]+)\s*=\s*(\d+)\s*;"))
                js[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);

            Assert.Greater(cs.Count, 0, "Parsed no EVT_ constants from the bridge; the guard would pass vacuously.");
            Assert.Greater(js.Count, 0, "Parsed no __EVT_ constants from the bootstrap; the guard would pass vacuously.");

            CollectionAssert.AreEquivalent(cs.Keys, js.Keys,
                "Event id sets differ.\n" +
                $"  only in C#: {string.Join(", ", cs.Keys.Except(js.Keys))}\n" +
                $"  only in JS: {string.Join(", ", js.Keys.Except(cs.Keys))}");

            foreach (var kv in cs)
                Assert.AreEqual(kv.Value, js[kv.Key],
                    $"EVT_{kv.Key} is {kv.Value} in the bridge and {js[kv.Key]} in the bootstrap. " +
                    "Nothing rejects an unexpected id, so this misdelivers events rather than failing.");
        }

        /// <summary>
        /// Two ids sharing a number would make one event arrive as the other,
        /// which the set comparison above cannot see because both sides would
        /// be equally wrong.
        /// </summary>
        [Test]
        public void EventTypeIds_AreUnique() {
            var cs = new List<KeyValuePair<string, int>>();
            foreach (Match m in Regex.Matches(Read(CsRel), @"const\s+int\s+EVT_([A-Z0-9_]+)\s*=\s*(\d+)\s*;"))
                cs.Add(new KeyValuePair<string, int>(m.Groups[1].Value, int.Parse(m.Groups[2].Value)));

            Assert.Greater(cs.Count, 0, "Parsed no EVT_ constants; the guard would pass vacuously.");

            var dupes = cs.GroupBy(p => p.Value).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} = {string.Join(" and ", g.Select(p => "EVT_" + p.Key))}").ToList();
            Assert.IsEmpty(dupes, "Event ids collide: " + string.Join("; ", dupes));
        }
    }
}
