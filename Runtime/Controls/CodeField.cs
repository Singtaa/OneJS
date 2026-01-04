using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS
{
    /// <summary>
    /// A TextField with syntax highlighting support via per-glyph vertex coloring.
    /// Uses UI Toolkit's <see cref="TextElement.PostProcessTextVertices"/> callback to colorize
    /// individual glyphs without affecting cursor positioning or text editing behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike rich text approaches that embed color tags in the text, CodeField applies colors
    /// at render time by modifying vertex tint values. This ensures cursor positions always
    /// correspond to actual character indices in the text.
    /// </para>
    /// <para>
    /// The control is multiline by default and includes a built-in JavaScript/TypeScript
    /// highlighter. Custom highlighters can be provided via the <see cref="Highlighter"/> property.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Basic usage
    /// var codeField = new CodeField();
    /// codeField.value = "const x = 42;";
    ///
    /// // Custom highlighter
    /// codeField.Highlighter = new MyPythonHighlighter();
    /// </code>
    /// </example>
    [UxmlElement]
    public partial class CodeField : TextField
    {
        /// <summary>
        /// Interface for syntax highlighters that provide color information per character.
        /// Implement this interface to add support for custom languages or color schemes.
        /// </summary>
        public interface ISyntaxHighlighter
        {
            /// <summary>
            /// Analyzes the text and returns a color for each character.
            /// </summary>
            /// <param name="text">The text to highlight.</param>
            /// <returns>
            /// An array of colors where each index corresponds to the character at that position.
            /// The array length must equal the text length.
            /// </returns>
            Color32[] Highlight(string text);
        }

        /// <summary>
        /// Built-in syntax highlighter for JavaScript/TypeScript/JSX code.
        /// Highlights keywords, strings, numbers, comments, and JSX elements with customizable colors.
        /// </summary>
        public class SimpleKeywordHighlighter : ISyntaxHighlighter
        {
            public Color32 DefaultColor = new Color32(212, 212, 212, 255);      // Light gray
            public Color32 KeywordColor = new Color32(197, 134, 192, 255);      // Purple
            public Color32 StringColor = new Color32(206, 145, 120, 255);       // Orange
            public Color32 NumberColor = new Color32(181, 206, 168, 255);       // Light green
            public Color32 CommentColor = new Color32(106, 153, 85, 255);       // Green
            public Color32 JsxTagColor = new Color32(86, 156, 214, 255);        // Blue (for JSX tags)
            public Color32 JsxAttributeColor = new Color32(156, 220, 254, 255); // Light blue (for JSX attributes)

            private static readonly HashSet<string> Keywords = new HashSet<string>
            {
                "if", "else", "for", "while", "do", "switch", "case", "break", "continue", "return",
                "function", "var", "let", "const", "class", "extends", "new", "this", "super",
                "import", "export", "from", "default", "async", "await", "try", "catch", "finally",
                "throw", "typeof", "instanceof", "in", "of", "true", "false", "null", "undefined"
            };

            // Reusable StringBuilder to avoid allocations when extracting keywords
            private readonly System.Text.StringBuilder _wordBuilder = new System.Text.StringBuilder(64);

            public Color32[] Highlight(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return Array.Empty<Color32>();

                var colors = new Color32[text.Length];
                var defaultColor = DefaultColor;
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = defaultColor;

                int pos = 0;
                while (pos < text.Length)
                {
                    char c = text[pos];

                    // Single-line comment
                    if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                    {
                        int start = pos;
                        while (pos < text.Length && text[pos] != '\n')
                            pos++;
                        for (int i = start; i < pos; i++)
                            colors[i] = CommentColor;
                        continue;
                    }

                    // Multi-line comment
                    if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
                    {
                        int start = pos;
                        pos += 2;
                        while (pos + 1 < text.Length && !(text[pos] == '*' && text[pos + 1] == '/'))
                            pos++;
                        pos += 2;
                        for (int i = start; i < pos && i < colors.Length; i++)
                            colors[i] = CommentColor;
                        continue;
                    }

                    // JSX tag: <TagName, </TagName, or <>
                    if (c == '<' && pos + 1 < text.Length)
                    {
                        char next = text[pos + 1];
                        // Check for JSX: < followed by letter, /, or > (fragment)
                        if (char.IsLetter(next) || next == '/' || next == '>')
                        {
                            int tagStart = pos;
                            colors[pos] = JsxTagColor; // <
                            pos++;

                            // Handle closing tag or fragment
                            if (pos < text.Length && text[pos] == '/')
                            {
                                colors[pos] = JsxTagColor;
                                pos++;
                            }

                            // Parse tag name (if any)
                            if (pos < text.Length && char.IsLetter(text[pos]))
                            {
                                int nameStart = pos;
                                while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_' || text[pos] == '-' || text[pos] == '.'))
                                    pos++;
                                for (int i = nameStart; i < pos; i++)
                                    colors[i] = JsxTagColor;
                            }

                            // Parse attributes until > or />
                            while (pos < text.Length && text[pos] != '>')
                            {
                                // Skip whitespace
                                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                                    pos++;

                                if (pos >= text.Length || text[pos] == '>' || text[pos] == '/')
                                    break;

                                // Attribute name
                                if (char.IsLetter(text[pos]) || text[pos] == '_')
                                {
                                    int attrStart = pos;
                                    while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_' || text[pos] == '-'))
                                        pos++;
                                    for (int i = attrStart; i < pos; i++)
                                        colors[i] = JsxAttributeColor;

                                    // Skip = if present
                                    if (pos < text.Length && text[pos] == '=')
                                        pos++;

                                    // Attribute value
                                    if (pos < text.Length)
                                    {
                                        if (text[pos] == '"' || text[pos] == '\'')
                                        {
                                            // String attribute value
                                            char quote = text[pos];
                                            int strStart = pos;
                                            pos++;
                                            while (pos < text.Length && text[pos] != quote)
                                            {
                                                if (text[pos] == '\\' && pos + 1 < text.Length)
                                                    pos++;
                                                pos++;
                                            }
                                            if (pos < text.Length)
                                                pos++;
                                            for (int i = strStart; i < pos && i < colors.Length; i++)
                                                colors[i] = StringColor;
                                        }
                                        else if (text[pos] == '{')
                                        {
                                            // JSX expression - parse balanced braces
                                            pos = HighlightJsxExpression(text, pos, colors);
                                        }
                                    }
                                }
                                else if (text[pos] == '{')
                                {
                                    // Spread attribute {...props}
                                    pos = HighlightJsxExpression(text, pos, colors);
                                }
                                else
                                {
                                    pos++;
                                }
                            }

                            // Handle /> or >
                            if (pos < text.Length && text[pos] == '/')
                            {
                                colors[pos] = JsxTagColor;
                                pos++;
                            }
                            if (pos < text.Length && text[pos] == '>')
                            {
                                colors[pos] = JsxTagColor;
                                pos++;
                            }
                            continue;
                        }
                    }

                    // String literals
                    if (c == '"' || c == '\'' || c == '`')
                    {
                        char quote = c;
                        int start = pos;
                        pos++;
                        while (pos < text.Length)
                        {
                            if (text[pos] == '\\' && pos + 1 < text.Length)
                            {
                                pos += 2;
                                continue;
                            }
                            if (text[pos] == quote)
                            {
                                pos++;
                                break;
                            }
                            pos++;
                        }
                        for (int i = start; i < pos && i < colors.Length; i++)
                            colors[i] = StringColor;
                        continue;
                    }

                    // Numbers
                    if (char.IsDigit(c) || (c == '.' && pos + 1 < text.Length && char.IsDigit(text[pos + 1])))
                    {
                        int start = pos;
                        while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.' || text[pos] == 'x' || text[pos] == 'X' ||
                               (text[pos] >= 'a' && text[pos] <= 'f') || (text[pos] >= 'A' && text[pos] <= 'F')))
                            pos++;
                        for (int i = start; i < pos; i++)
                            colors[i] = NumberColor;
                        continue;
                    }

                    // Identifiers and keywords
                    if (char.IsLetter(c) || c == '_' || c == '$')
                    {
                        int start = pos;
                        while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_' || text[pos] == '$'))
                            pos++;

                        // Check if it's a keyword without allocating a new string
                        if (IsKeyword(text, start, pos - start))
                        {
                            for (int i = start; i < pos; i++)
                                colors[i] = KeywordColor;
                        }
                        continue;
                    }

                    pos++;
                }

                return colors;
            }

            /// <summary>
            /// Highlights a JSX expression {...} and returns the position after the closing brace.
            /// Handles nested braces and strings within the expression.
            /// </summary>
            private int HighlightJsxExpression(string text, int pos, Color32[] colors)
            {
                if (pos >= text.Length || text[pos] != '{')
                    return pos;

                int braceDepth = 1;
                pos++; // Skip opening {

                while (pos < text.Length && braceDepth > 0)
                {
                    char c = text[pos];

                    if (c == '{')
                    {
                        braceDepth++;
                        pos++;
                    }
                    else if (c == '}')
                    {
                        braceDepth--;
                        pos++;
                    }
                    else if (c == '"' || c == '\'' || c == '`')
                    {
                        // String inside expression
                        char quote = c;
                        int strStart = pos;
                        pos++;
                        while (pos < text.Length && text[pos] != quote)
                        {
                            if (text[pos] == '\\' && pos + 1 < text.Length)
                                pos++;
                            pos++;
                        }
                        if (pos < text.Length)
                            pos++;
                        for (int i = strStart; i < pos && i < colors.Length; i++)
                            colors[i] = StringColor;
                    }
                    else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                    {
                        // Single-line comment inside expression
                        int commentStart = pos;
                        while (pos < text.Length && text[pos] != '\n')
                            pos++;
                        for (int i = commentStart; i < pos; i++)
                            colors[i] = CommentColor;
                    }
                    else if (char.IsDigit(c))
                    {
                        // Number inside expression
                        int numStart = pos;
                        while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.' || text[pos] == 'x'))
                            pos++;
                        for (int i = numStart; i < pos; i++)
                            colors[i] = NumberColor;
                    }
                    else if (char.IsLetter(c) || c == '_' || c == '$')
                    {
                        // Identifier or keyword inside expression
                        int idStart = pos;
                        while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_' || text[pos] == '$'))
                            pos++;
                        if (IsKeyword(text, idStart, pos - idStart))
                        {
                            for (int i = idStart; i < pos; i++)
                                colors[i] = KeywordColor;
                        }
                    }
                    else
                    {
                        pos++;
                    }
                }

                return pos;
            }

            /// <summary>
            /// Checks if a substring matches a keyword without allocating a new string.
            /// </summary>
            private static bool IsKeyword(string text, int start, int length)
            {
                // Quick length check - keywords are 2-11 chars
                if (length < 2 || length > 11)
                    return false;

                // Check against each keyword
                foreach (var keyword in Keywords)
                {
                    if (keyword.Length != length)
                        continue;

                    bool match = true;
                    for (int i = 0; i < length; i++)
                    {
                        if (text[start + i] != keyword[i])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                        return true;
                }

                return false;
            }
        }

        private static Font _monospaceFont;
        private static bool _fontLoadAttempted;

        private ISyntaxHighlighter _highlighter;
        private Color32[] _characterColors;
        private Color32[] _visibleCharacterColors; // Colors for visible glyphs only
        private TextElement _textElement;
        private ScrollView _scrollView;
        private int _indentSize = 4;
        private bool _indentUsingSpaces = true;
        private bool _autoHeight = false;
        private float _lineHeight = 18f;
        private int _minLines = 3;

        // Debouncing and caching for large text performance
        private IVisualElementScheduledItem _highlightSchedule;
        private string _lastHighlightedText;
        private const long HighlightDebounceMs = 50; // Debounce delay in milliseconds

        /// <summary>
        /// Gets a monospace font from the system. Caches the result.
        /// </summary>
        private static Font GetMonospaceFont()
        {
            if (_fontLoadAttempted)
                return _monospaceFont;

            _fontLoadAttempted = true;

            // Try common monospace fonts in order of preference
            string[] fontNames;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS
            fontNames = new[] { "Menlo", "SF Mono", "Monaco", "Courier New", "Courier" };
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            fontNames = new[] { "Consolas", "Cascadia Code", "Courier New", "Lucida Console" };
#else
            fontNames = new[] { "DejaVu Sans Mono", "Liberation Mono", "Consolas", "Courier New", "monospace" };
#endif

            foreach (var fontName in fontNames)
            {
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(fontName, 14);
                    if (font != null)
                    {
                        _monospaceFont = font;
                        Debug.Log($"[CodeField] Loaded monospace font: {fontName}");
                        return font;
                    }
                }
                catch
                {
                    // Font not available, try next
                }
            }

            Debug.LogWarning("[CodeField] Could not load any monospace font");
            return null;
        }

        /// <summary>
        /// Applies the monospace font to a VisualElement and all its TextElement children.
        /// </summary>
        private static void ApplyMonospaceFont(VisualElement element)
        {
            var font = GetMonospaceFont();
            if (font == null || element == null)
                return;

            var fontDef = new StyleFontDefinition(FontDefinition.FromFont(font));

            // Apply to all TextElements in the hierarchy
            element.Query<TextElement>().ForEach(te =>
            {
                te.style.unityFontDefinition = fontDef;
            });

            // Also set on the element itself in case it's a TextElement
            if (element is TextElement textElement)
            {
                textElement.style.unityFontDefinition = fontDef;
            }
        }

        /// <summary>
        /// The syntax highlighter to use. Set to null to disable highlighting.
        /// </summary>
        public ISyntaxHighlighter Highlighter
        {
            get => _highlighter;
            set
            {
                _highlighter = value;
                RefreshHighlighting();
            }
        }

        /// <summary>
        /// When true, pressing Tab inserts spaces. When false, inserts a tab character.
        /// Default is true (spaces).
        /// </summary>
        public bool IndentUsingSpaces
        {
            get => _indentUsingSpaces;
            set => _indentUsingSpaces = value;
        }

        /// <summary>
        /// Number of spaces to insert when pressing Tab (only applies when IndentUsingSpaces is true).
        /// Also controls how many spaces to remove when dedenting. Default is 4.
        /// </summary>
        public int IndentSize
        {
            get => _indentSize;
            set => _indentSize = Math.Max(1, value);
        }

        /// <summary>
        /// When true, automatically adjusts height based on content line count.
        /// Also affects scroll behavior: AutoHeight on passes vertical scroll to parent,
        /// AutoHeight off enables internal vertical scrolling with scroll chaining.
        /// Default is false.
        /// </summary>
        public bool AutoHeight
        {
            get => _autoHeight;
            set
            {
                _autoHeight = value;
                if (_scrollView != null)
                {
                    _scrollView.mode = value ? ScrollViewMode.Horizontal : ScrollViewMode.VerticalAndHorizontal;
                    _scrollView.verticalScrollerVisibility = value ? ScrollerVisibility.Hidden : ScrollerVisibility.Auto;
                }
                if (_autoHeight) UpdateAutoHeight();
            }
        }

        /// <summary>
        /// Height per line in pixels when AutoHeight is enabled. Default is 18.
        /// </summary>
        public float LineHeight
        {
            get => _lineHeight;
            set
            {
                _lineHeight = Math.Max(1f, value);
                if (_autoHeight) UpdateAutoHeight();
            }
        }

        /// <summary>
        /// Minimum number of lines to display when AutoHeight is enabled. Default is 3.
        /// </summary>
        public int MinLines
        {
            get => _minLines;
            set
            {
                _minLines = Math.Max(1, value);
                if (_autoHeight) UpdateAutoHeight();
            }
        }

        public CodeField() : this(null, -1, false, false, default) { }

        public CodeField(string label) : this(label, -1, false, false, default) { }

        public CodeField(string label, int maxLength, bool multiline, bool isPasswordField, char maskChar)
            : base(label, maxLength, multiline, isPasswordField, maskChar)
        {
            // Default to multiline for code
            this.multiline = true;

            // Disable select-all-on-focus (not useful for code editing)
            this.selectAllOnFocus = false;
            this.selectAllOnMouseUp = false;

            // Use a simple keyword highlighter by default
            _highlighter = new SimpleKeywordHighlighter();

            // Find the TextElement child and configure it
            _textElement = this.Q<TextElement>();
            if (_textElement != null)
            {
                _textElement.PostProcessTextVertices = ColorizeGlyphs;
            }

            // Configure horizontal scrolling
            ConfigureScrolling();

            // Apply monospace font to all text elements
            ApplyMonospaceFont(this);

            // Refresh highlighting and auto-height when value changes
            this.RegisterValueChangedCallback(evt =>
            {
                ScheduleRefreshHighlighting(); // Debounced for large text performance
                if (_autoHeight) UpdateAutoHeight();
            });

            // Trigger initial setup after the element is attached
            RegisterCallback<AttachToPanelEvent>(evt =>
            {
                // Re-query in case hierarchy wasn't ready in constructor
                if (_textElement == null)
                {
                    _textElement = this.Q<TextElement>();
                    if (_textElement != null)
                    {
                        _textElement.PostProcessTextVertices = ColorizeGlyphs;
                    }
                }

                // Configure scrolling (may need to reapply after attach)
                ConfigureScrolling();

                // Apply monospace font (may need to reapply after attach)
                ApplyMonospaceFont(this);

                // Schedule to ensure layout is complete
                schedule.Execute(() =>
                {
                    RefreshHighlighting();
                    if (_autoHeight) UpdateAutoHeight();
                });
            });

            // Handle Tab key for space indentation
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // Add USS class for styling
            AddToClassList("code-field");
        }

        /// <summary>
        /// Configures the internal ScrollView for scrolling.
        /// When AutoHeight is on, only horizontal scrolling (content expands vertically).
        /// When AutoHeight is off, both horizontal and vertical scrolling.
        /// </summary>
        private void ConfigureScrolling()
        {
            // Find the ScrollView inside TextField (if it exists)
            _scrollView = this.Q<ScrollView>();
            if (_scrollView != null)
            {
                // When AutoHeight is off, allow both horizontal and vertical scrolling
                // When AutoHeight is on, only horizontal (content expands vertically)
                _scrollView.mode = _autoHeight ? ScrollViewMode.Horizontal : ScrollViewMode.VerticalAndHorizontal;
                _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Auto;
                _scrollView.verticalScrollerVisibility = _autoHeight ? ScrollerVisibility.Hidden : ScrollerVisibility.Auto;
                _scrollView.elasticity = 0; // Disable elastic scrolling for code
            }

            // Prevent text wrapping in the text input
            var textInput = this.Q(className: "unity-text-field__input");
            if (textInput != null)
            {
                textInput.style.whiteSpace = WhiteSpace.NoWrap;
                textInput.style.overflow = Overflow.Hidden;

                // Handle wheel events for horizontal scrolling (trackpad support)
                textInput.RegisterCallback<WheelEvent>(OnWheel);
            }

            // Ensure the text element doesn't wrap
            if (_textElement != null)
            {
                _textElement.style.whiteSpace = WhiteSpace.NoWrap;
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            bool isVerticalScroll = Math.Abs(evt.delta.y) > Math.Abs(evt.delta.x);

            // AutoHeight mode: pass through vertical scroll immediately to parent
            if (_autoHeight && isVerticalScroll)
            {
                return; // Let parent handle it
            }

            // Fixed height mode: handle vertical scroll with scroll chaining
            if (!_autoHeight && isVerticalScroll && _scrollView != null)
            {
                float scrollPos = _scrollView.scrollOffset.y;
                float maxScroll = _scrollView.contentContainer.resolvedStyle.height - _scrollView.contentViewport.resolvedStyle.height;
                maxScroll = Math.Max(0, maxScroll);

                bool atTop = scrollPos <= 0 && evt.delta.y < 0;
                bool atBottom = scrollPos >= maxScroll && evt.delta.y > 0;

                if (atTop || atBottom)
                {
                    return; // At bounds, let parent scroll
                }

                // Let ScrollView handle it naturally, but stop propagation
                evt.StopPropagation();
                return;
            }

            // Horizontal scrolling logic
            var textInput = this.Q(className: "unity-text-field__input");
            if (textInput == null) return;

            var contentContainer = _textElement?.parent;
            if (contentContainer == null) return;

            // Only use actual horizontal delta (no vertical-to-horizontal conversion)
            float deltaX = evt.delta.x;
            if (Math.Abs(deltaX) < 0.01f) return; // No horizontal scroll, let event propagate

            // Apply scroll by adjusting the content's left position
            var currentLeft = contentContainer.style.left.value.value;
            var newLeft = currentLeft - deltaX * 20f; // Multiply for reasonable scroll speed

            // Clamp to valid range
            var maxScrollX = Math.Max(0, contentContainer.resolvedStyle.width - textInput.resolvedStyle.width);
            newLeft = Math.Max(-maxScrollX, Math.Min(0, newLeft));

            contentContainer.style.left = newLeft;
            evt.StopPropagation();
        }

        /// <summary>
        /// Updates the height of the control based on content line count.
        /// </summary>
        private void UpdateAutoHeight()
        {
            if (!_autoHeight) return;

            var lineCount = CountLines(value);
            var numLines = Math.Max(lineCount, _minLines);
            var height = numLines * _lineHeight + 16; // 16 for padding

            style.height = height;
        }

        /// <summary>
        /// Counts lines without allocating an array (avoids Split allocation).
        /// </summary>
        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 1;

            int count = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    count++;
            }
            return count;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Tab)
            {
                evt.StopPropagation();

                if (evt.shiftKey)
                {
                    HandleDedent();
                }
                else
                {
                    HandleIndent();
                }
            }
            // Smart backspace: only when no modifiers are pressed (allow Cmd+Backspace, Ctrl+Backspace, etc.)
            else if (evt.keyCode == KeyCode.Backspace &&
                     _indentUsingSpaces &&
                     !evt.commandKey &&
                     !evt.ctrlKey &&
                     !evt.altKey)
            {
                if (HandleSmartBackspace())
                {
                    evt.StopPropagation();
                }
            }
            // Fix Cmd+Arrow navigation for multiline (UI Toolkit quirk)
            else if (evt.commandKey && !evt.shiftKey && !evt.altKey && !evt.ctrlKey)
            {
                if (evt.keyCode == KeyCode.RightArrow)
                {
                    evt.StopPropagation();
                    cursorIndex = GetLineEnd(value, cursorIndex);
                    selectIndex = cursorIndex;
                }
                else if (evt.keyCode == KeyCode.LeftArrow)
                {
                    evt.StopPropagation();
                    cursorIndex = GetLineStart(value, cursorIndex);
                    selectIndex = cursorIndex;
                }
            }
            // Fix Cmd+Shift+Arrow selection for multiline
            else if (evt.commandKey && evt.shiftKey && !evt.altKey && !evt.ctrlKey)
            {
                if (evt.keyCode == KeyCode.RightArrow)
                {
                    evt.StopPropagation();
                    cursorIndex = GetLineEnd(value, cursorIndex);
                }
                else if (evt.keyCode == KeyCode.LeftArrow)
                {
                    evt.StopPropagation();
                    cursorIndex = GetLineStart(value, cursorIndex);
                }
            }
        }

        /// <summary>
        /// Handles smart backspace: when cursor is in leading whitespace, delete back to previous indent level.
        /// Returns true if handled, false if default backspace behavior should apply.
        /// </summary>
        private bool HandleSmartBackspace()
        {
            // Only apply when there's no selection
            if (selectIndex != cursorIndex)
                return false;

            int cursorPos = cursorIndex;
            if (cursorPos == 0)
                return false;

            int lineStart = GetLineStart(value, cursorPos);

            // Check if cursor is within leading whitespace
            bool inLeadingWhitespace = true;
            for (int i = lineStart; i < cursorPos; i++)
            {
                if (value[i] != ' ' && value[i] != '\t')
                {
                    inLeadingWhitespace = false;
                    break;
                }
            }

            if (!inLeadingWhitespace)
                return false;

            // Count spaces from line start to cursor
            int spacesBeforeCursor = 0;
            for (int i = lineStart; i < cursorPos; i++)
            {
                if (value[i] == ' ')
                    spacesBeforeCursor++;
                else if (value[i] == '\t')
                    spacesBeforeCursor += _indentSize; // Treat tab as IndentSize spaces for alignment
                else
                    break;
            }

            if (spacesBeforeCursor == 0)
                return false;

            // Calculate previous indent level
            int remainder = spacesBeforeCursor % _indentSize;

            int spacesToDelete;
            if (remainder > 0)
            {
                // Delete just the remainder to align to indent level
                spacesToDelete = remainder;
            }
            else
            {
                // Delete full indent
                spacesToDelete = _indentSize;
            }

            // Make sure we don't delete more than available
            spacesToDelete = Math.Min(spacesToDelete, cursorPos - lineStart);

            if (spacesToDelete <= 0)
                return false;

            // Delete the spaces
            value = value.Remove(cursorPos - spacesToDelete, spacesToDelete);
            cursorIndex = cursorPos - spacesToDelete;
            selectIndex = cursorIndex;

            return true;
        }

        private void HandleIndent()
        {
            string indentString = _indentUsingSpaces ? new string(' ', _indentSize) : "\t";
            int indentLength = indentString.Length;
            int cursorPos = cursorIndex;

            // Check if there's a selection
            if (selectIndex != cursorIndex)
            {
                // Indent all selected lines
                int selStart = Math.Min(cursorIndex, selectIndex);
                int selEnd = Math.Max(cursorIndex, selectIndex);

                int lineStart = GetLineStart(value, selStart);
                int lineEnd = GetLineEnd(value, selEnd);

                // Find all line starts in the selection range
                var newText = new System.Text.StringBuilder();
                int addedChars = 0;
                int lastPos = 0;

                for (int i = lineStart; i <= lineEnd && i < value.Length; i++)
                {
                    if (i == lineStart || (i > 0 && value[i - 1] == '\n'))
                    {
                        newText.Append(value.Substring(lastPos, i - lastPos));
                        newText.Append(indentString);
                        lastPos = i;
                        addedChars += indentLength;
                    }
                }
                newText.Append(value.Substring(lastPos));

                value = newText.ToString();
                // Adjust selection
                selectIndex = selStart + indentLength;
                cursorIndex = selEnd + addedChars;
            }
            else
            {
                // No selection - just insert indent at cursor
                value = value.Insert(cursorPos, indentString);
                cursorIndex = cursorPos + indentLength;
                selectIndex = cursorIndex;
            }
        }

        private void HandleDedent()
        {
            int cursorPos = cursorIndex;
            int selStart = Math.Min(cursorIndex, selectIndex);
            int selEnd = Math.Max(cursorIndex, selectIndex);
            bool hasSelection = selectIndex != cursorIndex;

            int lineStart = GetLineStart(value, selStart);
            int lineEnd = hasSelection ? GetLineEnd(value, selEnd) : GetLineEnd(value, cursorPos);

            var newText = new System.Text.StringBuilder();
            int removedBeforeCursor = 0;
            int removedTotal = 0;
            int lastPos = 0;

            for (int i = lineStart; i <= lineEnd && i < value.Length; i++)
            {
                if (i == lineStart || (i > 0 && value[i - 1] == '\n'))
                {
                    // Found a line start, check for leading whitespace
                    newText.Append(value.Substring(lastPos, i - lastPos));

                    int charsToRemove = 0;

                    // Check for tab character first
                    if (i < value.Length && value[i] == '\t')
                    {
                        charsToRemove = 1;
                    }
                    else
                    {
                        // Check for spaces (up to IndentSize)
                        for (int j = i; j < value.Length && j < i + _indentSize; j++)
                        {
                            if (value[j] == ' ')
                                charsToRemove++;
                            else
                                break;
                        }
                    }

                    if (charsToRemove > 0)
                    {
                        lastPos = i + charsToRemove;
                        if (i < selStart)
                            removedBeforeCursor += charsToRemove;
                        removedTotal += charsToRemove;
                    }
                    else
                    {
                        lastPos = i;
                    }
                }
            }
            newText.Append(value.Substring(lastPos));

            if (removedTotal > 0)
            {
                value = newText.ToString();
                if (hasSelection)
                {
                    selectIndex = Math.Max(0, selStart - removedBeforeCursor);
                    cursorIndex = Math.Max(selectIndex, selEnd - removedTotal);
                }
                else
                {
                    cursorIndex = Math.Max(GetLineStart(value, Math.Max(0, cursorPos - removedBeforeCursor)),
                                          cursorPos - removedBeforeCursor);
                    selectIndex = cursorIndex;
                }
            }
        }

        private static int GetLineStart(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position <= 0)
                return 0;

            position = Math.Min(position, text.Length);
            for (int i = position - 1; i >= 0; i--)
            {
                if (text[i] == '\n')
                    return i + 1;
            }
            return 0;
        }

        private static int GetLineEnd(string text, int position)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            position = Math.Min(position, text.Length - 1);
            for (int i = position; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    return i;
            }
            return text.Length;
        }

        private void RefreshHighlighting()
        {
            RefreshHighlighting(false);
        }

        private void RefreshHighlighting(bool force)
        {
            // Skip if text hasn't changed (cache check)
            var currentText = value;
            if (!force && currentText == _lastHighlightedText)
            {
                return;
            }
            _lastHighlightedText = currentText;

            if (_highlighter != null && !string.IsNullOrEmpty(currentText))
            {
                _characterColors = _highlighter.Highlight(currentText);
                // Build visible-only color array (skip newlines and control chars)
                _visibleCharacterColors = BuildVisibleColors(currentText, _characterColors);
            }
            else
            {
                _characterColors = null;
                _visibleCharacterColors = null;
            }
            _textElement?.MarkDirtyRepaint();
        }

        /// <summary>
        /// Schedules a debounced refresh of syntax highlighting.
        /// Use this for high-frequency triggers like value changes.
        /// </summary>
        private void ScheduleRefreshHighlighting()
        {
            // Cancel any pending scheduled highlight
            _highlightSchedule?.Pause();

            // Schedule new highlight after debounce delay
            _highlightSchedule = schedule.Execute(() =>
            {
                RefreshHighlighting();
                _highlightSchedule = null;
            }).StartingIn(HighlightDebounceMs);
        }

        /// <summary>
        /// Builds a color array containing only colors for visible characters.
        /// The glyph enumerator skips invisible characters (newlines, etc.),
        /// so we need to match that behavior.
        /// </summary>
        private static Color32[] BuildVisibleColors(string text, Color32[] allColors)
        {
            if (allColors == null || allColors.Length == 0)
                return Array.Empty<Color32>();

            var visibleColors = new List<Color32>(allColors.Length);
            for (int i = 0; i < text.Length && i < allColors.Length; i++)
            {
                char c = text[i];
                // Skip characters that don't produce visible glyphs
                // Based on Unity's TextElement behavior: newlines, carriage returns,
                // and other control characters are not rendered as glyphs
                if (IsVisibleCharacter(c))
                {
                    visibleColors.Add(allColors[i]);
                }
            }
            return visibleColors.ToArray();
        }

        /// <summary>
        /// Returns true if the character produces a visible glyph.
        /// </summary>
        private static bool IsVisibleCharacter(char c)
        {
            // Newlines and carriage returns don't produce visible glyphs
            if (c == '\n' || c == '\r')
                return false;

            // Other common invisible/control characters
            if (c == '\0')
                return false;

            // Tab might produce a glyph or might not depending on settings
            // For now, treat it as visible (space-like)
            // if (c == '\t') return false;

            return true;
        }

        private void ColorizeGlyphs(TextElement.GlyphsEnumerable glyphs)
        {
            if (_visibleCharacterColors == null || _visibleCharacterColors.Length == 0)
                return;

            int glyphIndex = 0;
            foreach (var glyph in glyphs)
            {
                if (glyphIndex >= _visibleCharacterColors.Length)
                    break;

                Color32 color = _visibleCharacterColors[glyphIndex];
                var vertices = glyph.vertices;

                // Each glyph has 4 vertices (quad)
                for (int i = 0; i < vertices.Length && i < 4; i++)
                {
                    var vertex = vertices[i];
                    vertex.tint = color;
                    vertices[i] = vertex;
                }

                glyphIndex++;
            }
        }
    }
}
