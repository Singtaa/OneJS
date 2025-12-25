# CustomStyleSheets

Runtime USS (Unity Style Sheets) compilation for OneJS v3.

## Purpose

Allows parsing and compiling USS strings into `StyleSheet` assets at runtime, bypassing Unity's asset import pipeline.

## Architecture

```
USS String
    ↓
ExCSS.Parse() → ExCSS.Stylesheet (MIT licensed CSS parser)
    ↓
UssCompiler (clean-room implementation)
    ↓
StyleSheetBuilderWrapper (reflection to Unity internals)
    ↓
UnityEngine.UIElements.StyleSheet asset
```

## Files

| File | Description |
|------|-------------|
| `ExCSS.Unity.dll` | Third-party CSS parser (MIT license) |
| `StyleSheetBuilderWrapper.cs` | Reflection wrapper for Unity's internal `StyleSheetBuilder` |
| `UssCompiler.cs` | Main compiler that translates ExCSS AST to StyleSheetBuilder calls |

## Usage

```csharp
var compiler = new UssCompiler(workingDirectory);
var styleSheet = ScriptableObject.CreateInstance<StyleSheet>();
compiler.Compile(styleSheet, ".my-class { color: red; padding: 10px; }");
element.styleSheets.Add(styleSheet);
```

## Supported Features

### Selectors
- Class selectors: `.my-class`
- ID selectors: `#my-id`
- Type selectors: `Button`, `Label`
- Pseudo-classes: `:hover`, `:active`, `:focus`
- Compound selectors: `.class1.class2`
- Descendant combinators: `.parent .child`
- Child combinators: `.parent > .child`
- Selector lists: `.a, .b`

### Values
- Colors: `#fff`, `#ffffff`, `rgb(255, 0, 0)`, `rgba(255, 0, 0, 0.5)`, named colors
- Dimensions: `10px`, `50%`, `1s`, `100ms`, `45deg`
- Numbers: `0`, `1.5`
- Keywords: `auto`, `none`, `initial`
- Enums: `flex-start`, `row`, `hidden`
- URLs: `url("path/to/image.png")` - loads from working directory
- Resources: `resource("path")` - Unity resource paths

### Not Yet Supported
- CSS variables (`var(--custom-prop)`)
- Complex functions (`linear-gradient()`, etc.)
- `@import` rules
- Media queries

## USS vs CSS Limitations

USS (Unity Style Sheets) is a subset of CSS with several limitations. These findings are based on the postcss transforms used in onejs-core for Tailwind compatibility.

### Unsupported Syntax

| CSS Feature | USS Support | Workaround |
|-------------|-------------|------------|
| `rem` units | ❌ Not supported | Convert to `px` |
| `:is()` pseudo-selector | ❌ Not supported | Flatten/unwrap selectors |
| `@media` queries | ❌ Not supported | Use class-based breakpoints |
| CSS variables in `rgb()` | ❌ Not supported | Use static values |
| 8-digit hex (`#RRGGBBAA`) | ⚠️ Limited | Convert to `rgba()` |
| Modern `rgb(r g b / a)` | ❌ Not supported | Use `rgba(r, g, b, a)` |

### Selector Character Restrictions

USS class names cannot contain certain characters. If using Tailwind or similar, these must be escaped:

| Character | Escape Sequence |
|-----------|-----------------|
| `.` | `_d_` |
| `#` | `_n_` |
| `%` | `_p_` |
| `:` | `_c_` |
| `/` | `_s_` |
| `[` `]` | `_lb_` `_rb_` |
| `(` `)` | `_lp_` `_rp_` |

### Unity-Specific Properties

USS supports Unity-specific properties with `-unity-` prefix:

```css
.my-element {
    -unity-background-scale-mode: scale-and-crop;  /* scale-to-fit, scale-to-fill */
    -unity-font-style: bold;                       /* normal, italic, bold-and-italic */
    -unity-text-align: middle-center;              /* upper/middle/lower + left/center/right */
    -unity-font-definition: url("path/to/font.ttf");
}
```

### What Our Compiler Handles

The `UssCompiler` currently handles:
- ✅ Standard hex colors (`#fff`, `#ffffff`)
- ✅ `rgba()` function
- ✅ `px`, `%`, `s`, `ms`, `deg` units
- ✅ `url()` for images/fonts
- ✅ Enum values (flex-direction, etc.)

What it doesn't transform (you must pre-process):
- ❌ `rem` → must convert to `px` before compiling
- ❌ 8-digit hex → must convert to `rgba()`
- ❌ Modern `rgb()` syntax → must use `rgba()`

### Tailwind Compatibility

For Tailwind CSS usage, see the postcss plugins in `onejs-core/scripts/postcss/`:
- `uss-transform-plugin.cjs` - Handles color and media query transforms
- `cleanup-plugin.cjs` - Removes unsupported properties
- `unwrap-is-plugin.cjs` - Flattens `:is()` selectors
- `onejs-tw-config.cjs` - USS-compatible Tailwind config

## Dependencies

- **ExCSS** (MIT): CSS parsing
- **Unity 6+**: Target platform (uses internal APIs that may change)

## Legal Notes

This is a clean-room implementation that:
1. Uses ExCSS (MIT licensed) for CSS parsing
2. Uses reflection to call Unity's public-facing internal APIs
3. Does not copy or derive from Unity source code
