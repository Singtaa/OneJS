// node --test "Auxiliary~/ExCSS/build.test.mjs": build.mjs check passes on the committed DLL and PDB and
// fails on a stale one, and the verifier fails on a PDB without its source. Each case runs a real build.
import assert from "node:assert/strict"
import { spawnSync } from "node:child_process"
import fs from "node:fs"
import os from "node:os"
import path from "node:path"
import { test } from "node:test"
import { fileURLToPath } from "node:url"

const HERE = path.dirname(fileURLToPath(import.meta.url))
const COMMITTED = path.resolve(HERE, "../../Runtime/Styling/ExCSS")
const PAIR = ["OneJS.ExCSS.dll", "OneJS.ExCSS.pdb"]

function build(...args) {
    const r = spawnSync(process.execPath, [path.join(HERE, "build.mjs"), ...args], { encoding: "utf8" })
    return { code: r.status, out: r.stdout + r.stderr }
}

function scratch() {
    return fs.mkdtempSync(path.join(os.tmpdir(), "onejs-excss-test-"))
}

test("the committed DLL and PDB are exactly what the source builds to", () => {
    const { code, out } = build("check")
    assert.equal(code, 0, out)
    assert.match(out, /503 of 503 source files/)
})

test("a DLL that differs by one byte fails the check", () => {
    const dir = scratch()
    try {
        for (const f of PAIR) fs.copyFileSync(path.join(COMMITTED, f), path.join(dir, f))
        const dll = fs.readFileSync(path.join(dir, PAIR[0]))
        dll[dll.length - 1] ^= 0xff
        fs.writeFileSync(path.join(dir, PAIR[0]), dll)
        const { code, out } = build("check", "--out", dir)
        assert.equal(code, 1, out)
        assert.match(out, /OneJS\.ExCSS\.dll in .* is not what the source builds to/)
    } finally {
        fs.rmSync(dir, { recursive: true, force: true })
    }
})

test("a change to the source that the committed DLL lacks fails the check", () => {
    const dir = scratch()
    try {
        const src = path.join(dir, "src")
        fs.cpSync(path.join(HERE, "src"), src, { recursive: true })
        fs.appendFileSync(path.join(src, "Values", "Length.cs"), "\n// a change nobody rebuilt\n")
        const { code, out } = build("check", "--src", src)
        assert.equal(code, 1, out)
        assert.match(out, /OneJS\.ExCSS\.dll and OneJS\.ExCSS\.pdb in .* are not what the source builds to/)
    } finally {
        fs.rmSync(dir, { recursive: true, force: true })
    }
})

test("no committed pair is reported as nothing compared", () => {
    const dir = scratch()
    try {
        const { code, out } = build("check", "--out", dir)
        assert.equal(code, 3, out)
    } finally {
        fs.rmSync(dir, { recursive: true, force: true })
    }
})

test("the verifier fails on a PDB without its source", () => {
    const dir = scratch()
    try {
        const project = path.join(dir, "OneJS.ExCSS")
        fs.mkdirSync(project)
        fs.copyFileSync(path.join(HERE, "OneJS.ExCSS.csproj"), path.join(project, "OneJS.ExCSS.csproj"))
        fs.cpSync(path.join(HERE, "src"), path.join(project, "src"), { recursive: true })
        const shell = process.platform === "win32"
        const b = spawnSync("dotnet", ["build", "-nologo", "-v", "q", "-o", path.join(dir, "out"), "-p:EmbedAllSources=false"], { cwd: project, encoding: "utf8", shell })
        assert.equal(b.status, 0, b.stdout)
        const v = spawnSync("dotnet", ["run", "--project", path.join(HERE, "verify"), "--", ...PAIR.map(f => path.join(dir, "out", f)), path.join(project, "src")], { encoding: "utf8", shell })
        assert.equal(v.status, 1, v.stdout)
        assert.match(v.stdout, /its source is not embedded/)
    } finally {
        fs.rmSync(dir, { recursive: true, force: true })
        fs.rmSync(path.join(HERE, "verify", "bin"), { recursive: true, force: true })
        fs.rmSync(path.join(HERE, "verify", "obj"), { recursive: true, force: true })
    }
})
