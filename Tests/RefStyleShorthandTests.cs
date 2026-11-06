using NUnit.Framework;

namespace OneJS.Tests {
    /// <summary>
    /// A style reached through a ref takes the shorthands a JSX style does:
    /// el.style.borderRadius = 6 sets all four corners. It threw "Property or
    /// field not found (set): InlineStyleAccess.borderRadius" (learn Mac).
    /// </summary>
    [TestFixture]
    public class RefStyleShorthandTests {
        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() {
            _ctx = new QuickJSContext();
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
        }

        [Test]
        public void Shorthands_SetEveryLonghand_AndLonghandsStillWork() {
            var read = _ctx.Eval(@"(() => {
                const el = new CS.UnityEngine.UIElements.VisualElement();
                el.style.borderRadius = 6;
                el.style.padding = 3;
                el.style.margin = 2;
                el.style.borderWidth = 1;
                el.style.borderTopLeftRadius = 9;
                const px = (s) => s.value;
                return [
                    px(el.style.borderTopLeftRadius), px(el.style.borderTopRightRadius),
                    px(el.style.borderBottomRightRadius), px(el.style.borderBottomLeftRadius),
                    px(el.style.paddingTop), px(el.style.paddingLeft),
                    px(el.style.marginBottom), el.style.borderRightWidth.value,
                ].join(',');
            })()");
            Assert.AreEqual("9,6,6,6,3,3,2,1", read);
        }
    }
}
