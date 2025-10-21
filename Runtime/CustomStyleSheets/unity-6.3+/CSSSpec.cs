using System;
using System.Text.RegularExpressions;

namespace OneJS.CustomStyleSheets {
    public static class CSSSpec {
        public static int GetSelectorSpecificity(string selector) {
            int result = 0;
            StyleSelectorPart[] parts;
            bool flag = CSSSpec.ParseSelector(selector, out parts);
            if (flag) {
                result = CSSSpec.GetSelectorSpecificity(parts);
            }
            return result;
        }

        // Token: 0x06003A6B RID: 14955 RVA: 0x000E4930 File Offset: 0x000E2B30
        public static int GetSelectorSpecificity(StyleSelectorPart[] parts) {
            int num = 1;
            for (int i = 0; i < parts.Length; i++) {
                switch (parts[i].type) {
                    case StyleSelectorType.Type:
                        num++;
                        break;
                    case StyleSelectorType.Class:
                    case StyleSelectorType.PseudoClass:
                        num += 10;
                        break;
                    case StyleSelectorType.RecursivePseudoClass:
                        throw new ArgumentException("Recursive pseudo classes are not supported");
                    case StyleSelectorType.ID:
                        num += 100;
                        break;
                }
            }
            return num;
        }

        // Token: 0x06003A6C RID: 14956 RVA: 0x000E49AC File Offset: 0x000E2BAC
        public static bool ValidateSelector(string selector) {
            return CSSSpec.rgx.Matches(selector).Count > 0;
        }

        // Token: 0x06003A6D RID: 14957 RVA: 0x000E49D4 File Offset: 0x000E2BD4
        public static bool ParseSelector(string selector, out StyleSelectorPart[] parts) {
            MatchCollection matchCollection = CSSSpec.rgx.Matches(selector);
            int count = matchCollection.Count;
            bool flag = count < 1;
            bool result;
            if (flag) {
                parts = null;
                result = false;
            } else {
                parts = new StyleSelectorPart[count];
                for (int i = 0; i < count; i++) {
                    Match match = matchCollection[i];
                    StyleSelectorType type = StyleSelectorType.Unknown;
                    string value = string.Empty;
                    bool flag2 = !string.IsNullOrEmpty(match.Groups["wildcard"].Value);
                    if (flag2) {
                        value = "*";
                        type = StyleSelectorType.Wildcard;
                    } else {
                        bool flag3 = !string.IsNullOrEmpty(match.Groups["id"].Value);
                        if (flag3) {
                            value = match.Groups["id"].Value.Substring(1);
                            type = StyleSelectorType.ID;
                        } else {
                            bool flag4 = !string.IsNullOrEmpty(match.Groups["class"].Value);
                            if (flag4) {
                                value = match.Groups["class"].Value.Substring(1);
                                type = StyleSelectorType.Class;
                            } else {
                                bool flag5 = !string.IsNullOrEmpty(match.Groups["pseudoclass"].Value);
                                if (flag5) {
                                    string value2 = match.Groups["param"].Value;
                                    bool flag6 = !string.IsNullOrEmpty(value2);
                                    if (flag6) {
                                        value = value2;
                                        type = StyleSelectorType.RecursivePseudoClass;
                                    } else {
                                        value = match.Groups["pseudoclass"].Value.Substring(1);
                                        type = StyleSelectorType.PseudoClass;
                                    }
                                } else {
                                    bool flag7 = !string.IsNullOrEmpty(match.Groups["type"].Value);
                                    if (flag7) {
                                        value = match.Groups["type"].Value;
                                        type = StyleSelectorType.Type;
                                    }
                                }
                            }
                        }
                    }
                    parts[i] = new StyleSelectorPart {
                        type = type,
                        value = value
                    };
                }
                result = true;
            }
            return result;
        }

        // Token: 0x04001D3E RID: 7486
        private static readonly Regex rgx = new Regex("(?<id>#[-]?\\w[\\w-]*)|(?<class>\\.[\\w-]+)|(?<pseudoclass>:[\\w-]+(\\((?<param>.+)\\))?)|(?<type>([^\\-]\\w+|\\w+))|(?<wildcard>\\*)|\\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Token: 0x04001D3F RID: 7487
        private const int typeSelectorWeight = 1;

        // Token: 0x04001D40 RID: 7488
        private const int classSelectorWeight = 10;

        // Token: 0x04001D41 RID: 7489
        private const int idSelectorWeight = 100;
    }
}