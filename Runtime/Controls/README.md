# OneJS Controls

Custom UI Toolkit controls for OneJS applications.

## CodeField

A `TextField` with built-in syntax highlighting support. Uses UI Toolkit's `PostProcessTextVertices` callback to colorize individual glyphs without affecting cursor positioning or text editing.

### Features

- **Per-glyph coloring** via vertex tint modification
- **Correct cursor positioning** - colors are applied at render time, not via rich text tags
- **Pluggable highlighters** - implement `ISyntaxHighlighter` for custom languages
- **Built-in JavaScript highlighter** - keywords, strings, numbers, comments

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
```

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
