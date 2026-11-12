// The Models tests' socket fixture (socket.glb): two boxes skinned to `root` and `arm`, painted by
// an 8 px picture of one colour, (40, 200, 90). Two places to hang things that carry no weights:
// `grip`, a bone of the skin under `arm`, and `back`, an empty node under `root` that is no bone
// of the skin. For the tests of the picture a model's file carries (ModelBridge.ModelTexture) and
// of which nodes an actor can be attached to. Ours, so it carries no licence. No packages needed:
//     node socket.mjs
import fs from "node:fs"
import path from "node:path"
import zlib from "node:zlib"

export const TEXEL = [40, 200, 90, 255]

const positions = [], normals = [], uvs = [], joints = [], weights = [], indices = []
for (const [joint, y0] of [[0, 0], [1, 1]]) {
    const base = positions.length / 3
    for (let c = 0; c < 8; c++) {
        const p = [c & 1 ? 0.25 : -0.25, y0 + (c & 2 ? 1 : 0), c & 4 ? 0.25 : -0.25]
        positions.push(...p)
        const n = [p[0], p[1] - y0 - 0.5, p[2]], l = Math.hypot(...n)
        normals.push(n[0] / l, n[1] / l, n[2] / l)
        uvs.push(0.5, 0.5)
        joints.push(joint, 0, 0, 0)
        weights.push(1, 0, 0, 0)
    }
    for (const f of [[0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4], [2, 6, 7, 3], [0, 4, 6, 2], [1, 3, 7, 5]]) {
        indices.push(base + f[0], base + f[1], base + f[2], base + f[0], base + f[2], base + f[3])
    }
}

function png(size, texel) {
    const raw = Buffer.alloc(size * (size * 4 + 1))
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) raw.set(texel, y * (size * 4 + 1) + 1 + x * 4)
    const chunk = (type, data) => {
        const body = Buffer.concat([Buffer.from(type), data])
        const out = Buffer.alloc(body.length + 8)
        out.writeUInt32BE(data.length, 0)
        body.copy(out, 4)
        out.writeUInt32BE(zlib.crc32(body) >>> 0, body.length + 4)
        return out
    }
    const head = Buffer.alloc(13)
    head.writeUInt32BE(size, 0)
    head.writeUInt32BE(size, 4)
    head.set([8, 6, 0, 0, 0], 8)
    return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", head), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))])
}

const chunks = [], views = [], accessors = []
let length = 0
function view(bytes) {
    const pad = (4 - bytes.length % 4) % 4
    views.push({ buffer: 0, byteOffset: length, byteLength: bytes.length })
    chunks.push(bytes, Buffer.alloc(pad))
    length += bytes.length + pad
    return views.length - 1
}
function accessor(data, type, componentType, extra = {}) {
    const per = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 }[type]
    accessors.push({ bufferView: view(Buffer.from(data.buffer, data.byteOffset, data.byteLength)), componentType, count: data.length / per, type, ...extra })
    return accessors.length - 1
}
const FLOAT = 5126
const aPosition = accessor(new Float32Array(positions), "VEC3", FLOAT, { min: [-0.25, 0, -0.25], max: [0.25, 2, 0.25] })
const aNormal = accessor(new Float32Array(normals), "VEC3", FLOAT)
const aUv = accessor(new Float32Array(uvs), "VEC2", FLOAT)
const aJoints = accessor(new Uint8Array(joints), "VEC4", 5121)
const aWeights = accessor(new Float32Array(weights), "VEC4", FLOAT)
const aIndices = accessor(new Uint16Array(indices), "SCALAR", 5123)
// root at the origin, arm 1 up, grip 0.5 aside and 0.5 up from arm.
const aInverse = accessor(new Float32Array([
    1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
    1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1,
    1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -0.5, -1.5, 0, 1,
]), "MAT4", FLOAT)
const imageView = view(png(8, TEXEL))

const gltf = {
    asset: { version: "2.0", generator: "OneJS Tests/Models/Fixtures~/socket.mjs" },
    scene: 0,
    scenes: [{ nodes: [0, 4] }],
    nodes: [
        { name: "root", children: [1, 3] },
        { name: "arm", translation: [0, 1, 0], children: [2] },
        { name: "grip", translation: [0.5, 0.5, 0] },
        { name: "back", translation: [0, 0.5, -0.3] },
        { name: "socket", mesh: 0, skin: 0 },
    ],
    skins: [{ joints: [0, 1, 2], inverseBindMatrices: aInverse, skeleton: 0 }],
    meshes: [{ name: "socket", primitives: [{ attributes: { POSITION: aPosition, NORMAL: aNormal, TEXCOORD_0: aUv, JOINTS_0: aJoints, WEIGHTS_0: aWeights }, indices: aIndices, material: 0 }] }],
    materials: [{ name: "socket", pbrMetallicRoughness: { baseColorTexture: { index: 0 }, metallicFactor: 0 } }],
    textures: [{ source: 0 }],
    images: [{ bufferView: imageView, mimeType: "image/png" }],
    accessors,
    bufferViews: views,
    buffers: [{ byteLength: length }],
}
const bin = Buffer.concat(chunks)
const head = (a, b, c) => { const h = Buffer.alloc(a === 0x46546c67 ? 12 : 8); h.writeUInt32LE(a, 0); h.writeUInt32LE(b, 4); if (c !== undefined) h.writeUInt32LE(c, 8); return h }
let json = Buffer.from(JSON.stringify(gltf))
json = Buffer.concat([json, Buffer.alloc((4 - json.length % 4) % 4, 0x20)])
fs.writeFileSync(path.join(import.meta.dirname, "socket.glb"), Buffer.concat([
    head(0x46546c67, 2, 12 + 8 + json.length + 8 + bin.length), head(json.length, 0x4e4f534a), json, head(bin.length, 0x004e4942), bin,
]))
