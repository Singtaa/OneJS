#!/usr/bin/env node
/**
 * Builds Runtime/Styling/ExCSS/OneJS.ExCSS.dll and OneJS.ExCSS.pdb from src/, or checks that the
 * committed pair is exactly what src/ builds to.
 *
 *   node "Auxiliary~/ExCSS/build.mjs"          # build, verify, and write the two files
 *   node "Auxiliary~/ExCSS/build.mjs" check    # build, verify, and compare them byte for byte
 *
 * Options, for the tests: --src <folder> builds another copy of the source, --out <folder>
 * writes or compares the pair there instead of Runtime/Styling/ExCSS, --property Name=Value
 * passes a build property.
 *
 * Exit code: 0 the pair matches (or was written), 1 it differs or the verifier failed, 2 a usage
 * error or a failed build, 3 check found no committed pair, so nothing was compared.
 *
 * The build runs in a scratch copy, so nothing is written beside the source, and the bytes
 * depend on src/ alone. OneJS.ExCSS.csproj makes the build deterministic and maps every path to
 * /_/. This script makes it run on one exact .NET SDK, SDK below, installed into a cache folder
 * of its own the first time (Microsoft's dotnet-install script; 650 to 800 MB on disk): a portable PDB
 * records the version of the runtime the compiler ran on, and that runtime compresses the
 * embedded source, so a machine's own .NET, which rolls forward to its newest patch, would
 * change both files. ONEJS_EXCSS_DOTNET names another folder for that install.
 */
import { spawnSync } from "node:child_process"
import fs from "node:fs"
import os from "node:os"
import path from "node:path"
import { fileURLToPath } from "node:url"

const HERE = path.dirname(fileURLToPath(import.meta.url))
const FILES = ["OneJS.ExCSS.dll", "OneJS.ExCSS.pdb"]
// The SDK the committed pair was built with. Changing it changes both files: rebuild and commit them.
const SDK = "10.0.403"
const WINDOWS = process.platform === "win32"

function parseArgs(argv) {
    const args = { mode: "build", src: path.join(HERE, "src"), out: path.resolve(HERE, "../../Runtime/Styling/ExCSS"), properties: [] }
    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === "check") args.mode = "check"
        else if (argv[i] === "--src") args.src = path.resolve(argv[++i])
        else if (argv[i] === "--out") args.out = path.resolve(argv[++i])
        else if (argv[i] === "--property") args.properties.push(argv[++i])
        else throw new Error(`Unknown argument: ${argv[i]}`)
    }
    return args
}

function cacheRoot() {
    if (process.env.ONEJS_EXCSS_DOTNET) return path.resolve(process.env.ONEJS_EXCSS_DOTNET)
    const base = WINDOWS ? (process.env.LOCALAPPDATA || os.tmpdir()) : (process.env.XDG_CACHE_HOME || path.join(os.homedir(), ".cache"))
    return path.join(base, "onejs-excss", `dotnet-sdk-${SDK}`)
}

/** The private .NET install, made the first time: only SDK and the one runtime it ships with. */
async function privateDotnet(scratch) {
    const root = cacheRoot()
    const exe = path.join(root, WINDOWS ? "dotnet.exe" : "dotnet")
    if (fs.existsSync(path.join(root, "sdk", SDK))) return exe
    console.log(`Installing the .NET SDK ${SDK} into ${root} (once)`)
    const script = path.join(scratch, WINDOWS ? "dotnet-install.ps1" : "dotnet-install.sh")
    const response = await fetch(`https://dot.net/v1/${path.basename(script)}`)
    if (!response.ok) fail(2, `Could not download ${path.basename(script)}: ${response.status}`)
    fs.writeFileSync(script, Buffer.from(await response.arrayBuffer()))
    const r = WINDOWS
        ? spawnSync("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Version", SDK, "-InstallDir", root, "-NoPath"], { encoding: "utf8" })
        : spawnSync("bash", [script, "--version", SDK, "--install-dir", root, "--no-path"], { encoding: "utf8" })
    if (r.status !== 0 || !fs.existsSync(path.join(root, "sdk", SDK))) fail(2, `Installing the .NET SDK ${SDK} failed:\n${(r.stdout || "").slice(-2000)}${(r.stderr || "").slice(-2000)}`)
    return exe
}

