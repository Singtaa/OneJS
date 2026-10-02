using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// onejs-react sends lengths, colours and enums to StyleBridge as plain data
    /// ({ value, unit }, { keyword }, { r, g, b, a }, a member name) instead of
    /// building each C# struct with its own crossing. Every key it sends that way
    /// is applied here from JS and read back from IStyle, so a key StyleBridge
    /// cannot read in that shape fails by name.
    /// </summary>
    [TestFixture]
    public class StyleBridgePlainDataTests {
        static readonly string[] LengthKeys = {
            "width", "height", "minWidth", "minHeight", "maxWidth", "maxHeight",
            "top", "right", "bottom", "left",
            "marginTop", "marginRight", "marginBottom", "marginLeft",
            "paddingTop", "paddingRight", "paddingBottom", "paddingLeft",
            "flexBasis",
            "borderTopLeftRadius", "borderTopRightRadius", "borderBottomLeftRadius", "borderBottomRightRadius",
            "fontSize", "letterSpacing", "wordSpacing", "unityParagraphSpacing",
        };

        // Keys that take a keyword in USS: the { keyword } shape is sent for these
        static readonly string[] KeywordKeys = {
            "width", "height", "minWidth", "minHeight", "maxWidth", "maxHeight",
            "top", "right", "bottom", "left",
            "marginTop", "marginRight", "marginBottom", "marginLeft", "flexBasis",
        };

        static readonly string[] ColorKeys = {
            "color", "backgroundColor",
            "borderTopColor", "borderRightColor", "borderBottomColor", "borderLeftColor",
            "unityTextOutlineColor", "unityBackgroundImageTintColor",
        };

        static readonly (string key, string member)[] EnumKeys = {
            ("flexDirection", "RowReverse"), ("flexWrap", "Wrap"),
            ("alignItems", "Center"), ("alignSelf", "FlexEnd"), ("alignContent", "Stretch"),
            ("justifyContent", "SpaceBetween"), ("position", "Absolute"), ("overflow", "Hidden"),
            ("display", "None"), ("visibility", "Hidden"), ("whiteSpace", "NoWrap"),
            ("textOverflow", "Ellipsis"), ("unityFontStyleAndWeight", "Bold"),
            ("unityTextOverflowPosition", "Middle"), ("unityOverflowClipBox", "ContentBox"),
        };

        QuickJSContext _ctx;
        VisualElement _element;

        [SetUp]
        public void SetUp() {
            _ctx = new QuickJSContext();
            _element = new VisualElement();
            var handle = QuickJSNative.RegisterObject(_element);
            _ctx.Eval($"globalThis.el = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {handle})");
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
        }

        void Apply(string key, string valueJs) {
            _ctx.Eval($"CS.OneJS.StyleBridge.ApplyStyles(el, {{ {key}: {valueJs} }})");
        }

        static object Read(IStyle style, string key) {
            var prop = typeof(IStyle).GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(prop, $"IStyle has no '{key}'");
            return prop.GetValue(style);
        }

        [Test]
        public void EveryLengthKey_TakesValueAndUnit() {
            foreach (var key in LengthKeys) {
                Apply(key, "{ value: 12, unit: CS.UnityEngine.UIElements.LengthUnit.Pixel }");
                var length = (StyleLength)Read(_element.style, key);
                Assert.AreEqual(12f, length.value.value, key);
                Assert.AreEqual(LengthUnit.Pixel, length.value.unit, key);

                Apply(key, "{ value: 40, unit: CS.UnityEngine.UIElements.LengthUnit.Percent }");
                length = (StyleLength)Read(_element.style, key);
                Assert.AreEqual(40f, length.value.value, key + " percent");
                Assert.AreEqual(LengthUnit.Percent, length.value.unit, key + " percent");
            }
        }

        [Test]
        public void EveryKeywordKey_TakesAKeyword() {
            foreach (var key in KeywordKeys) {
                Apply(key, "{ value: 12, unit: 0 }");
                Apply(key, "{ keyword: CS.UnityEngine.UIElements.StyleKeyword.Auto }");
                var length = (StyleLength)Read(_element.style, key);
                Assert.AreEqual(StyleKeyword.Auto, length.keyword, key);
            }
            Apply("width", "{ keyword: CS.UnityEngine.UIElements.StyleKeyword.Initial }");
            Assert.AreEqual(StyleKeyword.Initial, _element.style.width.keyword);
        }

        [Test]
        public void EveryColorKey_TakesRGBA() {
            foreach (var key in ColorKeys) {
                Apply(key, "{ r: 0.25, g: 0.5, b: 0.75, a: 0.5 }");
                var color = (StyleColor)Read(_element.style, key);
                Assert.AreEqual(new Color(0.25f, 0.5f, 0.75f, 0.5f), color.value, key);
            }
        }

        [Test]
        public void EveryEnumKey_TakesTheMemberName() {
            foreach (var (key, member) in EnumKeys) {
                Apply(key, $"'{member}'");
                var styleEnum = Read(_element.style, key);
                var value = styleEnum.GetType().GetProperty("value").GetValue(styleEnum);
                Assert.AreEqual(member, value.ToString(), key);
            }
        }

        [Test]
        public void ManyKeysInOneBatch_AllApply() {
            _ctx.Eval(@"CS.OneJS.StyleBridge.ApplyStyles(el, {
                width: { value: 30, unit: 0 }, color: { r: 1, g: 0, b: 0, a: 1 },
                flexDirection: 'Row', opacity: 0.5 })");
            Assert.AreEqual(30f, _element.style.width.value.value);
            Assert.AreEqual(Color.red, _element.style.color.value);
            Assert.AreEqual(FlexDirection.Row, _element.style.flexDirection.value);
            Assert.AreEqual(0.5f, _element.style.opacity.value);
        }
    }
}
