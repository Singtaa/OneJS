using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OneJS;
using OneJS.Dom;
using OneJS.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace OneJS.Engine {
    [RequireComponent(typeof(ScriptEngine), typeof(UIDocument))]
    public class Tailwind : MonoBehaviour, IClassStrProcessor {
        [Tooltip("Tailwind output file. Relative to the OneJS working directory.")]
        [SerializeField] string _outputCssPath = "output.css";

        [SerializeField] StyleSheet _baseStyleSheet;
        [SerializeField] StyleSheet[] _breakpointStyleSheets;

        [Tooltip("Watch for screen size changes even for Standalone builds.")]
        [SerializeField] bool _pollStandaloneScreen;

        [Tooltip("Enable informational logging")]
        [SerializeField] bool _enableLogging;

        ScriptEngine _scriptEngine;
        UIDocument _uiDocument;

        float _lastScreenWidth;
        DateTime _lastCssWriteStamp = DateTime.UtcNow;
        float _lastCssCheckTime;

        (string, string)[] _replacePairsForCssStr = new[] {
            ("\\.", "_d_"), ("\\/", "_s_"), ("\\:", "_c_"), ("\\%", "_p_"), ("\\#", "_n_"),
            ("\\[", "_lb_"), ("\\]", "_rb_"), ("\\(", "_lp_"), ("\\)", "_rp_"),
            ("\\2c ", "_cm_")
        };

        (string, string)[] _replacePairsForClassNames = new[] {
            (".", "_d_"), ("/", "_s_"), (":", "_c_"), ("%", "_p_"), ("#", "_n_"),
            ("[", "_lb_"), ("]", "_rb_"), ("(", "_lp_"), (")", "_rp_"),
            (",", "_cm_")
        };

        void Awake() {
            _uiDocument = GetComponent<UIDocument>();
            _scriptEngine = GetComponent<ScriptEngine>();
            _uiDocument.rootVisualElement.styleSheets.Add(_baseStyleSheet);
            _scriptEngine.RegisterClassStrProcessor(this);
        }

        void Start() {
            if (_enableLogging) {
                print("tailwindcss -i ./input.css -o ./output.css --watch\n\n" +
                      $"Use the line above for tailwindcss cli. Run it at your OneJS working directory: {_scriptEngine.EditorModeWorkingDirInfo} \n");
            }
            CoroutineUtil.Start(
                CoroutineUtil.DelayFrames(PollScreenChange, 1)
            );
        }

        void Update() {
#if UNITY_EDITOR
            PollScreenChange();
            CheckOutputCssForChange();
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

        void SetBreakpointStyleSheet(float width) {
            var sheetSet = _uiDocument.rootVisualElement.styleSheets;
            foreach (var sheet in _breakpointStyleSheets) {
                if (sheet != null && sheetSet.Contains(sheet))
                    sheetSet.Remove(sheet);
            }
            for (int i = 0; i < _scriptEngine.Breakpoints.Length; i++) {
                var bp = _scriptEngine.Breakpoints[i];
                var sheet = _breakpointStyleSheets[i];
                if (width >= bp && sheet != null) {
                    sheetSet.Add(sheet);
                }
            }
        }

#if UNITY_EDITOR
        void CheckOutputCssForChange() {
            if (_lastCssCheckTime > Time.realtimeSinceStartup - 0.1f)
                return;
            _lastCssCheckTime = Time.realtimeSinceStartup;
            var path = Path.Combine(_scriptEngine.WorkingDir, _outputCssPath);
            if (!File.Exists(path))
                return;
            var fi = new FileInfo(path);
            if (_lastCssWriteStamp != fi.LastWriteTimeUtc) {
                _lastCssWriteStamp = fi.LastWriteTimeUtc;
                GenerateUssFiles();
            }
        }

        [ContextMenu("Generate USS Files")]
        void GenerateUssFiles() {
            _scriptEngine = GetComponent<ScriptEngine>();
            string css = File.ReadAllText(Path.Combine(_scriptEngine.WorkingDir, _outputCssPath));

            css = ConvertRgbToRgba(css);

            string pattern = @"@media[^\{]+\{(([^\{\}]+\{[^\{\}]+\}[^\{\}]*)+)\}";
            MatchCollection matches = Regex.Matches(css, pattern, RegexOptions.Singleline);

            string baseCss = css;
            string[] mediaTypes = { "sm", "md", "lg", "xl", "2xl" };
            for (int i = 0; i < mediaTypes.Length; i++) {
                var media = mediaTypes[i];
                string mediaCss = "";
                for (int j = 0; j < matches.Count; j++) {
                    if (matches[j].Groups[1].Value.Contains($".{media}\\:")) {
                        mediaCss = matches[j].Groups[1].Value.Trim() + Environment.NewLine;
                        baseCss = baseCss.Replace(matches[j].Value, "");
                        mediaCss = RemoveLeadingSpaces(mediaCss);
                        mediaCss = TransformCssText(mediaCss);
                        break;
                    }
                }
                File.WriteAllText(GetAbsoluteAssetPath(_breakpointStyleSheets[i]), mediaCss);
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(_breakpointStyleSheets[i]));
            }
            baseCss = Regex.Replace(baseCss, @"@media[^\{]+\{([^\{\}]+\{[^\{\}]+\}[^\{\}]*)+\}", "",
                RegexOptions.Singleline);
            baseCss = TransformCssText(baseCss);
            File.WriteAllText(GetAbsoluteAssetPath(_baseStyleSheet), baseCss);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(_baseStyleSheet));

            if (_enableLogging)
                print("Tailwind USS files Written.");
        }

        string RemoveLeadingSpaces(string input) {
            string pattern = @"^  ";
            string replacement = "";
            string output = Regex.Replace(input, pattern, replacement, RegexOptions.Multiline);
            return output;
        }

        string GetAbsoluteAssetPath(UnityEngine.Object obj) {
            string relativePath = AssetDatabase.GetAssetPath(obj);
            string absolutePath = Path.Combine(Application.dataPath, "../", relativePath);
            return absolutePath;
        }
#endif

        public string ProcessClassStr(string classStr, Dom.Dom dom) {
            var names = classStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            names = names.Select(s => s[0] >= 48 && s[0] <= 57 ? "_" + s : s).ToArray(); // ^\d => _\d

            var output = String.Join(" ", names);
            return TransformUsingPairs(output, _replacePairsForClassNames);
        }

        string TransformCssText(string cssText) {
            var output = Regex.Replace(cssText, @"^(\.)(\d.+)", "$1_$2");
            return TransformUsingPairs(output, _replacePairsForCssStr);
        }

        /// <summary>
        /// Basically a more performant version of chaining string.Replace
        /// </summary>
        string TransformUsingPairs(string input, (string, string)[] pairs) {
            var sb = new StringBuilder(input);
            var indices = new List<(int, int, string)>();

            foreach (var pair in pairs) {
                int index = 0;
                while ((index = input.IndexOf(pair.Item1, index)) != -1) {
                    indices.Add((index, pair.Item1.Length, pair.Item2));
                    index += pair.Item1.Length;
                }
            }

            indices.Sort((a, b) => b.Item1.CompareTo(a.Item1));

            foreach (var tuple in indices) {
                sb.Remove(tuple.Item1, tuple.Item2);
                sb.Insert(tuple.Item1, tuple.Item3);
            }

            return sb.ToString();
        }

        static string ConvertRgbToRgba(string input) {
            string pattern = @"rgb\((.*?) /\s*(.*?)\)";
            string replacement = "rgba($1 $2)";
            return Regex.Replace(input, pattern, replacement);
        }
    }
}
