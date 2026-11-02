# ExCSS source and build

`src/` is upstream ExCSS's `src/ExCSS` at release **v4.3.2** (commit `0a75db4bfffe9dc26555fd8b9ca152f6f9a2760f`), unmodified. Left out: the build files (`ExCSS.csproj`, `ExCSS.snk`, `Directory.Build.targets`) and `Properties/AssemblyInfo.cs`, which only carries COM and test-signing attributes. It builds `Runtime/Styling/ExCSS/OneJS.ExCSS.dll` and its `.pdb`; Unity never imports this folder.

```bash
node "Auxiliary~/ExCSS/build.mjs"          # build, verify, and write the DLL and PDB
node "Auxiliary~/ExCSS/build.mjs" check    # build, verify, and compare them byte for byte
node --test "Auxiliary~/ExCSS/build.test.mjs"
```

Needs Node and a network connection the first time: the build runs on its own copy of one exact .NET SDK, which it installs (see below). `check` exits 0 when the committed pair is exactly what `src/` builds to, 1 when it is not or the debug file is missing something, 3 when there is no pair to compare. CI runs it on Linux, Windows and macOS.

| File | What it does |
|------|--------------|
| `OneJS.ExCSS.csproj` | The build. `netstandard2.1` and C# 9, what Unity compiled the source against when it shipped as source in 3.9.15; no version or framework attributes, as Unity's compile had none |
| `build.mjs` | Builds in a scratch copy, so nothing is written here, with the source list sorted the same on every platform; runs the verifier, then writes or compares |
| `verify/` | Reads the DLL and PDB and fails unless the PDB is portable, every source file is embedded in it under a `/_/` path, and the DLL is marked deterministic and points at it the same way |
| `build.test.mjs` | Feeds `check` a stale DLL and a changed source, and the verifier a PDB without its source, and expects each to fail |
| `.gitattributes` | Keeps git from converting line endings, which would change the embedded source |

## Same bytes everywhere

The DLL and PDB depend on `src/` alone, so any machine can check them:

- One exact .NET SDK (`SDK` in `build.mjs`, 10.0.403 with runtime 10.0.14), installed with Microsoft's `dotnet-install` into a cache folder of its own (about 800 MB) (`~/.cache/onejs-excss`, `%LOCALAPPDATA%\onejs-excss` on Windows, or `ONEJS_EXCSS_DOTNET`). A portable PDB records the runtime its compiler ran on, and that runtime compresses the embedded source, so the machine's own .NET, which rolls forward to its newest patch, would change both files. Pinning only the compiler was not enough: CI's runners and a Windows machine each built different bytes. Changing `SDK` means rebuilding and committing the pair.
- The compiler sees the source files in one order everywhere: `build.mjs` lists them sorted by their `/`-separated paths. A glob sorts by the platform's own paths, and Windows' `\` sorts after a letter, so `StyleProperties/Border` came after `BorderImage` there and every byte after it moved.
- The build is deterministic, and every path is mapped to `/_/`. The SDK's own map is switched off (`DeterministicSourcePaths`): it maps the git root, so a copy outside git kept its real path.
- Nothing from the build machine is in either file: no user name, no checkout path, no commit hash.

## Updating ExCSS

Copy upstream's `src/ExCSS` from a release tag over `src/`, keeping the exclusions above, update the version here and in `Runtime/Styling/ExCSS/README.md`, then run `build.mjs` and commit the DLL and PDB with the source. Compare `UssCompiler`'s output on OneJS's stylesheets before and after: when the source moved into the DLL (3.9.16), every sheet compiled to the same result as 3.9.15's build from source.
