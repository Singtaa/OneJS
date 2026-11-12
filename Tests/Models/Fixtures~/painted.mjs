// The Models tests' vertex colour fixture (painted.glb): two unit quads that share one
// white material, "Painted" at x = -1 with a red COLOR_0 on every vertex and "Plain" at x = 1 with
// none. Ours, so it carries no licence. No dependencies: node painted.mjs painted.glb
import { writeFileSync } from "node:fs"

const quad = (x) => [x - 0.5, -0.5, 0, x - 0.5, 0.5, 0, x + 0.5, 0.5, 0, x + 0.5, -0.5, 0]
const parts = [
    new Float32Array(quad(-1)), new Float32Array(quad(1)),
    new Float32Array([0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1]),
    new Float32Array([1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1]),
    new Uint16Array([0, 1, 2, 0, 2, 3]),
]
const views = [], chunks = []
let offset = 0
for (const p of parts) {
    const bytes = new Uint8Array(p.buffer)
    views.push({ buffer: 0, byteOffset: offset, byteLength: bytes.length })
    chunks.push(bytes, new Uint8Array((4 - (bytes.length % 4)) % 4))
    offset += bytes.length + ((4 - (bytes.length % 4)) % 4)
}
const bin = Buffer.concat(chunks)
const json = {
    asset: { version: "2.0" },
    buffers: [{ byteLength: bin.length }],
    bufferViews: views,
    accessors: [
        { bufferView: 0, componentType: 5126, count: 4, type: "VEC3", min: [-1.5, -0.5, 0], max: [-0.5, 0.5, 0] },
        { bufferView: 1, componentType: 5126, count: 4, type: "VEC3", min: [0.5, -0.5, 0], max: [1.5, 0.5, 0] },
        { bufferView: 2, componentType: 5126, count: 4, type: "VEC3" },
        { bufferView: 3, componentType: 5126, count: 4, type: "VEC4" },
        { bufferView: 4, componentType: 5123, count: 6, type: "SCALAR" },
    ],
    materials: [{ name: "White", pbrMetallicRoughness: { baseColorFactor: [1, 1, 1, 1], metallicFactor: 0 }, doubleSided: true }],
    meshes: [
        { name: "Painted", primitives: [{ attributes: { POSITION: 0, NORMAL: 2, COLOR_0: 3 }, indices: 4, material: 0 }] },
        { name: "Plain", primitives: [{ attributes: { POSITION: 1, NORMAL: 2 }, indices: 4, material: 0 }] },
    ],
    nodes: [{ name: "Painted", mesh: 0 }, { name: "Plain", mesh: 1 }],
    scenes: [{ nodes: [0, 1] }],
    scene: 0,
}
const text = Buffer.from(JSON.stringify(json))
const jsonChunk = Buffer.concat([text, Buffer.alloc((4 - (text.length % 4)) % 4, 0x20)])
const header = Buffer.alloc(12), jh = Buffer.alloc(8), bh = Buffer.alloc(8)
header.writeUInt32LE(0x46546c67, 0); header.writeUInt32LE(2, 4); header.writeUInt32LE(12 + 8 + jsonChunk.length + 8 + bin.length, 8)
jh.writeUInt32LE(jsonChunk.length, 0); jh.writeUInt32LE(0x4e4f534a, 4)
bh.writeUInt32LE(bin.length, 0); bh.writeUInt32LE(0x004e4942, 4)
writeFileSync(process.argv[2], Buffer.concat([header, jh, jsonChunk, bh, bin]))
