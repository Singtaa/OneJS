// The Models tests' reach fixture (reach.glb): two boxes on two joints, the upper on `arm`, with
// two one-second clips. `Reach` carries the arm 4 units to the side, far outside the mesh as it
// stands, and `Turn` turns it a quarter round. For the tests of a skinned model's fixed bounds
// and of what an unseen actor's clips do. Ours, so it carries no licence. No packages needed:
//     node reach.mjs
import fs from "node:fs"
import path from "node:path"

const positions = [], normals = [], joints = [], weights = [], indices = []
for (const [joint, y0] of [[0, 0], [1, 1]]) {
    const base = positions.length / 3
    for (let c = 0; c < 8; c++) {
        const p = [c & 1 ? 0.25 : -0.25, y0 + (c & 2 ? 1 : 0), c & 4 ? 0.25 : -0.25]
        positions.push(...p)
        const n = [p[0], p[1] - y0 - 0.5, p[2]], l = Math.hypot(...n)
        normals.push(n[0] / l, n[1] / l, n[2] / l)
        joints.push(joint, 0, 0, 0)
        weights.push(1, 0, 0, 0)
    }
    for (const f of [[0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4], [2, 6, 7, 3], [0, 4, 6, 2], [1, 3, 7, 5]]) {
        indices.push(base + f[0], base + f[1], base + f[2], base + f[0], base + f[2], base + f[3])
    }
}

const KEYS = 11
const times = Float32Array.from({ length: KEYS }, (_, k) => k / (KEYS - 1))
const reach = new Float32Array(KEYS * 3), turn = new Float32Array(KEYS * 4)
for (let k = 0; k < KEYS; k++) {
    const t = k / (KEYS - 1)
    reach.set([4 * t, 1, 0], k * 3)
    turn.set([0, 0, Math.sin(t * Math.PI / 4), Math.cos(t * Math.PI / 4)], k * 4)
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
const aPosition = accessor(new Float32Array(positions), "VEC3", FLOAT, { min: [-0.25, 0, -0.25], max: [0.25, 2, 0.25] })
const aNormal = accessor(new Float32Array(normals), "VEC3", FLOAT)
const aJoints = accessor(new Uint8Array(joints), "VEC4", 5121)
const aWeights = accessor(new Float32Array(weights), "VEC4", FLOAT)
const aIndices = accessor(new Uint16Array(indices), "SCALAR", 5123)
const aInverse = accessor(new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1]), "MAT4", FLOAT)
const aTimes = accessor(times, "SCALAR", FLOAT, { min: [0], max: [1] })
const aReach = accessor(reach, "VEC3", FLOAT)
const aTurn = accessor(turn, "VEC4", FLOAT)

const gltf = {
    asset: { version: "2.0", generator: "OneJS Tests/Models/Fixtures~/reach.mjs" },
    scene: 0,
    scenes: [{ nodes: [0, 2] }],
    nodes: [{ name: "root", children: [1] }, { name: "arm", translation: [0, 1, 0] }, { name: "reach", mesh: 0, skin: 0 }],
    skins: [{ joints: [0, 1], inverseBindMatrices: aInverse, skeleton: 0 }],
    meshes: [{ name: "reach", primitives: [{ attributes: { POSITION: aPosition, NORMAL: aNormal, JOINTS_0: aJoints, WEIGHTS_0: aWeights }, indices: aIndices, material: 0 }] }],
    materials: [{ name: "reach", pbrMetallicRoughness: { baseColorFactor: [0.8, 0.6, 0.4, 1], metallicFactor: 0 } }],
    animations: [
        { name: "Reach", samplers: [{ input: aTimes, output: aReach, interpolation: "LINEAR" }], channels: [{ sampler: 0, target: { node: 1, path: "translation" } }] },
        { name: "Turn", samplers: [{ input: aTimes, output: aTurn, interpolation: "LINEAR" }], channels: [{ sampler: 0, target: { node: 1, path: "rotation" } }] },
    ],
    accessors,
    bufferViews: views,
    buffers: [{ byteLength: length }],
}
let json = Buffer.from(JSON.stringify(gltf))
json = Buffer.concat([json, Buffer.alloc((4 - json.length % 4) % 4, 0x20)])
const bin = Buffer.concat(chunks)
const head = (a, b, c) => { const h = Buffer.alloc(a === 0x46546c67 ? 12 : 8); h.writeUInt32LE(a, 0); h.writeUInt32LE(b, 4); if (c !== undefined) h.writeUInt32LE(c, 8); return h }
fs.writeFileSync(path.join(import.meta.dirname, "reach.glb"), Buffer.concat([
    head(0x46546c67, 2, 12 + 8 + json.length + 8 + bin.length), head(json.length, 0x4e4f534a), json, head(bin.length, 0x004e4942), bin,
]))
