[Docs](https://onejs.com/docs) - [Discord](https://discord.gg/dwnYFte6SF)

OneJS puts the full modern web stack (TypeScript, Preact, Tailwind, etc.) inside Unity so you can build runtime *and* editor UIs with instant live-reload, zero browser overhead, and performance that feels native on Windows, macOS, iOS, and Android.

## Features

* **Native UI, no webviews** – bridges straight to UI Toolkit for true in-game performance.
* **Instant iteration** – hit *Save*, see Unity refresh without domain reloads.
* **Web dev tooling** – TypeScript, JSX/Preact, Tailwind, ESBuild all pre-wired.
* **Cross-platform** – tested on desktop and mobile targets out of the box.
* **Scriptable** – expose C# safely to JavaScript for mods or rapid prototyping.

## Requirements

|              | Minimum                    | Notes                                      |
| ------------ |----------------------------| ------------------------------------------ |
| **Unity**    | 2021.3 LTS                 | 2022.1+ if you need UI Toolkit Vector API  |
| **Packages** | burst & mathematics        | auto-installed                             |
| **Tooling**  | Node ≥ 18 + TypeScript CLI | for build/watch tasks                      |

## Quick Start

### Install

You can use any **one** of the following three methods.

 * Download and import from Asset Store.
 * Unity **Package Manager → Add package by Git URL**

      ```text
      https://github.com/Singtaa/OneJS.git
      ```

 * Clone the repo anywhere on your machine, and use `Install package from disk` from Package Manager.

### Add the prefab

Drag **`ScriptEngine`** into an empty scene and press **Play**. Unity will scaffold an `App/` working directory.

### Boot the toolchain

 * Open `{ProjectDir}/App` with VSCode.
 * Run `npm run setup` in VSCode's terminal.
 * Use `Ctrl + Shift + B` or `Cmd + Shift + B` to start up all 3 watch tasks: `esbuild`, `tailwind`, and `tsc`.

### Code something

Edit `App/index.tsx`, hit *Save*, watch Unity live-reload.

For our full docs, please go here: [onejs.com/docs](https://onejs.com/docs)

## Contributing

Pull requests and issue reports are welcome!

## Community & Support

[Discord](https://discord.gg/dwnYFte6SF) is where it's at! Join the community to ask questions, share your work, and get help.

## License

Distributed under the MIT License.
