using NUnit.Framework;

namespace OneJS.Tests {
    /// <summary>A constructor whose trailing parameters have C# defaults.</summary>
    public class DefaultParamCtorFixture {
        public readonly string Name;
        public readonly int Count;
        public readonly string Tag;

        public DefaultParamCtorFixture(string name, int count = 3, string tag = "default") {
            Name = name;
            Count = count;
            Tag = tag;
        }
    }

    /// <summary>
    /// An exact match declared after a constructor that also fits a one-argument call
    /// through its default, so a matcher taking the first fit would pick the wrong one.
    /// </summary>
    public class ShadowedCtorFixture {
        public readonly string Picked;

        public ShadowedCtorFixture(string a, int b = 5) {
            Picked = $"defaults b={b}";
        }

        public ShadowedCtorFixture(string a) {
            Picked = "exact";
        }
    }

    /// <summary>
    /// `new` from JS binding to a constructor with omitted trailing optionals, by the
    /// same rule methods follow (#146): an exact arity match first, then a constructor
    /// whose omitted parameters all declare defaults, which take their declared values.
    /// </summary>
    [TestFixture]
    public class QuickJSConstructorDispatchTests {
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

        string Construct(string args) => _ctx.Eval($@"
            (() => {{
                const f = new CS.OneJS.Tests.DefaultParamCtorFixture({args})
                return `${{f.Name}} ${{f.Count}} ${{f.Tag}}`
            }})()");

        [Test]
        public void OmittedTrailingArguments_TakeTheDeclaredDefaults() {
            Assert.AreEqual("a 3 default", Construct("'a'"));
            Assert.AreEqual("a 7 default", Construct("'a', 7"));
            Assert.AreEqual("a 7 x", Construct("'a', 7, 'x'"));
        }

        [Test]
        public void AnExactMatch_WinsOverAConstructorThatFitsThroughDefaults() {
            Assert.AreEqual("exact", _ctx.Eval("new CS.OneJS.Tests.ShadowedCtorFixture('x').Picked"));
            Assert.AreEqual("defaults b=2", _ctx.Eval("new CS.OneJS.Tests.ShadowedCtorFixture('x', 2).Picked"));
        }

        [Test]
        public void AMissingRequiredArgument_StillFindsNoConstructor() {
            var result = _ctx.Eval(@"
                (() => {
                    try { new CS.OneJS.Tests.DefaultParamCtorFixture(); return 'constructed' }
                    catch (e) { return String(e && e.message || e) }
                })()");
            StringAssert.Contains("No matching ctor", result);
        }
    }
}
