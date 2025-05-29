/**
 * Default OneJS ESbuild Config
 */
import * as esbuild from "esbuild"
import { importTransformationPlugin, outputWatcherPlugin, copyAssetsPlugin, decoratorFixPlugin } from "onejs-core/scripts/esbuild/index.mjs"

const once = process.argv.includes("--once")

let ctx = await esbuild.context({
	entryPoints: ["@outputs/tsc/index.js"],
	bundle: true,
	plugins: [importTransformationPlugin(), !once && outputWatcherPlugin(), copyAssetsPlugin(), decoratorFixPlugin()].filter(Boolean),
	inject: ["onejs-core/dist/index.js"],
	platform: "node",
	sourcemap: true,
	sourceRoot: process.cwd() + "/index",
	alias: {
		"onejs": "onejs-core",
		"preact": "onejs-preact",
		"react": "onejs-preact/compat",
		"react-dom": "onejs-preact/compat"
	},
	outfile: "@outputs/esbuild/app.js",
});

if (once) {
    await ctx.rebuild()
    await ctx.dispose()
    console.log("Build finished.")
    process.exit(0)
} else {
    await ctx.watch()
    console.log("Watching for changes…")
}