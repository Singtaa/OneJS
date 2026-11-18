// The Models tests' scaled-root fixture (scaled-root.glb): a skinned body under an armature that is
// scaled to a third, moved and turned, the way Blender writes a rig scaled to size (PrimoEden's wolf,
// 18 Nov 2026: drawn 0.9 m tall under an armature scaled 0.337). The skin's node takes no part in
// where it is drawn, as glTF has it: each joint's inverse bind matrix undoes the armature and the
// joint, so the vertices are where the body stands. That is a box 0.4 wide, 0.9 tall and 1 long, its
// lower half on `hips` and its upper half on `head`, which stands turned. For the test that a skinned
// model is measured as it is drawn. Ours, so it carries no licence. No packages needed:
//     node scaled-root.mjs
import fs from "node:fs"
import path from "node:path"

// Column-major 4x4 matrices, as glTF stores them.
function compose(t = [0, 0, 0], q = [0, 0, 0, 1], s = [1, 1, 1]) {
    const [x, y, z, w] = q
    const r = [
        1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w),
        2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w),
        2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y),
    ]
    return [r[0] * s[0], r[1] * s[0], r[2] * s[0], 0, r[3] * s[1], r[4] * s[1], r[5] * s[1], 0, r[6] * s[2], r[7] * s[2], r[8] * s[2], 0, t[0], t[1], t[2], 1]
}
function multiply(a, b) {
    const out = new Array(16).fill(0)
    for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) out[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k]
    return out
}
// An affine matrix's inverse: the 3x3's inverse, and the translation taken back through it.
function invert(m) {
    const [a, b, c, d, e, f, g, h, i] = [m[0], m[4], m[8], m[1], m[5], m[9], m[2], m[6], m[10]]
    const det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)
    const n = [(e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det, (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det, (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det]
    const t = [m[12], m[13], m[14]]
    const back = [0, 1, 2].map((r) => -(n[r * 3] * t[0] + n[r * 3 + 1] * t[1] + n[r * 3 + 2] * t[2]))
    return [n[0], n[3], n[6], 0, n[1], n[4], n[7], 0, n[2], n[5], n[8], 0, back[0], back[1], back[2], 1]
}

const third = 1 / 3
const turn = (axis, degrees) => { const h = degrees * Math.PI / 360, q = [0, 0, 0, Math.cos(h)]; q[axis] = Math.sin(h); return q }
const nodes = [
    { name: "armature", translation: [0.1, 0.02, -0.36], rotation: turn(1, 90), scale: [third, third, third], children: [1, 2] },
    { name: "body", mesh: 0, skin: 0 },
    { name: "hips", translation: [0, 1.2, 0], children: [3] },
    { name: "head", translation: [0, 1.4, 0.3], rotation: turn(0, 30) },
]
const local = (n) => compose(n.translation, n.rotation, n.scale)
const hips = multiply(local(nodes[0]), local(nodes[2]))
const head = multiply(hips, local(nodes[3]))

const positions = [], normals = [], joints = [], weights = [], indices = []
for (const [joint, y0] of [[0, 0], [1, 0.45]]) {
    const base = positions.length / 3
    for (let c = 0; c < 8; c++) {
        const p = [c & 1 ? 0.2 : -0.2, y0 + (c & 2 ? 0.45 : 0), c & 4 ? 0.4 : -0.6]
        positions.push(...p)
        const n = [p[0], p[1] - y0 - 0.225, p[2] + 0.1], l = Math.hypot(...n)
        normals.push(n[0] / l, n[1] / l, n[2] / l)
        joints.push(joint, 0, 0, 0)
        weights.push(1, 0, 0, 0)
    }
    for (const f of [[0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4], [2, 6, 7, 3], [0, 4, 6, 2], [1, 3, 7, 5]]) {
        indices.push(base + f[0], base + f[1], base + f[2], base + f[0], base + f[2], base + f[3])
    }
}

const chunks = [], views = [], accessors = []
let length = 0
function accessor(data, type, componentType, extra = {}) {
    const bytes = Buffer.from(data.buffer, data.byteOffset, data.byteLength)
    const pad = (4 - bytes.length % 4) % 4
    views.push({ buffer: 0, byteOffset: length, byteLength: bytes.length })
    chunks.push(bytes, Buffer.alloc(pad))
    length += bytes.length + pad
    const per = { SCALAR: 1, VEC3: 3, VEC4: 4, MAT4: 16 }[type]
    accessors.push({ bufferView: views.length - 1, componentType, count: data.length / per, type, ...extra })
    return accessors.length - 1
}
const FLOAT = 5126
const aPosition = accessor(new Float32Array(positions), "VEC3", FLOAT, { min: [-0.2, 0, -0.6], max: [0.2, 0.9, 0.4] })
const aNormal = accessor(new Float32Array(normals), "VEC3", FLOAT)
const aJoints = accessor(new Uint8Array(joints), "VEC4", 5121)
const aWeights = accessor(new Float32Array(weights), "VEC4", FLOAT)
const aIndices = accessor(new Uint16Array(indices), "SCALAR", 5123)
const aInverse = accessor(new Float32Array([...invert(hips), ...invert(head)]), "MAT4", FLOAT)

const gltf = {
    asset: { version: "2.0", generator: "OneJS Tests/Models/Fixtures~/scaled-root.mjs" },
    scene: 0,
    scenes: [{ nodes: [0] }],
    nodes,
    skins: [{ joints: [2, 3], inverseBindMatrices: aInverse, skeleton: 2 }],
    meshes: [{ name: "body", primitives: [{ attributes: { POSITION: aPosition, NORMAL: aNormal, JOINTS_0: aJoints, WEIGHTS_0: aWeights }, indices: aIndices, material: 0 }] }],
    materials: [{ name: "body", pbrMetallicRoughness: { baseColorFactor: [0.6, 0.5, 0.4, 1], metallicFactor: 0 } }],
    accessors,
    bufferViews: views,
    buffers: [{ byteLength: length }],
}
const bin = Buffer.concat(chunks)
const head8 = (a, b, c) => { const h = Buffer.alloc(c === undefined ? 8 : 12); h.writeUInt32LE(a, 0); h.writeUInt32LE(b, 4); if (c !== undefined) h.writeUInt32LE(c, 8); return h }
let json = Buffer.from(JSON.stringify(gltf))
json = Buffer.concat([json, Buffer.alloc((4 - json.length % 4) % 4, 0x20)])
fs.writeFileSync(path.join(import.meta.dirname, "scaled-root.glb"), Buffer.concat([
    head8(0x46546c67, 2, 12 + 8 + json.length + 8 + bin.length), head8(json.length, 0x4e4f534a), json, head8(bin.length, 0x004e4942), bin,
]))
