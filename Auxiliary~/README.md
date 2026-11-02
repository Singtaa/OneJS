The Auxiliary~/ folder is for containing non-Unity files. It'll generally include native code and codegen stuff.

- `quickjs-unity/`: QuickJS and the native wrapper, built into `Plugins/`.
- `ExCSS/`: the CSS parser's source, built into `Runtime/Styling/ExCSS/OneJS.ExCSS.dll` and its `.pdb` (see its README).

Rider's "Reformat Code" is not working for .c files. We've setup a "clang-format" external tool for this. Current shortcut is `options+shift+c`.