# OneJS Controls

Custom UI Toolkit controls for OneJS applications.

## CodeField

A `TextField` with built-in syntax highlighting support. Uses UI Toolkit's `PostProcessTextVertices` callback to colorize individual glyphs without affecting cursor positioning or text editing.

### Features

- **Per-glyph coloring** via vertex tint modification
- **Correct cursor positioning** - colors are applied at render time, not via rich text tags
- **Pluggable highlighters** - implement `ISyntaxHighlighter` for custom languages
- **Built-in JavaScript highlighter** - keywords, strings, numbers, comments
- **Tab-to-spaces indentation** - Tab inserts spaces, Shift+Tab dedents
- **Multi-line indent/dedent** - Select multiple lines and Tab/Shift+Tab to indent/dedent all

### Usage

```csharp
using OneJS;
using UnityEngine.UIElements;

// Basic usage with default JavaScript highlighter
var codeField = new CodeField();
codeField.value = "const x = 42;";

// Custom highlighter
codeField.Highlighter = new MyCustomHighlighter();

// Disable highlighting
codeField.Highlighter = null;

// Configure indentation
codeField.IndentUsingSpaces = true;  // true = spaces (default), false = tab character
codeField.IndentSize = 2;            // number of spaces when IndentUsingSpaces is true
```

### Indentation

CodeField handles Tab key specially for code editing:

| Property | Default | Description |
|----------|---------|-------------|
| `IndentUsingSpaces` | `true` | When true, Tab inserts spaces. When false, inserts tab character. |
| `IndentSize` | `4` | Number of spaces to insert (only applies when `IndentUsingSpaces` is true) |

| Key | Action |
|-----|--------|
| Tab | Insert indent (spaces or tab based on settings) |
| Tab (with selection) | Indent all selected lines |
| Shift+Tab | Remove leading whitespace (tab or up to `IndentSize` spaces) |
| Shift+Tab (with selection) | Dedent all selected lines |

### Custom Highlighter

Implement `CodeField.ISyntaxHighlighter`:

```csharp
public class MyHighlighter : CodeField.ISyntaxHighlighter
{
    public Color32[] Highlight(string text)
    {
        var colors = new Color32[text.Length];
        // Fill colors array based on syntax analysis
        // Each index corresponds to a character in the text
        return colors;
    }
}
```

### Architecture

```
Text Input → ISyntaxHighlighter.Highlight() → Color32[] per character
                                                      ↓
                                          BuildVisibleColors()
                                          (filter invisible chars)
                                                      ↓
PostProcessTextVertices callback ← Color32[] for visible glyphs only
         ↓
   Modify vertex.tint for each glyph quad (4 vertices)
         ↓
   Rendered text with syntax highlighting
```

### Test Window

Open **Window > OneJS > CodeField Test** to see the control in action.

### JS Interoperability (Future)

The highlighter can run in JavaScript, returning token spans that are converted to colors in C#. This enables using existing JS syntax highlighting libraries.
