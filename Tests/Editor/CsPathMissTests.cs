using System;
using System.Linq;
using NUnit.Framework;

namespace OneJS.Tests.PathMiss.Shared.Deep {
    /// <summary>Under a namespace named as a type in OneJS.Tests (CsPathMissFixtures.cs).</summary>
    public static class Leaf {
        public static int V = 7;
    }
}

namespace OneJS.Tests.PathMiss {
    public static class Outer {
        public class Gen<T> {
            public string Of => typeof(T).Name;
            public static string Name() => typeof(T).Name;
        }

        public static class Inner {
            public static int V = 3;
        }

        public enum Kind { A = 1, B = 2 }
    }

    public class KnownBase {
        public static int Inherited = 5;
        public static string Hello() => "base";
    }

    public class Known : KnownBase {
        public static int Field = 1;
        public static int Prop => 2;
        public const int Const = 3;
        public static object Nothing = null;

        public static string Over(int a) => "int";
        public static string Over(string s) => "string";
        public static T Echo<T>(T x) => x;

        public static event Action Changed;
        public static void Fire() => Changed?.Invoke();
    }

    public interface IWithStatic {
        static int Answer => 42;
    }

    public class Generic<T> {
        public static int Count => 9;
        public static string Make() => typeof(T).Name;
    }

    /// <summary>
    /// What a CS path reads when its last name is not a member of the type
    /// before it. A path proxy stands for anything, so every missing name used
    /// to read as one, typeof "function", and only a call said otherwise
    /// ("Method not found"): a check that a member exists could never fail.
    /// A missing member of a known type now reads undefined, except where the
    /// path still leads somewhere: a nested generic reached by its dotted path,
    /// a namespace under a type of the same name, a nested type or enum.
    /// </summary>
    [TestFixture]
    public class CsPathMissTests {
        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() {
            _ctx = new QuickJSContext();
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
        }

        const string P = "CS.OneJS.Tests.PathMiss";

        string Eval(string js) => _ctx.Eval(js);

        // What must not break: each path below reaches its target only
        // through a name that is no member of the type before it.

        [Test]
        public void ANestedGeneric_ResolvesByItsDottedPath() {
            Assert.AreEqual("Int32", Eval($"new ({P}.Outer.Gen(CS.System.Int32))().Of"));
            Assert.AreEqual("Int32", Eval($"{P}.Outer.Gen(CS.System.Int32).Name()"));
        }

        [Test]
        public void ANamespaceUnderATypeOfTheSameName_StillLeadsToItsTypes() {
            Assert.AreEqual("shared", Eval($"{P}.Shared.Name"));
            Assert.AreEqual("7", Eval($"String({P}.Shared.Deep.Leaf.V)"));
        }

        [Test]
        public void ANestedTypeAndANestedEnum_ResolveByTheirDottedPaths() {
            Assert.AreEqual("3", Eval($"String({P}.Outer.Inner.V)"));
            Assert.AreEqual("2", Eval($"String({P}.Outer.Kind.B)"));
            Assert.AreEqual("number", Eval($"typeof {P}.Outer.Kind.A"));
        }

        // The change: a missing member of a known type reads undefined.

        [Test]
        public void AMissingStaticMember_OfAKnownType_ReadsUndefined() {
            foreach (var name in new[] { "NoSuchMethod", "NoSuchField", "NoSuchProperty", "NoSuchConst" }) {
                Assert.AreEqual("undefined", Eval($"typeof {P}.Known.{name}"), name);
            }
            Assert.AreEqual("skipped", Eval($"{P}.Known.NoSuchMethod?.() ?? 'skipped'"));
        }

