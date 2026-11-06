using NUnit.Framework;

namespace OneJS.Tests {
    /// <summary>
    /// __eventAPI.listens says whether an element has a handler for any of the
    /// event types given: how onejs-play's input tells a press on a button
    /// from a press on the game behind it (overUI).
    /// </summary>
    [TestFixture]
    public class EventApiListensTests {
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
        public void Listens_AnswersForTheTypesGiven_AndStopsWhenTheHandlerGoes() {
            var answers = _ctx.Eval(@"(() => {
                const UIE = CS.UnityEngine.UIElements;
                const button = new UIE.VisualElement();
                const plain = new UIE.VisualElement();
                const press = () => {};
                __eventAPI.addEventListener(button, 'click', press);
                const seen = [
                    __eventAPI.listens(button, ['pointerdown', 'click']),
                    __eventAPI.listens(button, ['pointerdown']),
                    __eventAPI.listens(plain, ['click']),
                    __eventAPI.listens(null, ['click']),
                ];
                __eventAPI.removeEventListener(button, 'click', press);
                seen.push(__eventAPI.listens(button, ['click']));
                return seen.join(',');
            })()");
            Assert.AreEqual("true,false,false,false,false", answers);
        }
    }
}
