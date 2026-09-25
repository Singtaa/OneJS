/**
 * Compiled shader language programs, drawn on Unity's own graphics device.
 *
 * Unity cannot compile a shader in a built player, which is why the SL VM
 * exists. A browser can: onejs-unity prints every program as WGSL and GLSL ES
 * 3.00 at build time (`src/sl/web.ts`), and this host compiles whichever one
 * the device speaks and draws it straight into the RenderTexture a
 * ShaderEffectElement shows. Same pixels as the VM within 1/255, and on the
 * spike's measurements 100 to 600 times cheaper per pixel, with no register or
 * instruction budget.
 *
 * WHY IT LIVES HERE, IN THE FRAMEWORK CLOSURE. Drawing needs Unity's WebGL2
 * context or WebGPU device and the table that turns a texture's native pointer
 * into a GL or GPU texture. Everything in this object is private to Unity's
 * framework closure: nothing is put on globalThis, so game code (and in Play,
 * the game sandbox) can reach none of it. The only way in is the four C#
 * entry points in `Runtime/SL/SLWeb.cs`.
 *
 * Three private handles are read, and all three are checked at startup by
 * `OneJS_SLWeb_Check` (`SLWeb.Describe()`), so a Unity upgrade that renames one
 * turns into "unavailable, drawing on the VM" rather than a broken picture:
 *
 *   GLctx, GL.textures   Emscripten's WebGL2 context and texture name table;
 *                        GetNativeTexturePtr() is an index into the table
 *   Module.WebGPU.device Unity's GPUDevice (set by Unity's WebGPU.js)
 *   wgpu                 lib_webgpu's object table, where GetNativeTexturePtr()
 *                        indexes on WebGPU (measured, not documented)
 *
 * None of them is declared as a dependency: a build without WebGPU has no
 * `wgpu`, and a missing dependency would fail that build's link rather than
 * report "unavailable" at runtime.
 *
 * Draw returns 1 when it drew, 0 when the program is not ready yet (the caller
 * draws the VM this frame), and -1 when it never will be (compile error, lost
 * handle); the caller then stays on the VM and says why once.
 */
