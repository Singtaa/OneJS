// node --test "Tests/WebGL~"
//
// Runs OneJSWebGL.jslib's qjs_eval in node, whose indirect eval scopes declarations exactly as a browser's does,
// so the WebGL eval path is checked without a player build. Emscripten's heap is stubbed: a "pointer" is the
// string itself, and a result is written into a plain object keyed by its out pointer.
import { test, beforeEach } from "node:test"
import assert from "node:assert/strict"
import fs from "node:fs"
import vm from "node:vm"

const source = fs.readFileSync(new URL("../../Plugins/WebGL/OneJSWebGL.jslib", import.meta.url), "utf8")
    .replace(/\{\{\{\s*makeDynCall\([^)]*\)\s*\}\}\}/g, "(function () {})")

const lib = {}
new Function("autoAddDeps", "mergeInto", "LibraryManager", source)(() => {}, (_, l) => Object.assign(lib, l), { library: {} })
globalThis.OneJS = lib.$OneJS
const out = {}
globalThis.UTF8ToString = (ptr) => ptr
globalThis.stringToUTF8 = (str, ptr) => { out[ptr] = str }

function evalIn(ctx, code) {
    const status = lib.qjs_eval(ctx, code, "app.js", 0, "out", 1 << 20)
    return { status, result: out.out }
}

// What JsHost.RegisterExport asks after the bundle has run.
const ON_PLAY = "typeof __exports !== 'undefined' && typeof __exports.onPlay === 'function'"

// esbuild's iife output for an app with a strict tsconfig: the scaffold's, so every new app.
const strictBundle = `"use strict";
var __exports = (() => {
  var helper = 1;
  function onPlay() {}
  function onStop() {}
  return { onPlay, onStop };
})();
//# sourceMappingURL=app.js.txt.map`

beforeEach(() => {
    delete globalThis.__exports
    OneJS.contexts = {}
})

test("a strict bundle's onPlay is found by the next eval", () => {
    assert.deepEqual(evalIn(1, strictBundle), { status: 0, result: "" })
    assert.equal(evalIn(1, ON_PLAY).result, "true")
})

test("a minified strict bundle's onPlay is found", () => {
    evalIn(1, `"use strict";var __exports=(()=>{function o(){}return{onPlay:o}})();`)
    assert.equal(evalIn(1, ON_PLAY).result, "true")
})

test("a sloppy bundle's onPlay is still found", () => {
    evalIn(1, strictBundle.replace(`"use strict";\n`, ""))
    assert.equal(evalIn(1, ON_PLAY).result, "true")
})

test("a strict bundle's other top-level declarations stay out of the global scope", () => {
    evalIn(1, `"use strict";\nvar __exports = (() => ({ onPlay() {} }))();\nvar notExported = 1;`)
    assert.equal(evalIn(1, "typeof notExported").result, "undefined")
})

test("an expression's value comes back unchanged", () => {
    assert.deepEqual(evalIn(1, "1 + 2"), { status: 0, result: "3" })
    assert.deepEqual(evalIn(1, ON_PLAY), { status: 0, result: "false" })
})

test("a strict bundle in an iframe realm publishes __exports in that realm, not the page's", () => {
    const realm = vm.runInContext("this", vm.createContext({}))
    OneJS.contexts[2] = { isPage: false, win: realm }
    evalIn(2, strictBundle)
    assert.equal(evalIn(2, ON_PLAY).result, "true")
    assert.equal(typeof globalThis.__exports, "undefined")
})

test("a throwing bundle still reports its error", () => {
    const { status, result } = evalIn(1, `"use strict";\nvar __exports = (() => { throw new Error("boom") })();`)
    assert.equal(status, 1)
    assert.match(result, /app\.js: boom/)
})
