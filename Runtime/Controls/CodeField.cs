using System;
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
    public class CodeField : TextField
    {
        public new class UxmlFactory : UxmlFactory<CodeField, UxmlTraits> { }

        public new class UxmlTraits : TextField.UxmlTraits { }

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
        /// Built-in syntax highlighter for JavaScript/TypeScript code.
        /// Highlights keywords, strings, numbers, and comments with customizable colors.
        /// </summary>
        public class SimpleKeywordHighlighter : ISyntaxHighlighter
        {
            public Color32 DefaultColor = new Color32(212, 212, 212, 255);      // Light gray
            public Color32 KeywordColor = new Color32(197, 134, 192, 255);      // Purple
            public Color32 StringColor = new Color32(206, 145, 120, 255);       // Orange
            public Color32 NumberColor = new Color32(181, 206, 168, 255);       // Light green
            public Color32 CommentColor = new Color32(106, 153, 85, 255);       // Green

            private static readonly HashSet<string> Keywords = new HashSet<string>
            {
                "if", "else", "for", "while", "do", "switch", "case", "break", "continue", "return",
                "function", "var", "let", "const", "class", "extends", "new", "this", "super",
                "import", "export", "from", "default", "async", "await", "try", "catch", "finally",
                "throw", "typeof", "instanceof", "in", "of", "true", "false", "null", "undefined"
            };

            public Color32[] Highlight(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return Array.Empty<Color32>();

                var colors = new Color32[text.Length];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = DefaultColor;

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
                        string word = text.Substring(start, pos - start);
                        if (Keywords.Contains(word))
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
        }

        private ISyntaxHighlighter _highlighter;
        private Color32[] _characterColors;
        private Color32[] _visibleCharacterColors; // Colors for visible glyphs only
        private TextElement _textElement;
        private int _indentSize = 4;
        private bool _indentUsingSpaces = true;

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

        public CodeField() : this(null, -1, false, false, default) { }

        public CodeField(string label) : this(label, -1, false, false, default) { }

        public CodeField(string label, int maxLength, bool multiline, bool isPasswordField, char maskChar)
            : base(label, maxLength, multiline, isPasswordField, maskChar)
        {
            // Default to multiline for code
            this.multiline = true;

            // Use a simple keyword highlighter by default
            _highlighter = new SimpleKeywordHighlighter();

            // Find the TextElement child
            _textElement = this.Q<TextElement>();
            if (_textElement != null)
            {
                _textElement.PostProcessTextVertices = ColorizeGlyphs;
            }

            // Refresh highlighting when value changes
            this.RegisterValueChangedCallback(evt => RefreshHighlighting());

            // Trigger initial highlighting after the element is attached
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
                // Schedule to ensure layout is complete
                schedule.Execute(() => RefreshHighlighting());
            });

            // Handle Tab key for space indentation
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // Add USS class for styling
            AddToClassList("code-field");
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Tab)
            {
                evt.StopPropagation();
                evt.PreventDefault();

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
                    evt.PreventDefault();
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
            if (_highlighter != null && !string.IsNullOrEmpty(value))
            {
                _characterColors = _highlighter.Highlight(value);
                // Build visible-only color array (skip newlines and control chars)
                _visibleCharacterColors = BuildVisibleColors(value, _characterColors);
            }
            else
            {
                _characterColors = null;
                _visibleCharacterColors = null;
            }
            _textElement?.MarkDirtyRepaint();
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
