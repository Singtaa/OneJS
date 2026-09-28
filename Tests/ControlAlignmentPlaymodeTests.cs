using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OneJS.CustomStyleSheets;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OneJS.Tests {
    /// <summary>
    /// A control in an `align-items: center` row lines up with its neighbours
    /// only if the part a person sees (a slider's track and thumb, a toggle's
    /// box, a text field's input) is centred in the control's margin box,
    /// because that box is what the row centres.
    ///
    /// A theme that positions a part with a fixed pixel offset gets this right
    /// for one height and wrong for every other, and nothing looks broken until
    /// the control sits beside a label. Both of OneJS's themes did: Unity's
    /// default places a slider's tracker and dragger at `top: 50%`, and each
    /// theme then gave them margins that assumed `top: 0`. So every control is
    /// measured at its natural height and at a taller one, from resolved layout
    /// (worldBound) rather than from pixels. A slider's tracker is measured
    /// again resized, because a rule that centres it by a margin matching its
    /// height breaks the day somebody changes the height. The dragger cannot be
    /// centred any other way (BaseSlider owns its translate), so its margin is
    /// pinned to the theme's height and the natural-size pass is its guard.
    ///
    /// Subclasses choose the stylesheets; the checks are the same for each.
    /// </summary>
    public abstract class ControlAlignmentFixture {
        /// <summary>Extra USS compiled onto the root, over the theme, the way a game's own sheets are.</summary>
        protected virtual string ExtraUss => null;

        /// <summary>The heights each control is measured at: its own, then a forced taller one.</summary>
        static readonly float?[] Heights = { null, 40f };

        const float Tolerance = 1f;

        GameObject _go;
        PanelSettings _panelSettings;
        StyleSheet _extra;
        VisualElement _root;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = OneJSRuntimeTheme();
            _go = new GameObject("ControlAlignmentHost");
            var doc = _go.AddComponent<UIDocument>();
            doc.panelSettings = _panelSettings;
            yield return null;

            _root = doc.rootVisualElement;
            if (ExtraUss != null) {
                _extra = ScriptableObject.CreateInstance<StyleSheet>();
                new UssCompiler().Compile(_extra, ExtraUss);
                _root.styleSheets.Add(_extra);
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            Object.Destroy(_go);
            Object.Destroy(_panelSettings);
            if (_extra != null) Object.Destroy(_extra);
            yield return null;
        }

        /// <summary>The theme JSRunner applies, and so the one every OneJS panel, Play's included, starts from.</summary>
        static ThemeStyleSheet OneJSRuntimeTheme() {
#if UNITY_EDITOR
            foreach (var guid in AssetDatabase.FindAssets("UnityDefaultRuntimeTheme t:ThemeStyleSheet")) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Runtime/Styling/UnityThemes/UnityDefaultRuntimeTheme.tss")) {
                    return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
                }
            }
#endif
            Assert.Ignore("OneJS's runtime theme is only found through the AssetDatabase, in the editor");
            return null;
        }

        [UnityTest]
        public IEnumerator Slider_TrackAndThumb_AreCentred() {
            Dictionary<string, VisualElement> Parts(Slider s) => new Dictionary<string, VisualElement> {
                ["tracker"] = s.Q(className: BaseSlider<float>.trackerUssClassName),
                ["dragger"] = s.Q(className: BaseSlider<float>.draggerUssClassName),
            };
            yield return Check(() => new Slider(0, 1) { value = 0.5f }, Parts);
            // Inline, so it beats every sheet, as a game restyling it would.
            yield return Check(() => {
                var s = new Slider(0, 1) { value = 0.5f };
                s.Q(className: BaseSlider<float>.trackerUssClassName).style.height = 12;
                return s;
            }, s => new Dictionary<string, VisualElement> {
                ["tracker"] = s.Q(className: BaseSlider<float>.trackerUssClassName),
            }, "with a 12px tracker");
        }

        [UnityTest]
        public IEnumerator Toggle_Checkmark_IsCentred() {
            yield return Check(() => new Toggle { text = "toggle" }, t => new Dictionary<string, VisualElement> {
                ["checkmark"] = t.Q(className: Toggle.checkmarkUssClassName),
            });
        }

        [UnityTest]
        public IEnumerator TextField_Input_IsCentred() {
            yield return Check(() => new TextField { value = "text" }, f => new Dictionary<string, VisualElement> {
                ["input"] = f.Q(className: TextField.inputUssClassName),
            });
        }

        [UnityTest]
        public IEnumerator Button_Box_IsCentred() {
            // A button's visible part is its own border box, so what can go
            // wrong is an uneven margin moving it off the centre the row uses.
            yield return Check(() => new Button { text = "button" }, b => new Dictionary<string, VisualElement> {
                ["box"] = b,
            });
        }

        [UnityTest]
        public IEnumerator Scroller_Dragger_StaysInItsTrack() {
            // A scroller is a slider inside, so a rule centring slider parts
            // reaches its dragger too unless the scroller rules reset it.
            var view = new ScrollView(ScrollViewMode.VerticalAndHorizontal) {
                horizontalScrollerVisibility = ScrollerVisibility.AlwaysVisible,
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible,
            };
            view.style.width = 200;
            view.style.height = 200;
            var content = new VisualElement();
            content.style.width = 600;
            content.style.height = 600;
            view.Add(content);
            _root.Add(view);
            yield return null;
            yield return null;
            if (!(view.worldBound.height > 0)) {
                _root.Remove(view);
                Assert.Ignore("ScrollView has no layout: this runner does not lay panels out");
            }

            var failures = new List<string>();
            void Inside(Scroller scroller, bool horizontal) {
                var track = scroller.slider.worldBound;
                var dragger = scroller.slider.Q(className: BaseSlider<float>.draggerUssClassName).worldBound;
                float lo = horizontal ? dragger.yMin - track.yMin : dragger.xMin - track.xMin;
                float hi = horizontal ? track.yMax - dragger.yMax : track.xMax - dragger.xMax;
                if (lo < -Tolerance || hi < -Tolerance) {
                    failures.Add($"{(horizontal ? "horizontal" : "vertical")} dragger overhangs its track by {Mathf.Max(-lo, -hi):0.#}px");
                }
            }
            Inside(view.horizontalScroller, true);
            Inside(view.verticalScroller, false);
            _root.Remove(view);
            Assert.IsEmpty(failures, string.Join("; ", failures));
        }

        /// <summary>
        /// Puts the control in a centred row at each height and asserts every
        /// visible part's centre is within <see cref="Tolerance"/> of the
        /// control's margin box centre. All offsets are collected before
        /// asserting, so one failure reports every height and part.
        /// </summary>
        IEnumerator Check<T>(System.Func<T> make, System.Func<T, Dictionary<string, VisualElement>> parts,
            string variant = null) where T : VisualElement {
            var failures = new List<string>();
            foreach (var height in Heights) {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.width = 300;
                row.Add(new Label("label"));
                var control = make();
                control.style.flexGrow = 1;
                if (height.HasValue) control.style.height = height.Value;
                row.Add(control);
                _root.Add(row);
                yield return null;
                yield return null;

                var b = control.worldBound;
                // A headless player lays nothing out, and every centre is then
                // zero, which would pass without having measured anything.
                if (!(b.height > 0)) {
                    _root.Remove(row);
                    Assert.Ignore($"{typeof(T).Name} has no layout ({b.height}px box): this runner does not lay panels out");
                }
                var r = control.resolvedStyle;
                float centre = b.y - r.marginTop + (b.height + r.marginTop + r.marginBottom) / 2f;
                foreach (var kv in parts(control)) {
                    Assert.IsNotNull(kv.Value, $"{typeof(T).Name} has no {kv.Key}");
                    float offset = kv.Value.worldBound.center.y - centre;
                    if (Mathf.Abs(offset) > Tolerance) {
                        failures.Add($"{kv.Key} at height {(height.HasValue ? height.Value + "px" : "auto")} " +
                            $"({b.height:0.#}px box): {offset:+0.#;-0.#}px off centre");
                    }
                }
                _root.Remove(row);
            }
            Assert.IsEmpty(failures, $"{typeof(T).Name}{(variant == null ? "" : " " + variant)}: " + string.Join("; ", failures));
        }
    }

    /// <summary>OneJS's runtime theme on its own, as any OneJS project gets it.</summary>
    public class OneJSThemeControlAlignmentTests : ControlAlignmentFixture { }
}
