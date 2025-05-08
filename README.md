<img src="https://onejs.com/images/OneJS_Banner.jpg">

📄 [Docs](https://onejs.com/docs) · 💬 [Discord](https://discord.gg/dwnYFte6SF)

<br />

OneJS brings the full JS ecosystem into Unity, so you can build snappy, modern UIs with live reload and zero browser overhead.

<br />

## ✨ Features

* 🧬 **Native UI, no webviews** – bridges straight to UI Toolkit for true in-game performance.
* ⚡ **Instant iteration** – hit *Save*, see Unity refresh without domain reloads.
* 🛠️ **Web dev tooling** – TypeScript, JSX/Preact, Tailwind, ESBuild all pre-wired.
* 📱 **Cross-platform** – tested on desktop and mobile targets out of the box.
* 🧠 **Scriptable** – expose C# safely to JavaScript for mods or rapid prototyping.

<br />

## 📋 Requirements

|                 | Minimum                    | Notes                                             |
|-----------------|----------------------------|---------------------------------------------------|
| 🎮 **Unity**    | 2021.3 LTS                 | 2022.1+ if you need UI Toolkit Vector API         |
| 📦 **Packages** | burst & mathematics        | auto-installed                                    |
| 🛠 **Tooling**  | Node ≥ 18 + TypeScript CLI | for build/watch tasks                             |

<br />

## 🚀 Quick Start

### Install

You can use any **one** of the following three methods:

* Download and import from Asset Store.
* Unity **Package Manager → Add package by Git URL** `https://github.com/Singtaa/OneJS.git`
* Clone the repo anywhere on your machine, and use `Install package from disk` from Package Manager.

### Add the prefab

Drag the **`ScriptEngine`** prefab into an empty scene and press **Play ▶️**. OneJS will scaffold an `App/` working directory.

### Boot the toolchain

* Open `{ProjectDir}/App` with VSCode.
* Run `npm run setup` in VSCode's terminal.
* Use `Ctrl + Shift + B` or `Cmd + Shift + B` to start up all 3 watch tasks: `esbuild`, `tailwind`, and `tsc`.

### Code something

Edit `index.tsx`, hit *Save*, watch Unity live-reload 🔄.

Please visit [onejs.com/docs](https://onejs.com/docs/getting-started) for proper documentation.

<br />

## 🤝 Contributing

Pull requests and issue reports are welcome! [Contributing Docs](CONTRIBUTING.md)

<br />

## 🌐 Community & Support

💬 [Discord](https://discord.gg/dwnYFte6SF) is where it's at! Join the community to ask questions, share your work, and get help.

<br />

## 📄 License

Distributed under the MIT License.
