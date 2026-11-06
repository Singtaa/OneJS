// The Models tests' fixture (offset-wall.glb): a 2 m square in glTF's x-y plane under a node at
// glTF x = +3. glTFast mirrors glTF's x into Unity's, so it must render at Unity x = -3, which is
// what onejs-play's physics3d/gltf.ts assumes when it builds a mesh collider from the same file.
// Ours, so it carries no licence. Regenerate with no dependencies: node offset-wall.mjs offset-wall.glb
import fs from "node:fs"
const align4 = (n) => Math.ceil(n / 4) * 4
const pos = new Uint8Array(new Float32Array([-1, 0, 0, 1, 0, 0, 1, 2, 0, -1, 2, 0]).buffer)
const idx = new Uint8Array(new Uint16Array([0, 1, 2, 0, 2, 3]).buffer)
const bin = new Uint8Array(align4(pos.length) + align4(idx.length))
bin.set(pos, 0)
bin.set(idx, align4(pos.length))
const json = {
    asset: { version: "2.0" }, scene: 0, scenes: [{ nodes: [0] }],
    nodes: [{ name: "Offset", children: [1], translation: [3, 0, 0] }, { name: "Wall", mesh: 0 }],
    meshes: [{ primitives: [{ attributes: { POSITION: 0 }, indices: 1 }] }],
    buffers: [{ byteLength: bin.length }],
    bufferViews: [{ buffer: 0, byteOffset: 0, byteLength: pos.length }, { buffer: 0, byteOffset: align4(pos.length), byteLength: idx.length }],
    accessors: [
        { bufferView: 0, componentType: 5126, count: 4, type: "VEC3", min: [-1, 0, 0], max: [1, 2, 0] },
        { bufferView: 1, componentType: 5123, count: 6, type: "SCALAR" },
    ],
}
const text = new TextEncoder().encode(JSON.stringify(json))
const head = new Uint8Array(align4(text.length)).fill(0x20)
head.set(text)
const out = new Uint8Array(12 + 8 + head.length + 8 + bin.length)
const v = new DataView(out.buffer)
v.setUint32(0, 0x46546c67, true); v.setUint32(4, 2, true); v.setUint32(8, out.length, true)
v.setUint32(12, head.length, true); v.setUint32(16, 0x4e4f534a, true); out.set(head, 20)
v.setUint32(20 + head.length, bin.length, true); v.setUint32(24 + head.length, 0x004e4942, true); out.set(bin, 28 + head.length)
fs.writeFileSync(process.argv[2], out)
