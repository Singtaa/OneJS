# ExCSS (`OneJS.ExCSS`)

The CSS parser behind `UssCompiler`: [ExCSS](https://github.com/TylerBrinks/ExCSS) by Tyler Brinks, MIT (`LICENSE.txt`), upstream release **v4.3.2**, unmodified.

| File | What it is |
|------|------------|
| `OneJS.ExCSS.dll` | The parser, built from `Auxiliary~/ExCSS/src` (see the README there) |
| `OneJS.ExCSS.pdb` | Its portable debug file, with every source file embedded, so a stack trace through the parser names the file and line and a debugger can step into it |
| `OneJS.ExCSS.asmdef` | Retires this folder's old source: see below |
| `LICENSE.txt` | ExCSS's MIT license |

The DLL is referenced explicitly (`OneJS.Runtime` lists it under `precompiledReferences`; it is not auto referenced), so only OneJS sees the `ExCSS` namespace.

## Why its own name

Unity's editor ships its own fork of ExCSS as `ExCSS.Unity`, which its stylesheet importer uses for every `.uss`, `.tss` and `.uxml`. OneJS used to bundle a build of that fork with the same assembly identity, and the editor loaded OneJS's copy in place of its own. On Unity 6.6 its importer called a member the older copy lacked, and every stylesheet import failed with OneJS installed ([#154](https://github.com/Singtaa/OneJS/issues/154)). Named `OneJS.ExCSS`, the parser can never stand in for Unity's again. `Runtime/Styling/ExCSS.Unity.dll` stays as a tombstone (see the Styling README).

## Why the asmdef compiles nothing

3.9.15 shipped the parser as about 500 `.cs` files in this folder, built by this asmdef. Updating a project over it never removes them, since importing a package never deletes a file. So the asmdef stays at its path, renamed `OneJS.ExCSS.RetiredSource` so it can never clash with the DLL, with a define constraint nothing sets (`ONEJS_RETIRED_SOURCE`): whatever old source an update leaves here compiles into no assembly, and the DLL is the only `OneJS.ExCSS`. Those leftover files are safe to delete. The upgrade gate (`Tools/upgrade-gate` in the dev repo) accepts the missing scripts because of this asmdef.
