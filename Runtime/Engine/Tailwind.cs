using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OneJS;
using OneJS.Dom;
using OneJS.Utils;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

[RequireComponent(typeof(ScriptEngine), typeof(UIDocument))]
public class Tailwind : MonoBehaviour, IClassStrProcessor {
    [FormerlySerializedAs("_styleSheets")]
    [Tooltip("Default Tailwind stylesheets.")]
    [SerializeField] StyleSheet[] _baseStyleSheets;

    [Tooltip("Tailwind stylesheets for the various breakpoints")]
    [SerializeField] StyleSheet[] _breakpointStyleSheets;

    [Tooltip("Watch for screen size changes even for Standalone builds.")]
    [SerializeField] bool _pollStandaloneScreen;

    ScriptEngine _scriptEngine;
    UIDocument _uiDocument;
    Regex _regex = new Regex("^([\\w-]+?)-\\[(.+?)\\]");
    Dictionary<string, Action<string[], Dom>> _funcs;

    float _lastScreenWidth;

    void Awake() {
        SetupFuncs();
        _uiDocument = GetComponent<UIDocument>();
        _baseStyleSheets.ToList().ForEach(s => _uiDocument.rootVisualElement.styleSheets.Add(s));
        _scriptEngine = GetComponent<ScriptEngine>();
        _scriptEngine.RegisterClassStrProcessor(this);
    }

    void Start() {
        CoroutineUtil.Start(
            CoroutineUtil.DelayFrames(() => {
                var width = _uiDocument.rootVisualElement.resolvedStyle.width;
                SetBreakpointStyleSheet(width);
                _lastScreenWidth = width;
            }, 1)
        );
    }

    void Update() {
#if UNITY_EDITOR
        PollScreenChange();
#else
        if (_pollStandaloneScreen) {
            PollScreenChange();
        }
#endif
    }

    void PollScreenChange() {
        var width = _uiDocument.rootVisualElement.resolvedStyle.width;
        if (!Mathf.Approximately(_lastScreenWidth, width)) {
            SetBreakpointStyleSheet(width);
            _lastScreenWidth = width;
        }
    }

    public string ProcessClassStr(string classStr, Dom dom) {
        var names = classStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        names = names.Select(s => s[0] >= 48 && s[0] <= 57 ? "_" + s : s).ToArray(); // ^\d => _\d
        var res = new List<string>();

        // Expand Chained Tailwind Classes
        foreach (var name in names) {
            var tokens = name.Split(":");
            if (tokens.Length == 1) {
                res.Add(name);
                continue;
            }
            var last = tokens[tokens.Length - 1];
            for (int i = 0; i < tokens.Length - 1; i++) {
                res.Add($"{tokens[i]}:{last}");
            }
        }

        names = res.ToArray();
        res.Clear();

        // Process Tailwind-specific funcs
        foreach (var name in names) {
            var match = _regex.Match(name);
            if (match.Success) {
                var funcName = match.Groups[1].Value;
                var funcParams = match.Groups[2].Value.Split('_', StringSplitOptions.RemoveEmptyEntries);
                if (_funcs.ContainsKey(funcName)) {
                    _funcs[funcName](funcParams, dom);
                    continue;
                }
            }
            res.Add(name);
        }

        return String.Join(" ", res).Replace(".", "dot").Replace("/", "slash").Replace(":", "_c_");
    }

    void SetBreakpointStyleSheet(float width) {
        var sheetSet = _uiDocument.rootVisualElement.styleSheets;
        foreach (var sheet in _breakpointStyleSheets) {
            if (sheetSet.Contains(sheet))
                sheetSet.Remove(sheet);
        }
        for (int i = 0; i < Mathf.Min(_scriptEngine.Breakpoints.Length, _breakpointStyleSheets.Length); i++) {
            var bp = _scriptEngine.Breakpoints[i];
            var sheet = _breakpointStyleSheets[i];
            if (width >= bp) {
                sheetSet.Add(sheet);
            }
        }
    }