/**
 * Every .cs under src, as MSBuild items in ordinal order of their /-separated paths: the order the
 * compiler sees them in, so the same on every platform.
 */
function sourcesProps(src) {
    const files = fs.readdirSync(src, { recursive: true }).map(f => f.split(path.sep).join("/")).filter(f => f.endsWith(".cs"))
    files.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0))
    const items = files.map(f => `    <Compile Include="src/${f}" />`).join("\n")
    return `<Project>\n  <ItemGroup>\n${items}\n  </ItemGroup>\n</Project>\n`
}

function runDotnet(exe, args, cwd) {
    const env = {
        ...process.env,
        DOTNET_ROOT: path.dirname(exe),
        DOTNET_MULTILEVEL_LOOKUP: "0",
        DOTNET_CLI_TELEMETRY_OPTOUT: "1",
        DOTNET_NOLOGO: "1",
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
        MSBUILDDISABLENODEREUSE: "1",
    }
    const r = spawnSync(exe, args, { cwd, env, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 })
    if (r.error) throw r.error
    return r
}

function fail(code, message) {
    console.log(message)
    process.exit(code)
}

async function main() {
    let args
    try {
        args = parseArgs(process.argv.slice(2))
    } catch (e) {
        fail(2, e.message)
    }
    if (!fs.existsSync(args.src)) fail(2, `No source at ${args.src}`)

    const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "onejs-excss-"))
    try {
        const dotnet = await privateDotnet(scratch)
        fs.writeFileSync(path.join(scratch, "global.json"), JSON.stringify({ sdk: { version: SDK, rollForward: "disable" } }))
        const project = path.join(scratch, "OneJS.ExCSS")
        fs.mkdirSync(project)
        fs.copyFileSync(path.join(HERE, "OneJS.ExCSS.csproj"), path.join(project, "OneJS.ExCSS.csproj"))
        fs.cpSync(args.src, path.join(project, "src"), { recursive: true })
        fs.writeFileSync(path.join(project, "Sources.props"), sourcesProps(path.join(project, "src")))
        const built = path.join(scratch, "out")
        const b = runDotnet(dotnet, ["build", "-nologo", "-v", "q", "--disable-build-servers", "-p:UseSharedCompilation=false", ...args.properties.map(p => `-p:${p}`), "-o", built], project)
        if (b.status !== 0) fail(2, `The build failed:\n${b.stdout.slice(-3000)}${b.stderr.slice(-2000)}`)

        const verifier = path.join(scratch, "verify")
        fs.cpSync(path.join(HERE, "verify"), verifier, { recursive: true, filter: (p) => !/[\\/](bin|obj)$/.test(p) })
        const v = runDotnet(dotnet, ["run", "--project", verifier, "--disable-build-servers", "--", ...FILES.map(f => path.join(built, f)), path.join(project, "src")], scratch)
        process.stdout.write(v.stdout)
        if (v.status !== 0) fail(1, "FAIL: the built DLL or PDB is missing what a stack trace needs")

        if (args.mode === "build") {
            fs.mkdirSync(args.out, { recursive: true })
            for (const f of FILES) fs.copyFileSync(path.join(built, f), path.join(args.out, f))
            console.log(`Wrote ${FILES.join(" and ")} to ${args.out}`)
            return
        }

        const missing = FILES.filter(f => !fs.existsSync(path.join(args.out, f)))
        if (missing.length === FILES.length) fail(3, `No committed ${FILES.join(" or ")} in ${args.out}: nothing was compared`)
        const differ = FILES.filter(f => missing.includes(f) || !fs.readFileSync(path.join(built, f)).equals(fs.readFileSync(path.join(args.out, f))))
        if (differ.length) fail(1, `FAIL: ${differ.join(" and ")} in ${args.out} ${differ.length === 1 ? "is" : "are"} not what the source builds to. Run node "Auxiliary~/ExCSS/build.mjs" and commit the result.`)
        console.log(`PASS: ${FILES.join(" and ")} are exactly what the source builds to`)
    } finally {
        fs.rmSync(scratch, { recursive: true, force: true })
    }
}

await main()