        [Test]
        public void PresentMembers_ReadAsBefore() {
            Assert.AreEqual("1 2 3 null", Eval($"(() => {{ const K = {P}.Known; return `${{K.Field}} ${{K.Prop}} ${{K.Const}} ${{K.Nothing}}` }})()"));
            Assert.AreEqual("int string", Eval($"{P}.Known.Over(1) + ' ' + {P}.Known.Over('a')"));
            Assert.AreEqual("5 base", Eval($"{P}.Known.Inherited + ' ' + {P}.Known.Hello()"));
            Assert.AreEqual("function", Eval($"typeof {P}.Known.Echo"), "a generic method is still a name, though calling one is unsupported");
            Assert.AreEqual("42", Eval($"String({P}.IWithStatic.Answer)"));
            Assert.AreEqual("9 Int32", Eval($"{P}.Generic(CS.System.Int32).Count + ' ' + {P}.Generic(CS.System.Int32).Make()"));
        }

        [Test]
        public void AStaticEvent_StaysReachable() {
            Assert.AreNotEqual("undefined", Eval($"typeof {P}.Known.Changed"));
            Assert.AreEqual("1", Eval($@"(() => {{
                let n = 0
                const f = () => {{ n++ }}
                {P}.Known.add_Changed(f)
                {P}.Known.Fire()
                {P}.Known.remove_Changed(f)
                {P}.Known.Fire()
                return String(n)
            }})()"));
        }

        [Test]
        public void ARepeatedMiss_CrossesToCSharpOnce() {
            Eval($"{P}.Known.NoSuchRepeat");
            long before = QuickJSNative.InteropCallCount;
            Eval($"for (let i = 0; i < 100; i++) {P}.Known.NoSuchRepeat");
            Assert.AreEqual(0, QuickJSNative.InteropCallCount - before);
        }
    }

    /// <summary>
    /// Explicit: a benchmark, not a check. What a CS path read costs in a loop,
    /// for the member that is there and the one that is not. Read the
    /// "[PathMissBench]" lines.
    /// </summary>
    [TestFixture, Explicit("benchmark"), Category("Benchmark")]
    public class CsPathMissBenchmark {
        const int N = 20000;

        static string Measure(QuickJSContext ctx, string label, string read) {
            ctx.Eval($"for (let i = 0; i < 100; i++) {{ {read} }}");
            long calls = QuickJSNative.InteropCallCount;
            var ms = double.Parse(ctx.Eval($@"(() => {{
                const t = Date.now()
                for (let i = 0; i < {N}; i++) {{ {read} }}
                return String(Date.now() - t)
            }})()"), System.Globalization.CultureInfo.InvariantCulture);
            calls = QuickJSNative.InteropCallCount - calls;
            return $"[PathMissBench] {label}: {ms * 1000 / N:F2} us a read, {(double)calls / N:F2} crossings a read";
        }

        // What the first miss in a domain pays: the index of namespaces and
        // generic names LeadsToType answers from, built from every loaded type.
        static string IndexBuild() {
            var build = typeof(QuickJSNative).GetMethod("BuildTypePathPrefixes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            long before = GC.GetTotalMemory(false);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var names = (System.Collections.Generic.HashSet<string>)build.Invoke(null, new object[] { assemblies });
            sw.Stop();
            return $"[PathMissBench] index build: {sw.Elapsed.TotalMilliseconds:F0} ms over {assemblies.Length} assemblies, {names.Count} names, about {(GC.GetTotalMemory(false) - before) / 1024} KB";
        }

        [Test]
        public void PathReads() {
            var ctx = new QuickJSContext();
            try {
                ctx.Eval("globalThis.K = CS.OneJS.Tests.PathMiss.Known");
                var lines = new[] {
                    Measure(ctx, "hit, a static field", "CS.OneJS.Tests.PathMiss.Known.Field"),
                    Measure(ctx, "hit, through a held type", "K.Field"),
                    Measure(ctx, "miss", "CS.OneJS.Tests.PathMiss.Known.NoSuch"),
                    Measure(ctx, "miss, through a held type", "K.NoSuch"),
                };
                foreach (var line in lines.Append(IndexBuild())) {
                    UnityEngine.Debug.Log(line);
                    TestContext.WriteLine(line);
                }
            } finally {
                ctx.Dispose();
                QuickJSNative.ClearAllHandles();
            }
        }
    }
}
