# ExCSS (`OneJS.ExCSS`)

The CSS parser behind `UssCompiler`: [ExCSS](https://github.com/TylerBrinks/ExCSS) by Tyler Brinks, MIT (`LICENSE.txt`).

This folder is upstream's `src/ExCSS` at release **v4.3.2** (commit `0a75db4bfffe9dc26555fd8b9ca152f6f9a2760f`), unmodified. Left out: the build files (`ExCSS.csproj`, `ExCSS.snk`, `Directory.Build.targets`) and `Properties/AssemblyInfo.cs`, which only carries COM and test-signing attributes.

## Why it is source, under its own name

Unity's editor ships its own fork of ExCSS as `ExCSS.Unity`, which its stylesheet importer uses for every `.uss`, `.tss` and `.uxml`. OneJS used to bundle a build of that fork with the same assembly identity (`ExCSS.Unity` 0.1.0.0), and the editor loaded OneJS's copy in place of its own. While the two builds matched nobody noticed; Unity 6.6 updated its fork, its importer called a member the older copy lacked, and every stylesheet import failed with OneJS installed ([#154](https://github.com/Singtaa/OneJS/issues/154)).

Built from source as `OneJS.ExCSS`, the parser can never stand in for Unity's again, and what it is can be read. `autoReferenced` is off, so only `OneJS.Runtime` sees the `ExCSS` namespace.

`Runtime/Styling/ExCSS.Unity.dll` stays as a tombstone (see the Styling README).

## Updating

Copy upstream's `src/ExCSS` from a release tag over this folder, keeping the exclusions above, and update the version here. Then compare `UssCompiler`'s output on OneJS's stylesheets before and after, as the change that brought this in did: the parse trees matched the old build on every sheet OneJS ships, and differed only on `calc()` inside a shorthand, which USS has no use for.
