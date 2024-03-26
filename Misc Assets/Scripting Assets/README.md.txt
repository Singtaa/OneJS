# Command Lines

## Development Installs

`npm install -DE esbuild tiny-glob tailwindcss postcss postcss-cli`

## Running ESBuild

`node esbuild.mjs`

## Running Tailwind via PostCSS

_Set your output uss path accordinly._

`npx postcss input.css -o ../Assets/tailwind.uss --watch`

## Tailwind Compiler (Don't do this directly. Use PostCSS instead.)

`npx tailwindcss -i ./input.css -o ./output.css --watch`