using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OneJS.Editor.TypeGenerator.Tests {
    /// <summary>A component no typings declare, so its declaration has to fall back.</summary>
    public class GlobalsTypingsProbe : MonoBehaviour { }

    [TestFixture]
    public class GlobalsTypingsTests {
        readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown() {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        T Make<T>(T o) where T : Object { _made.Add(o); return o; }

        static GlobalEntry Entry(string key, Object value) => new GlobalEntry { key = key, value = value };

        static readonly HashSet<string> Engine = new HashSet<string> {
            "UnityEngine.Object", "UnityEngine.GameObject", "UnityEngine.Component",
            "UnityEngine.Behaviour", "UnityEngine.MonoBehaviour",
        };

        [Test]
        public void An_entry_is_declared_as_its_objects_type() {
            var ghost = Make(new GameObject("ghost"));
            var text = GlobalsTypings.Generate(new[] { Entry("ghostPrefab", ghost) }, Engine);
            StringAssert.Contains("declare const ghostPrefab: CS.UnityEngine.GameObject\n", text);
        }

        [Test]
        public void A_type_the_typings_lack_falls_back_to_its_nearest_declared_base_and_says_so() {
            var probe = Make(new GameObject("probe")).AddComponent<GlobalsTypingsProbe>();
            var text = GlobalsTypings.Generate(new[] { Entry("spawner", probe) }, Engine);
            StringAssert.Contains("declare const spawner: CS.UnityEngine.MonoBehaviour // a OneJS.Editor.TypeGenerator.Tests.GlobalsTypingsProbe", text);
        }

        [Test]
        public void With_no_typings_at_all_it_ends_at_UnityEngine_Object_and_never_any() {
            var ghost = Make(new GameObject("ghost"));
            var text = GlobalsTypings.Generate(new[] { Entry("ghostPrefab", ghost) }, new HashSet<string>());
            StringAssert.Contains("declare const ghostPrefab: CS.UnityEngine.Object", text);
            StringAssert.DoesNotContain("any", text);
        }

        [Test]
        public void A_removed_entry_is_dropped_and_an_empty_one_skipped() {
            var a = Make(new GameObject("a"));
            var b = Make(new GameObject("b"));
            var both = GlobalsTypings.Generate(new[] { Entry("first", a), Entry("second", b) }, Engine);
            var one = GlobalsTypings.Generate(new[] { Entry("first", a), Entry("unset", null), Entry("", b) }, Engine);
            StringAssert.Contains("declare const second:", both);
            StringAssert.DoesNotContain("second", one);
            StringAssert.DoesNotContain("unset", one);
        }

        [Test]
        public void A_later_entry_with_the_same_key_wins_as_it_does_at_runtime() {
            var go = Make(new GameObject("go"));
            var probe = go.AddComponent<GlobalsTypingsProbe>();
            var text = GlobalsTypings.Generate(new[] { Entry("thing", go), Entry("thing", probe) }, Engine);
            StringAssert.Contains("declare const thing: CS.UnityEngine.MonoBehaviour", text);
            Assert.AreEqual(1, text.Split("declare const thing").Length - 1);
        }

        [Test]
        public void A_key_that_cannot_be_declared_says_how_to_read_it() {
            var go = Make(new GameObject("go"));
            var text = GlobalsTypings.Generate(new[] { Entry("my-thing", go), Entry("class", go) }, Engine);
            StringAssert.Contains("// \"my-thing\" is not a name a declaration can have, so read it as globalThis[\"my-thing\"].", text);
            StringAssert.DoesNotContain("declare const class", text);
        }

        [Test]
        public void An_empty_list_declares_nothing_and_removes_only_the_file_it_wrote() {
            Assert.IsNull(GlobalsTypings.Generate(new GlobalEntry[0], Engine));
            var dir = Path.Combine(Path.GetTempPath(), "onejs-globals-typings-" + System.Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, GlobalsTypings.FileName);
            try {
                var ghost = Make(new GameObject("ghost"));
                Assert.IsTrue(GlobalsTypings.WriteIfChanged(path, GlobalsTypings.Generate(new[] { Entry("ghostPrefab", ghost) }, Engine)));
                Assert.IsTrue(GlobalsTypings.WriteIfChanged(path, null));
                Assert.IsFalse(File.Exists(path));
                File.WriteAllText(path, "declare const mine: number\n");
                Assert.IsFalse(GlobalsTypings.WriteIfChanged(path, null));
                Assert.IsTrue(File.Exists(path));
            } finally {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void The_file_is_not_rewritten_when_its_content_is_the_same() {
            var dir = Path.Combine(Path.GetTempPath(), "onejs-globals-typings-" + System.Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, "types", GlobalsTypings.FileName);
            try {
                Assert.IsTrue(GlobalsTypings.WriteIfChanged(path, "a"));
                var stamp = new System.DateTime(2001, 1, 1);
                File.SetLastWriteTimeUtc(path, stamp);
                Assert.IsFalse(GlobalsTypings.WriteIfChanged(path, "a"));
                Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(path));
                Assert.IsTrue(GlobalsTypings.WriteIfChanged(path, "b"));
                Assert.AreEqual("b", File.ReadAllText(path));
            } finally {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Reads_the_types_a_generated_dts_declares_under_CS_and_nothing_inside_their_bodies() {
            const string dts =
                "declare namespace CS {\n" +
                "    namespace UnityEngine {\n" +
                "        class GameObject extends UnityEngine.Object {\n" +
                "            GetComponent($type: System.Type): { inner: number };\n" +
                "            class NotAType\n" +
                "        }\n" +
                "        interface ISomething {\n" +
                "        }\n" +
                "    }\n" +
                "    namespace MyGame.Ghosts {\n" +
                "        abstract class Spawner extends UnityEngine.MonoBehaviour {\n" +
                "        }\n" +
                "    }\n" +
                "}\n" +
                "declare namespace Other {\n" +
                "    class Elsewhere {\n" +
                "    }\n" +
                "}\n";
            var found = new HashSet<string>();
            GlobalsTypings.ReadDeclared(dts, found);
            CollectionAssert.AreEquivalent(new[] { "UnityEngine.GameObject", "UnityEngine.ISomething", "MyGame.Ghosts.Spawner" }, found);
        }
    }
}
