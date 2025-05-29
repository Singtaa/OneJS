const tailwindcss = require('tailwindcss')
const ussTransformPlugin = require('onejs-core/scripts/postcss/uss-transform-plugin.cjs')
const cleanupPlugin = require('onejs-core/scripts/postcss/cleanup-plugin.cjs')
const optionalImportPlugin = require('onejs-core/scripts/postcss/optional-import-plugin.cjs')
const loggingPlugin = require('onejs-core/scripts/postcss/tailwind-logging-plugin.cjs')
const unwrapIsPlugin = require('onejs-core/scripts/postcss/unwrap-is-plugin.cjs')

module.exports = {
    plugins: [
        loggingPlugin(),
        optionalImportPlugin(),
        tailwindcss('./tailwind.config.js'),
        ussTransformPlugin(),
        cleanupPlugin({ remove: ["line-height:", ".grid"] }),
        unwrapIsPlugin(),
    ]
}