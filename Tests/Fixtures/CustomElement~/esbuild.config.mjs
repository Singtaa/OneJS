import * as esbuild from "esbuild"
import path from "path"
import { fileURLToPath } from "url"

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const reactPath = path.resolve(__dirname, "node_modules/react")
const reactJsxPath = path.resolve(__dirname, "node_modules/react/jsx-runtime")

await esbuild.build({
    entryPoints: ["index.tsx"],
    bundle: true,
    outfile: "../Resources/TestCustomElement.txt",
    format: "iife",
    globalName: "__exports",
    target: "es2022",
    jsx: "automatic",
    resolveExtensions: [".tsx", ".ts", ".jsx", ".js", ".json"],
    alias: {
        "react": reactPath,
        "react/jsx-runtime": reactJsxPath,
    },
    packages: "bundle",
})
console.log("Test fixture built!")