    void SetupFuncs() {
        _funcs = new Dictionary<string, Action<string[], Dom>>() {
            {
                "p", (vals, dom) => {
                    if (vals.Length == 1) {
                        dom.ve.style.paddingTop = ParseLength(vals[0]);
                        dom.ve.style.paddingBottom = ParseLength(vals[0]);
                        dom.ve.style.paddingLeft = ParseLength(vals[0]);
                        dom.ve.style.paddingRight = ParseLength(vals[0]);
                    } else if (vals.Length == 2) {
                        dom.ve.style.paddingTop = ParseLength(vals[0]);
                        dom.ve.style.paddingBottom = ParseLength(vals[0]);
                        dom.ve.style.paddingLeft = ParseLength(vals[1]);
                        dom.ve.style.paddingRight = ParseLength(vals[1]);
                    } else if (vals.Length == 3) {
                        dom.ve.style.paddingTop = ParseLength(vals[0]);
                        dom.ve.style.paddingBottom = ParseLength(vals[2]);
                        dom.ve.style.paddingLeft = ParseLength(vals[1]);
                        dom.ve.style.paddingRight = ParseLength(vals[1]);
                    } else if (vals.Length == 4) {
                        dom.ve.style.paddingTop = ParseLength(vals[0]);
                        dom.ve.style.paddingBottom = ParseLength(vals[2]);
                        dom.ve.style.paddingLeft = ParseLength(vals[4]);
                        dom.ve.style.paddingRight = ParseLength(vals[1]);
                    }
                }
            }, {
                "pt", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.paddingTop = ParseLength(vals[0]);
                }
            }, {
                "pb", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.paddingBottom = ParseLength(vals[0]);
                }
            }, {
                "pl", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.paddingLeft = ParseLength(vals[0]);
                }
            }, {
                "pr", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.paddingRight = ParseLength(vals[0]);
                }
            }, {
                "px", (vals, dom) => {
                    if (vals.Length == 1) {
                        var v = ParseLength(vals[0]);
                        dom.ve.style.paddingLeft = v;
                        dom.ve.style.paddingRight = v;
                    }
                }
            }, {
                "py", (vals, dom) => {
                    if (vals.Length == 1) {
                        var v = ParseLength(vals[0]);
                        dom.ve.style.paddingTop = v;
                        dom.ve.style.paddingBottom = v;
                    }
                }
            }, {
                "m", (vals, dom) => {
                    if (vals.Length == 1) {
                        dom.ve.style.marginTop = ParseLength(vals[0]);
                        dom.ve.style.marginBottom = ParseLength(vals[0]);
                        dom.ve.style.marginLeft = ParseLength(vals[0]);
                        dom.ve.style.marginRight = ParseLength(vals[0]);
                    } else if (vals.Length == 2) {
                        dom.ve.style.marginTop = ParseLength(vals[0]);
                        dom.ve.style.marginBottom = ParseLength(vals[0]);
                        dom.ve.style.marginLeft = ParseLength(vals[1]);
                        dom.ve.style.marginRight = ParseLength(vals[1]);
                    } else if (vals.Length == 3) {
                        dom.ve.style.marginTop = ParseLength(vals[0]);
                        dom.ve.style.marginBottom = ParseLength(vals[2]);
                        dom.ve.style.marginLeft = ParseLength(vals[1]);
                        dom.ve.style.marginRight = ParseLength(vals[1]);
                    } else if (vals.Length == 4) {
                        dom.ve.style.marginTop = ParseLength(vals[0]);
                        dom.ve.style.marginBottom = ParseLength(vals[2]);
                        dom.ve.style.marginLeft = ParseLength(vals[4]);
                        dom.ve.style.marginRight = ParseLength(vals[1]);
                    }
                }
            }, {
                "mt", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.marginTop = ParseLength(vals[0]);
                }
            }, {
                "mb", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.marginBottom = ParseLength(vals[0]);
                }
            }, {
                "ml", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.marginLeft = ParseLength(vals[0]);
                }
            }, {
                "mr", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.marginRight = ParseLength(vals[0]);
                }
            }, {
                "mx", (vals, dom) => {
                    if (vals.Length == 1) {
                        var v = ParseLength(vals[0]);
                        dom.ve.style.marginLeft = v;
                        dom.ve.style.marginRight = v;
                    }
                }
            }, {
                "my", (vals, dom) => {
                    if (vals.Length == 1) {
                        var v = ParseLength(vals[0]);
                        dom.ve.style.marginTop = v;
                        dom.ve.style.marginBottom = v;
                    }
                }
            }, {
                "top", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.top = ParseLength(vals[0]);
                }
            }, {
                "right", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.right = ParseLength(vals[0]);
                }
            }, {
                "bottom", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.bottom = ParseLength(vals[0]);
                }
            }, {
                "left", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.left = ParseLength(vals[0]);
                }
            }, {
                "bg", (vals, dom) => {
                    if (vals.Length != 1)
                        return;
                    dom.ve.style.backgroundColor = ParseColor(vals[0]);
                }
            }, {
                "text", (vals, dom) => {
                    if (vals.Length != 1)
                        return;
                    var legnth = ParseLength(vals[0]);
                    if (legnth.keyword != StyleKeyword.Null) {
                        dom.ve.style.fontSize = legnth;
                        return;
                    }
                    dom.ve.style.color = ParseColor(vals[0]);
                }
            }, {
                "w", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.width = ParseLength(vals[0]);
                }
            }, {
                "min-w", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.minWidth = ParseLength(vals[0]);
                }
            }, {
                "max-w", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.maxWidth = ParseLength(vals[0]);
                }
            }, {
                "h", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.height = ParseLength(vals[0]);
                }
            }, {
                "min-h", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.minHeight = ParseLength(vals[0]);
                }
            }, {
                "max-h", (vals, dom) => {
                    if (vals.Length == 1) dom.ve.style.maxHeight = ParseLength(vals[0]);
                }
            }, {
                "rounded", (vals, dom) => {
                    if (vals.Length == 1) {
                        dom.ve.style.borderTopLeftRadius = ParseLength(vals[0]);
                        dom.ve.style.borderTopRightRadius = ParseLength(vals[0]);
                        dom.ve.style.borderBottomRightRadius = ParseLength(vals[0]);
                        dom.ve.style.borderBottomLeftRadius = ParseLength(vals[0]);
                    } else if (vals.Length == 2) {
                        dom.ve.style.borderTopLeftRadius = ParseLength(vals[0]);
                        dom.ve.style.borderTopRightRadius = ParseLength(vals[1]);
                        dom.ve.style.borderBottomRightRadius = ParseLength(vals[0]);
                        dom.ve.style.borderBottomLeftRadius = ParseLength(vals[1]);
                    } else if (vals.Length == 3) {
                        dom.ve.style.borderTopLeftRadius = ParseLength(vals[0]);
                        dom.ve.style.borderTopRightRadius = ParseLength(vals[1]);
                        dom.ve.style.borderBottomRightRadius = ParseLength(vals[2]);
                        dom.ve.style.borderBottomLeftRadius = ParseLength(vals[1]);
                    } else if (vals.Length == 4) {
                        dom.ve.style.borderTopLeftRadius = ParseLength(vals[0]);
                        dom.ve.style.borderTopRightRadius = ParseLength(vals[1]);
                        dom.ve.style.borderBottomRightRadius = ParseLength(vals[2]);
                        dom.ve.style.borderBottomLeftRadius = ParseLength(vals[3]);
                    }
                }
            }, {
                "border", (vals, dom) => {
                    if (vals.Length != 1)
                        return;
                    var legnth = ParseLength(vals[0]);
                    if (legnth.keyword != StyleKeyword.Null) {
                        dom.ve.style.borderTopWidth = legnth.value.value;
                        dom.ve.style.borderRightWidth = legnth.value.value;
                        dom.ve.style.borderBottomWidth = legnth.value.value;
                        dom.ve.style.borderLeftWidth = legnth.value.value;
                        return;
                    }
                    dom.ve.style.borderTopColor = ParseColor(vals[0]);
                    dom.ve.style.borderRightColor = ParseColor(vals[0]);
                    dom.ve.style.borderBottomColor = ParseColor(vals[0]);
                    dom.ve.style.borderLeftColor = ParseColor(vals[0]);
                }
            }, {
                "border-t", (vals, dom) => {
                    if (vals.Length != 1)
                        return;
                    var legnth = ParseLength(vals[0]);
                    if (legnth.keyword != StyleKeyword.Null) {
                        dom.ve.style.borderTopWidth = legnth.value.value;
                        return;
                    }
                    dom.ve.style.borderTopColor = ParseColor(vals[0]);
                }
            }, {
                "border-r", (vals, dom) => {
                    if (vals.Length != 1)
                        return;
                    var legnth = ParseLength(vals[0]);
                    if (legnth.keyword != StyleKeyword.Null) {
                        dom.ve.style.borderRightWidth = legnth.value.value;
                        return;
                    }
                    dom.ve.style.borderRightColor = ParseColor(vals[0]);
                }
            }, {
                "border-b", (vals, dom) => {
                    if (vals.Length != 1)
                        return;
                    var legnth = ParseLength(vals[0]);
                    if (legnth.keyword != StyleKeyword.Null) {
                        dom.ve.style.borderBottomWidth = legnth.value.value;
                        return;
                    }
                    dom.ve.style.borderBottomColor = ParseColor(vals[0]);
                }
            }, {
                "border-l", (vals, dom) => {
                    if (vals.Length != 1)
                        return;
                    var legnth = ParseLength(vals[0]);
                    if (legnth.keyword != StyleKeyword.Null) {
                        dom.ve.style.borderLeftWidth = legnth.value.value;
                        return;
                    }
                    dom.ve.style.borderLeftColor = ParseColor(vals[0]);
                }
            }, {
                "transition", (vals, dom) => {
                    var list = vals.Select(s => new StylePropertyName(s)).ToList();
                    dom.ve.style.transitionProperty = new StyleList<StylePropertyName>(list);
                }
            }, {
                "duration", (vals, dom) => {
                    dom.ve.style.transitionDuration = new StyleList<TimeValue>(vals.Select(s => {
                        if (float.TryParse(s, out float res))
                            return new TimeValue(res);
                        return new TimeValue(0);
                    }).ToList());
                }
            }, {
                "ease", (vals, dom) => {
                    dom.ve.style.transitionTimingFunction = new StyleList<EasingFunction>(vals.Select(s => {
                        if (Enum.TryParse(s, out EasingMode res))
                            return new EasingFunction(res);
                        return new EasingFunction(EasingMode.Linear);
                    }).ToList());
                }
            }, {
                "delay", (vals, dom) => {
                    dom.ve.style.transitionDelay = new StyleList<TimeValue>(vals.Select(s => {
                        if (float.TryParse(s, out float res))
                            return new TimeValue(res);
                        return new TimeValue(0);
                    }).ToList());
                }
            }, {
                "translate-x", (vals, dom) => {
                    if (vals.Length == 1)
                        dom.ve.style.translate = new Translate(ParseLength(vals[0]).value, 0, 0);
                }
            }, {
                "translate-y", (vals, dom) => {
                    if (vals.Length == 1)
                        dom.ve.style.translate = new Translate(0, ParseLength(vals[0]).value, 0);
                }
            }, {
                "rotate", (vals, dom) => {
                    if (vals.Length != 1)
                        return;
                    dom.ve.style.rotate = ParseRotate(vals[0]);
                }
            }, {
                "scale", (vals, dom) => {
                    if (vals.Length == 1 && float.TryParse(vals[0], out float v)) {
                        dom.ve.style.scale = new StyleScale(new Scale(new Vector2(v, v)));
                    } else if (vals.Length == 2 && float.TryParse(vals[0], out float v1) &&
                               float.TryParse(vals[1], out float v2)) {
                        dom.ve.style.scale = new StyleScale(new Scale(new Vector2(v1, v2)));
                    }
                }
            }, {
                "origin", (vals, dom) => {
                    if (vals.Length == 2) {
                        dom.ve.style.transformOrigin =
                            new StyleTransformOrigin(new TransformOrigin(ParseLength(vals[0]).value,
                                ParseLength(vals[1]).value, 0));
                    }
                }
            }
        };
    }

    StyleLength ParseLength(string str) {
        var match = Regex.Match(str, "([\\d\\.-]+?)(px|%)");
        if (match.Success) {
            var numStr = match.Groups[1].Value;
            var unitStr = match.Groups[2].Value;
            return new StyleLength(new Length(float.Parse(numStr),
                unitStr == "%" ? LengthUnit.Percent : LengthUnit.Pixel));
        }
        return new StyleLength(StyleKeyword.Null);
    }

    StyleRotate ParseRotate(string str) {
        var match = Regex.Match(str, "([\\d\\.-]+?)(deg|rad|grad|turn)");
        if (match.Success) {
            var numStr = match.Groups[1].Value;
            var unitStr = match.Groups[2].Value;
            var unit = AngleUnit.Degree;
            if (unitStr == "grad")
                unit = AngleUnit.Gradian;
            if (unitStr == "rad")
                unit = AngleUnit.Radian;
            if (unitStr == "turn")
                unit = AngleUnit.Turn;
            return new StyleRotate(new Rotate(new Angle(float.Parse(numStr), unit)));
        }
        return new StyleRotate(StyleKeyword.Null);
    }

    StyleColor ParseColor(string str) {
        var match = Regex.Match(str,
            "#([0-9a-f][0-9a-f])([0-9a-f][0-9a-f])([0-9a-f][0-9a-f])([0-9a-f][0-9a-f])?",
            RegexOptions.IgnoreCase);
        if (match.Success) {
            var v1 = Convert.ToInt32($"0x{match.Groups[1].Value}", 16) / 255f;
            var v2 = Convert.ToInt32($"0x{match.Groups[2].Value}", 16) / 255f;
            var v3 = Convert.ToInt32($"0x{match.Groups[3].Value}", 16) / 255f;
            var v4 = match.Groups.Count > 4 && match.Groups[4].Value != string.Empty
                ? Convert.ToInt32($"0x{match.Groups[4].Value}", 16) / 255f
                : 1f;
            return new Color(v1, v2, v3, v4);
        }
        return new StyleColor(StyleKeyword.Null);
    }
}