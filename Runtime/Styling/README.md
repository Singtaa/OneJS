# Styling (`OneJS.CustomStyleSheets`)

Runtime USS (Unity Style Sheets) compilation for OneJS v3.

## Purpose

Compiles USS strings into `StyleSheet` assets at runtime, bypassing Unity's asset import pipeline. `QuickJSUIBridge` owns one `UssCompiler` (rooted at the working directory) behind `compileStyleSheet()` and `loadStyleSheet()`; CSS Modules and Tailwind output embedded in the bundle go through it too.

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
| `UnityThemes/UnityDefaultRuntimeTheme.tss` | Default runtime theme: JSRunner's `_defaultThemeStylesheet`, assigned to the PanelSettings it creates |

## Usage

```csharp
var compiler = new UssCompiler(workingDirectory);
var styleSheet = ScriptableObject.CreateInstance<StyleSheet>();
compiler.Compile(styleSheet, ".my-class { color: red; padding: 10px; }");
element.styleSheets.Add(styleSheet);
// compiler.Diagnostics lists declarations UI Toolkit will ignore (unknown property, empty value)
```

## Supported Features

### Selectors
- Class selectors: `.my-class`
- ID selectors: `#my-id`
- Type selectors: `Button`, `Label`
- Pseudo-classes: `:hover`, `:active`, `:focus`
- Universal selector: `*`
- Compound selectors: `.class1.class2`
- Descendant combinators: `.parent .child`
- Child combinators: `.parent > .child`
- Selector lists: `.a, .b`

### Values
- Colors: `#fff`, `#ffffff`, `rgb(255, 0, 0)`, `rgba(255, 0, 0, 0.5)`, named colors
- Dimensions: `10px`, `50%`, `1s`, `100ms`, `45deg`, `grad`, `rad`, `turn`
- Numbers: `0`, `1.5`
- Keywords: `auto`, `none`, `initial`
- Enums: `flex-start`, `row`, `hidden`
- URLs: `url("path/to/image.png")`, loads from working directory
- Resources: `resource("path")`. Unity resource paths
- Custom properties and `var(--name)` / `var(--name, fallback)` (a `var()` can carry a font)

### Not Yet Supported
- Complex functions (`linear-gradient()`, etc.)
- `@import` rules
- Media queries

### Diagnostics

The parser is tolerant (one bad declaration never aborts the sheet), so `Compile` records what UI Toolkit will silently ignore in `Diagnostics`: property names missing from Unity's own style property table, and values that were empty or unparseable. Covered by `Tests/Editor/UssCompilerDiagnosticsTests.cs`.

## USS vs CSS Limitations

USS (Unity Style Sheets) is a subset of CSS with several limitations; onejs-unity's Tailwind generator and PostCSS plugins exist to work around them.

### Unsupported Syntax

| CSS Feature | USS Support | Workaround |
|-------------|-------------|------------|
| `rem` units | ❌ Not supported | Convert to `px` |
| `:is()` pseudo-selector | ❌ Not supported | Flatten/unwrap selectors |
| `@media` queries | ❌ Not supported | Use class-based breakpoints |
| CSS variables in `rgb()` | ❌ Not supported | Use static values |
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

The full map (also `,` `&` `>` `<` `*` `'`) is `ESCAPE_MAP` in `onejs-unity/src/tailwind/generator.mjs`.

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
- ✅ Hex colors (`#rgb`, `#rrggbb`, and with alpha, `#rgba`, `#rrggbbaa`, via `ColorUtility.TryParseHtmlString`)
- ✅ `rgb()` / `rgba()` with commas
- ✅ `var()`
- ✅ `px`, `%`, `s`, `ms`, `deg`, `grad`, `rad`, `turn` units
- ✅ `url()` for images/fonts
- ✅ Enum values (flex-direction, etc.)

What it doesn't transform (you must pre-process):
- ❌ `rem` → must convert to `px` before compiling
- ❌ Modern `rgb(r g b / a)` syntax → must use `rgba()`

### Tailwind Compatibility

- `import "onejs:tailwind"` uses onejs-unity's built-in generator (`src/tailwind/`, wired by `onejs-unity/esbuild/tailwind`): no Tailwind install, USS-safe output escaped as above, embedded in the bundle.
- For a real Tailwind/PostCSS pipeline, `onejs-unity/postcss` exports `ussTransform` (escaping, media queries to breakpoint classes, `rem` to `px`, modern colors to `rgba()`), `ussCleanup` (drops unsupported properties) and `ussUnwrapIs` (flattens `:is()`).

## Dependencies

- **ExCSS** (MIT): CSS parsing
- **Unity 6.3+**: Target platform (uses internal APIs that may change)

## Legal Notes

This is a clean-room implementation that:
1. Uses ExCSS (MIT licensed) for CSS parsing
2. Uses reflection to call Unity's public-facing internal APIs
3. Does not copy or derive from Unity source code
