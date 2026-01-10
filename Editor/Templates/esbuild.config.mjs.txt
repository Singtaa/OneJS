import * as esbuild from "esbuild"
import fs from "fs"
import path from "path"
import { fileURLToPath } from "url"

// Import plugins from onejs-unity
import { importTransformPlugin, tailwindPlugin } from "onejs-unity/esbuild"
// Optional: USS Modules support (scoped class names)
// import { ussModulesPlugin } from "onejs-unity/esbuild"

const __dirname = path.dirname(fileURLToPath(import.meta.url))

// Resolve react to the App's node_modules to prevent duplicate copies
const reactPath = path.resolve(__dirname, "node_modules/react")
const reactJsxPath = path.resolve(__dirname, "node_modules/react/jsx-runtime")
const reactJsxDevPath = path.resolve(__dirname, "node_modules/react/jsx-dev-runtime")

const isWatch = process.argv.includes("--watch")

const config = {
    entryPoints: ["index.tsx"],
    bundle: true,
    outfile: "../app.js.txt",
    format: "esm",
    target: "es2022",
    jsx: "automatic",
    // Generate source maps for better error messages
    sourcemap: true,
    alias: {
        // Force all react imports to use the App's copy
        "react": reactPath,
        "react/jsx-runtime": reactJsxPath,
        "react/jsx-dev-runtime": reactJsxDevPath,
    },
    // Ensure packages from node_modules are bundled, not externalized
    packages: "bundle",
    plugins: [
        // Transform `import { X } from "UnityEngine"` to `const { X } = CS.UnityEngine`
        importTransformPlugin(),
        // Tailwind support - use `import "onejs:tailwind"` to activate
        tailwindPlugin({ content: ["./**/*.{tsx,ts,jsx,js}"] }),
        // Optional: USS Modules support (scoped class names)
        // ussModulesPlugin({ generateTypes: true }),
    ],
}

// Rename sourcemap from app.js.txt.map to app.js.map.txt (Unity TextAsset)
function renameSourceMap() {
    const oldPath = path.resolve(__dirname, "../app.js.txt.map")
    const newPath = path.resolve(__dirname, "../app.js.map.txt")
    if (fs.existsSync(oldPath)) {
        fs.renameSync(oldPath, newPath)
    }
}

if (isWatch) {
    const ctx = await esbuild.context({
        ...config,
        plugins: [
            ...(config.plugins || []),
            {
                name: "rename-sourcemap",
                setup(build) {
                    build.onEnd(() => renameSourceMap())
                }
            }
        ]
    })
    await ctx.watch()
    console.log("Watching for changes...")
} else {
    await esbuild.build(config)
    renameSourceMap()
    console.log("Build complete!")
}
