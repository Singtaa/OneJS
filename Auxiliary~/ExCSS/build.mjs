#!/usr/bin/env node
/**
 * Builds Runtime/Styling/ExCSS/OneJS.ExCSS.dll and OneJS.ExCSS.pdb from src/, or checks that the
 * committed pair is exactly what src/ builds to.
 *
 *   node "Auxiliary~/ExCSS/build.mjs"          # build, verify, and write the two files
 *   node "Auxiliary~/ExCSS/build.mjs" check    # build, verify, and compare them byte for byte
 *
 * Options, for the tests: --src <folder> builds another copy of the source, --out <folder>
 * writes or compares the pair there instead of Runtime/Styling/ExCSS.
 *
 * Exit code: 0 the pair matches (or was written), 1 it differs or the verifier failed, 2 a usage
 * error or a failed build, 3 check found no committed pair, so nothing was compared.
 *
 * The build runs in a scratch copy, so nothing is written beside the source, and the bytes
 * depend on src/ alone: OneJS.ExCSS.csproj pins the compiler and maps every path to /_/. Needs
 * the .NET SDK 8 or later.
 */
import { spawnSync } from "node:child_process"
import fs from "node:fs"
import os from "node:os"
import path from "node:path"
import { fileURLToPath } from "node:url"

const HERE = path.dirname(fileURLToPath(import.meta.url))
const FILES = ["OneJS.ExCSS.dll", "OneJS.ExCSS.pdb"]

function parseArgs(argv) {
    const args = { mode: "build", src: path.join(HERE, "src"), out: path.resolve(HERE, "../../Runtime/Styling/ExCSS") }
    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === "check") args.mode = "check"
        else if (argv[i] === "--src") args.src = path.resolve(argv[++i])
        else if (argv[i] === "--out") args.out = path.resolve(argv[++i])
        else throw new Error(`Unknown argument: ${argv[i]}`)
    }
    return args
}

function dotnet(args, cwd) {
    const r = spawnSync("dotnet", args, { cwd, encoding: "utf8", maxBuffer: 64 * 1024 * 1024, shell: process.platform === "win32" })
    if (r.error) throw r.error
    return r
}

function fail(code, message) {
    console.log(message)
    process.exit(code)
}

function main() {
    let args
    try {
        args = parseArgs(process.argv.slice(2))
    } catch (e) {
        fail(2, e.message)
    }
    if (!fs.existsSync(args.src)) fail(2, `No source at ${args.src}`)

    const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "onejs-excss-"))
    try {
        const project = path.join(scratch, "OneJS.ExCSS")
        fs.mkdirSync(project)
        fs.copyFileSync(path.join(HERE, "OneJS.ExCSS.csproj"), path.join(project, "OneJS.ExCSS.csproj"))
        fs.cpSync(args.src, path.join(project, "src"), { recursive: true })
        const built = path.join(scratch, "out")
        const b = dotnet(["build", "-nologo", "-v", "q", "-o", built], project)
        if (b.status !== 0) fail(2, `The build failed:\n${b.stdout.slice(-3000)}${b.stderr.slice(-2000)}`)

        const verifier = path.join(scratch, "verify")
        fs.cpSync(path.join(HERE, "verify"), verifier, { recursive: true, filter: (p) => !/[\\/](bin|obj)$/.test(p) })
        const v = dotnet(["run", "--project", verifier, "--", ...FILES.map(f => path.join(built, f)), path.join(project, "src")], scratch)
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

main()
