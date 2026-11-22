using NUnit.Framework;

namespace OneJS.Tests.BoundGeneric {
    public class Box<T> {
        public static int Count => 9;
        public static int Field = 4;
        public const int Const = 3;
        public static object Nothing = null;
        public static string Name => typeof(T).Name;
        public static int lower = 2;

        public static string Make() => "made " + typeof(T).Name;
        public static string Over(int a) => "int";
        public static string Over(string s) => "string";
    }

    /// <summary>
    /// The static members of a constructed generic type, CS.X.Box(Int32). Its
    /// proxy took any name starting in uppercase for a method unless it was on
    /// a short list of Unity's (time, width, ...), so Box(Int32).Count read as a
    /// function and a property of a generic type could not be read at all.
    /// </summary>
    [TestFixture]
    public class BoundGenericStaticTests {
        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() {
            _ctx = new QuickJSContext();
            _ctx.Eval("globalThis.B = CS.OneJS.Tests.BoundGeneric.Box(CS.System.Int32)");
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
        }

        [Test]
        public void AStaticPropertyFieldOrConst_ReadsItsValue() {
            Assert.AreEqual("9 4 3 null", _ctx.Eval("`${B.Count} ${B.Field} ${B.Const} ${B.Nothing}`"));
            Assert.AreEqual("number", _ctx.Eval("typeof B.Count"));
        }

        [Test]
        public void AStaticProperty_ReadsForItsTypeArgument() {
            Assert.AreEqual("Int32 String", _ctx.Eval("B.Name + ' ' + CS.OneJS.Tests.BoundGeneric.Box(CS.System.String).Name"));
        }

        [Test]
        public void StaticMethods_StayCallable() {
            Assert.AreEqual("made Int32", _ctx.Eval("B.Make()"));
            Assert.AreEqual("int string", _ctx.Eval("B.Over(1) + ' ' + B.Over('a')"));
            Assert.AreEqual("made Int32", _ctx.Eval("B.Make()"), "the second call, once the name is known to be a method");
        }

        [Test]
        public void ALowercaseStatic_ReadsAsBefore() {
            Assert.AreEqual("2", _ctx.Eval("String(B.lower)"));
        }

        [Test]
        public void AMethodName_CrossesOnceToBeKnown() {
            _ctx.Eval("B.Make");
            long before = QuickJSNative.InteropCallCount;
            _ctx.Eval("for (let i = 0; i < 10; i++) B.Make");
            Assert.AreEqual(0, QuickJSNative.InteropCallCount - before);
        }
    }
}