var OneJSSLWebLibrary = {
    $OJSL: {
        programs: {},
        next: 1,
        // Created on first use: this object is serialised into the framework
        // by Emscripten, so it holds only functions and plain values.
        views: null,
        checked: null,
        samplers: {},
        glSamplers: {},
        fbo: null,
        vao: null,
        vs: null,
        data: null,
        loggedFail: {},
        // Cleared only by the negative control (SLWeb.SetRestoreGLState).
        restore: true,

        gl: function () {
            return typeof GLctx !== "undefined" && GLctx ? GLctx : null
        },
        device: function () {
            var w = Module["WebGPU"]
            return w && w.device ? w.device : null
        },
        glTexture: function (id) {
            return typeof GL !== "undefined" && GL.textures ? (GL.textures[id] || null) : null
        },
        gpuTexture: function (id) {
            if (typeof wgpu === "undefined") return null
            var t = wgpu[id]
            return typeof GPUTexture !== "undefined" && t instanceof GPUTexture ? t : null
        },

        fail: function (p, message) {
            p.failed = message
            if (!OJSL.loggedFail[p.id]) {
                OJSL.loggedFail[p.id] = true
                console.warn("[OneJS sl] compiled program " + p.id + " failed, so it will not draw compiled: " + message)
            }
            return -1
        },

        // Unity's TextureWrapMode (Repeat, Clamp, Mirror, MirrorOnce) and
        // FilterMode (Point, Bilinear, Trilinear), from the texture itself, so
        // a compiled program samples exactly as the VM's material does.
        gpuSampler: function (dev, wrapU, wrapV, filter) {
            var key = wrapU + "," + wrapV + "," + filter
            var s = OJSL.samplers[key]
            if (s) return s
            var mode = function (w) { return w === 0 ? "repeat" : w === 2 ? "mirror-repeat" : "clamp-to-edge" }
            s = dev.createSampler({
                addressModeU: mode(wrapU), addressModeV: mode(wrapV),
                magFilter: filter === 0 ? "nearest" : "linear",
                minFilter: filter === 0 ? "nearest" : "linear",
                mipmapFilter: filter === 2 ? "linear" : "nearest",
            })
            OJSL.samplers[key] = s
            return s
        },
        glSampler: function (gl, wrapU, wrapV, filter, mips) {
            var key = wrapU + "," + wrapV + "," + filter + "," + mips
            var s = OJSL.glSamplers[key]
            if (s) return s
            var mode = function (w) { return w === 0 ? gl.REPEAT : w === 2 ? gl.MIRRORED_REPEAT : gl.CLAMP_TO_EDGE }
            s = gl.createSampler()
            gl.samplerParameteri(s, gl.TEXTURE_WRAP_S, mode(wrapU))
            gl.samplerParameteri(s, gl.TEXTURE_WRAP_T, mode(wrapV))
            gl.samplerParameteri(s, gl.TEXTURE_MAG_FILTER, filter === 0 ? gl.NEAREST : gl.LINEAR)
            // A mip filter on a texture with one level makes it incomplete,
            // which samples black, so mips are only asked for when they exist.
            var min = filter === 0 ? gl.NEAREST : gl.LINEAR
            if (mips) min = filter === 0 ? gl.NEAREST_MIPMAP_NEAREST : filter === 1 ? gl.LINEAR_MIPMAP_NEAREST : gl.LINEAR_MIPMAP_LINEAR
            gl.samplerParameteri(s, gl.TEXTURE_MIN_FILTER, min)
            OJSL.glSamplers[key] = s
            return s
        },

        // ------------------------------------------------------------ WebGL2
        // Unity caches GL state and never re-reads it, so every piece of state
        // this touches is saved first and put back after. A missed one shows
        // up as some unrelated Unity draw going wrong a frame later.
        glCompile: function (gl, glsl) {
            var ext = gl.getExtension("KHR_parallel_shader_compile")
            if (!OJSL.vs) {
                OJSL.vs = gl.createShader(gl.VERTEX_SHADER)
                gl.shaderSource(OJSL.vs, "#version 300 es\n" +
                    "void main() {\n" +
                    "    vec2 c = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));\n" +
                    "    gl_Position = vec4(c * 2.0 - 1.0, 0.0, 1.0);\n" +
                    "}\n")
                gl.compileShader(OJSL.vs)
            }
            var fs = gl.createShader(gl.FRAGMENT_SHADER)
            gl.shaderSource(fs, glsl)
            gl.compileShader(fs)
            var prog = gl.createProgram()
            gl.attachShader(prog, OJSL.vs)
            gl.attachShader(prog, fs)
            gl.linkProgram(prog)
            return { prog: prog, fs: fs, ext: ext, loc: null }
        },
        glReady: function (gl, p) {
            var g = p.gl
            // With parallel compile, asking for LINK_STATUS before completion
            // blocks; the caller draws the VM until the driver is done.
            if (g.ext && !gl.getProgramParameter(g.prog, g.ext.COMPLETION_STATUS_KHR)) return 0
            if (!gl.getProgramParameter(g.prog, gl.LINK_STATUS)) {
                var log = gl.getShaderInfoLog(g.fs) || gl.getProgramInfoLog(g.prog) || "link failed"
                return OJSL.fail(p, "GLSL: " + log.trim().slice(0, 400))
            }
            g.loc = {
                res: gl.getUniformLocation(g.prog, "sl_Res"),
                opt: gl.getUniformLocation(g.prog, "sl_Opt"),
                u: gl.getUniformLocation(g.prog, "sl_U"),
                tex: [],
            }
            for (var i = 0; i < 16; i++) g.loc.tex.push(gl.getUniformLocation(g.prog, "sl_Tex" + i))
            return 1
        },
        glDraw: function (gl, p, target, w, h, secs, linear, uniforms, tex) {
            var g = p.gl
            if (!g.loc) {
                var r = OJSL.glReady(gl, p)
                if (r !== 1) return r
            }
            var rt = OJSL.glTexture(target)
            if (!rt) return OJSL.fail(p, "the target has no GL texture")
            if (!OJSL.fbo) OJSL.fbo = gl.createFramebuffer()
            if (!OJSL.vao) OJSL.vao = gl.createVertexArray()

            var caps = [gl.SCISSOR_TEST, gl.BLEND, gl.DEPTH_TEST, gl.CULL_FACE, gl.STENCIL_TEST, gl.RASTERIZER_DISCARD,
                gl.POLYGON_OFFSET_FILL, gl.SAMPLE_ALPHA_TO_COVERAGE, gl.SAMPLE_COVERAGE]
            var restore = OJSL.restore
            var s = !restore ? null : {
                fbD: gl.getParameter(gl.DRAW_FRAMEBUFFER_BINDING), fbR: gl.getParameter(gl.READ_FRAMEBUFFER_BINDING),
                prog: gl.getParameter(gl.CURRENT_PROGRAM), vao: gl.getParameter(gl.VERTEX_ARRAY_BINDING),
                vp: gl.getParameter(gl.VIEWPORT), cm: gl.getParameter(gl.COLOR_WRITEMASK),
                at: gl.getParameter(gl.ACTIVE_TEXTURE), en: [], units: [],
            }
            var c, u
            if (restore) {
                for (c = 0; c < caps.length; c++) s.en.push(gl.isEnabled(caps[c]))
                for (u = 0; u < tex.length; u++) {
                    gl.activeTexture(gl.TEXTURE0 + u)
                    s.units.push([gl.getParameter(gl.TEXTURE_BINDING_2D), gl.getParameter(gl.SAMPLER_BINDING)])
                }
            }

            var ok = 1
            gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, OJSL.fbo)
            gl.framebufferTexture2D(gl.DRAW_FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, rt, 0)
            if (!OJSL.checked) OJSL.checked = new WeakSet()
            if (!OJSL.checked.has(rt)) {
                var st = gl.checkFramebufferStatus(gl.DRAW_FRAMEBUFFER)
                if (st !== gl.FRAMEBUFFER_COMPLETE) ok = OJSL.fail(p, "the target is not renderable (0x" + st.toString(16) + ")")
                else OJSL.checked.add(rt)
            }
            if (ok === 1) {
                gl.viewport(0, 0, w, h)
                for (c = 0; c < caps.length; c++) gl.disable(caps[c])
                gl.colorMask(true, true, true, true)
                gl.useProgram(g.prog)
                gl.bindVertexArray(OJSL.vao)
                gl.uniform4f(g.loc.res, w, h, secs, 0)
                gl.uniform4f(g.loc.opt, linear, 0, 0, 0)
                if (g.loc.u) gl.uniform4fv(g.loc.u, uniforms)
                for (u = 0; u < tex.length; u++) {
                    var t = tex[u]
                    var gt = t ? OJSL.glTexture(t[0]) : null
                    gl.activeTexture(gl.TEXTURE0 + u)
                    gl.bindTexture(gl.TEXTURE_2D, gt)
                    gl.bindSampler(u, t ? OJSL.glSampler(gl, t[1], t[2], t[3], t[4]) : null)
                    if (g.loc.tex[u]) gl.uniform1i(g.loc.tex[u], u)
                }
                gl.drawArrays(gl.TRIANGLES, 0, 3)
            }
            gl.framebufferTexture2D(gl.DRAW_FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, null, 0)
            if (!restore) return ok

            for (u = 0; u < s.units.length; u++) {
                gl.activeTexture(gl.TEXTURE0 + u)
                gl.bindTexture(gl.TEXTURE_2D, s.units[u][0])
                gl.bindSampler(u, s.units[u][1])
            }
            gl.activeTexture(s.at)
            gl.bindVertexArray(s.vao)
            gl.useProgram(s.prog)
            gl.colorMask(s.cm[0], s.cm[1], s.cm[2], s.cm[3])
            for (c = 0; c < caps.length; c++) s.en[c] ? gl.enable(caps[c]) : gl.disable(caps[c])
            gl.viewport(s.vp[0], s.vp[1], s.vp[2], s.vp[3])
            gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, s.fbD)
            gl.bindFramebuffer(gl.READ_FRAMEBUFFER, s.fbR)
            return ok
        },

        // ------------------------------------------------------------ WebGPU
        // Nothing to save: a pass on Unity's device with its own encoder, and
        // submitted before Unity submits its frame, so the UI samples this
        // frame's picture.
        gpuCompile: function (dev, p, wgsl) {
            var g = { module: dev.createShaderModule({ code: wgsl, label: "sl " + p.id }), ready: 0, pipelines: {}, buf: null, slots: [] }
            // The emitter declares only the texture slots the program samples,
            // and the automatic layout has exactly those bindings, so they are
            // read back from the source rather than assumed from the C# side.
            var re = /var sl_tex(\d+)\s*:/g, m
            while ((m = re.exec(wgsl)) !== null) g.slots.push(+m[1])
            g.module.getCompilationInfo().then(function (info) {
                var errs = info.messages.filter(function (m) { return m.type === "error" })
                if (errs.length) {
                    OJSL.fail(p, "WGSL: " + errs.map(function (m) { return m.lineNum + ":" + m.linePos + " " + m.message }).join("; ").slice(0, 400))
                } else {
                    g.ready = 1
                }
            }, function (e) { OJSL.fail(p, "WGSL: " + e) })
            return g
        },
        gpuDraw: function (dev, p, target, w, h, secs, linear, uniforms, tex) {
            var g = p.gpu
            if (!g.ready) return 0
            var rt = OJSL.gpuTexture(target)
            if (!rt) return OJSL.fail(p, "the target has no GPU texture")
            var format = rt.format
            var pl = g.pipelines[format]
            if (pl === undefined) {
                // Built off the frame, per target format (sRGB in a Linear
                // project, plain in a Gamma one); the VM draws until it lands.
                g.pipelines[format] = null
                dev.createRenderPipelineAsync({
                    label: "sl " + p.id,
                    layout: "auto",
                    vertex: { module: g.module, entryPoint: "sl_vs" },
                    fragment: { module: g.module, entryPoint: "sl_fs", targets: [{ format: format }] },
                    primitive: { topology: "triangle-list" },
                }).then(function (pipeline) { g.pipelines[format] = pipeline },
                    function (e) { OJSL.fail(p, "pipeline: " + (e && e.message ? e.message : e)) })
                return 0
            }
            if (pl === null) return 0

            if (!g.buf) g.buf = dev.createBuffer({ size: 72 * 4, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST })
            if (!OJSL.data) OJSL.data = new Float32Array(72)
            var d = OJSL.data
            d[0] = w; d[1] = h; d[2] = secs; d[3] = 0
            d[4] = linear; d[5] = 0; d[6] = 0; d[7] = 0
            d.set(uniforms, 8)
            dev.queue.writeBuffer(g.buf, 0, d)

            if (!OJSL.views) OJSL.views = new WeakMap()
            var entries = [{ binding: 0, resource: { buffer: g.buf } }]
            for (var k = 0; k < g.slots.length; k++) {
                var u = g.slots[k]
                var t = u < tex.length ? tex[u] : null
                var gt = t ? OJSL.gpuTexture(t[0]) : null
                if (!gt) return OJSL.fail(p, "texture slot " + u + " has no GPU texture")
                var view = OJSL.views.get(gt)
                if (!view) { view = gt.createView(); OJSL.views.set(gt, view) }
                entries.push({ binding: 1 + 2 * u, resource: OJSL.gpuSampler(dev, t[1], t[2], t[3]) })
                entries.push({ binding: 2 + 2 * u, resource: view })
            }
            var target_ = OJSL.views.get(rt)
            if (!target_) { target_ = rt.createView(); OJSL.views.set(rt, target_) }

            // The first draw into each target is checked, so a bind group or
            // pass that does not validate falls back rather than logging an
            // uncaptured error every frame.
            var verify = g.verified !== rt
            if (verify) dev.pushErrorScope("validation")
            var enc = dev.createCommandEncoder({ label: "sl " + p.id })
            var pass = enc.beginRenderPass({ colorAttachments: [{ view: target_, loadOp: "clear", storeOp: "store", clearValue: [0, 0, 0, 0] }] })
            pass.setPipeline(pl)
            pass.setBindGroup(0, dev.createBindGroup({ layout: pl.getBindGroupLayout(0), entries: entries }))
            pass.draw(3)
            pass.end()
            dev.queue.submit([enc.finish()])
            if (verify) {
                g.verified = rt
                dev.popErrorScope().then(function (err) { if (err) OJSL.fail(p, "draw: " + err.message) })
            }
            return 1
        },
    },

    /**
     * The startup handle check: can this page draw into `nativeId`, a
     * RenderTexture's GetNativeTexturePtr()? Returns "webgpu ok <format>",
     * "webgl2 ok", or "unavailable: <why>".
     */
    OneJS_SLWeb_Check__deps: ["$OJSL"],
    OneJS_SLWeb_Check: function (nativeId, webgpu) {
        var r
        try {
            if (webgpu) {
                var dev = OJSL.device()
                var t = OJSL.gpuTexture(nativeId)
                if (!dev || !(dev instanceof GPUDevice)) r = "unavailable: Module.WebGPU.device is not a GPUDevice"
                else if (typeof wgpu === "undefined") r = "unavailable: no wgpu table in this build"
                else if (!t) r = "unavailable: wgpu[" + nativeId + "] is " + (wgpu[nativeId] ? wgpu[nativeId].constructor.name : String(wgpu[nativeId])) + ", not a GPUTexture"
                else if (!(t.usage & GPUTextureUsage.RENDER_ATTACHMENT)) r = "unavailable: a RenderTexture is not a render attachment (usage " + t.usage + ")"
                else r = "webgpu ok " + t.format
            } else {
                var gl = OJSL.gl()
                var gt = OJSL.glTexture(nativeId)
                if (!gl || !(gl instanceof WebGL2RenderingContext)) r = "unavailable: GLctx is not a WebGL2 context"
                else if (!gt || !(gt instanceof WebGLTexture)) r = "unavailable: GL.textures[" + nativeId + "] is not a WebGLTexture"
                else r = "webgl2 ok"
            }
        } catch (e) {
            r = "unavailable: " + e
        }
        var len = lengthBytesUTF8(r) + 1
        var ptr = _malloc(len)
        stringToUTF8(r, ptr, len)
        return ptr
    },

    /** Compiles a program for the current device. 0 when there is no device. */
    OneJS_SLWeb_Create__deps: ["$OJSL"],
    OneJS_SLWeb_Create: function (wgslPtr, glslPtr, webgpu) {
        var p = { id: OJSL.next++, failed: null, gl: null, gpu: null }
        try {
            if (webgpu) {
                var dev = OJSL.device()
                if (!dev) return 0
                p.gpu = OJSL.gpuCompile(dev, p, UTF8ToString(wgslPtr))
            } else {
                var gl = OJSL.gl()
                if (!gl) return 0
                p.gl = OJSL.glCompile(gl, UTF8ToString(glslPtr))
            }
        } catch (e) {
            OJSL.fail(p, String(e))
        }
        OJSL.programs[p.id] = p
        return p.id
    },

    /**
     * Draws program `id` into the RenderTexture whose native pointer is
     * `target`. `uniforms` is 64 floats (16 slots of vec4); `textures` is 5
     * ints per slot: native pointer (0 for none), wrapU, wrapV, filter, has
     * mips.
     */
    OneJS_SLWeb_Draw__deps: ["$OJSL"],
    OneJS_SLWeb_Draw: function (id, target, w, h, secs, linear, uniformsPtr, texturesPtr, textureCount) {
        var p = OJSL.programs[id]
        if (!p) return -1
        if (p.failed) return -1
        try {
            var uniforms = HEAPF32.subarray(uniformsPtr >> 2, (uniformsPtr >> 2) + 64)
            var tex = []
            for (var i = 0; i < textureCount; i++) {
                var o = (texturesPtr >> 2) + i * 5
                tex.push(HEAP32[o] === 0 ? null : [HEAP32[o], HEAP32[o + 1], HEAP32[o + 2], HEAP32[o + 3], HEAP32[o + 4]])
            }
            if (p.gpu) {
                var dev = OJSL.device()
                return dev ? OJSL.gpuDraw(dev, p, target, w, h, secs, linear, uniforms, tex) : OJSL.fail(p, "the WebGPU device is gone")
            }
            var gl = OJSL.gl()
            return gl ? OJSL.glDraw(gl, p, target, w, h, secs, linear, uniforms, tex) : OJSL.fail(p, "the WebGL2 context is gone")
        } catch (e) {
            return OJSL.fail(p, String(e && e.message ? e.message : e))
        }
    },

    /**
     * The negative control: with restore off, a WebGL2 draw leaves Unity's GL
     * state as it set it, and Unity's own frame must visibly break. A harness
     * whose checks still pass then is blind to a missing restore.
     */
    OneJS_SLWeb_SetRestore__deps: ["$OJSL"],
    OneJS_SLWeb_SetRestore: function (on) {
        OJSL.restore = !!on
    },

    OneJS_SLWeb_Release__deps: ["$OJSL"],
    OneJS_SLWeb_Release: function (id) {
        var p = OJSL.programs[id]
        if (!p) return
        delete OJSL.programs[id]
        delete OJSL.loggedFail[id]
        var gl = p.gl ? OJSL.gl() : null
        if (gl) { gl.deleteProgram(p.gl.prog); gl.deleteShader(p.gl.fs) }
        if (p.gpu && p.gpu.buf) p.gpu.buf.destroy()
    },
}

mergeInto(LibraryManager.library, OneJSSLWebLibrary)
