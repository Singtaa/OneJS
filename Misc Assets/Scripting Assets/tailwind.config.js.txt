const plugin = require("tailwindcss/plugin")
const { theme, paths, plugins: onejsPlugins, corePlugins } = require('onejs-core/scripts/postcss/onejs-tw-config.cjs')

module.exports = {
    content: [...paths, "./@outputs/esbuild/**/*.js", "./comps/**/*.css"],
    theme: theme,
    important: ".root", // Used for the added selector specificity
    plugins: [...onejsPlugins, plugin(function ({ addUtilities }) {
        addUtilities({
            // These are some default colors for the OneJS Headless Components
            // such as Select, Toggle, Slider, etc
            ".default-bg-color": { "background-color": "white" },
            ".accented-bg-color": { "background-color": "#fde047" },
            ".hover-bg-color": { "background-color": "rgb(0 0 0 / 0.1)" },
            ".default-text-color": { "color": "#4b5563" },
            ".active-text-color": { "color": "#cd8c06" },
            ".highlighted-text-color": { "color": "#854d0e" },
        })
    })],
    corePlugins: corePlugins,
}