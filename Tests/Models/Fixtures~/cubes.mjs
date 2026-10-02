// The Models tests' fixture (cubes.glb): two cubes, the glTF material features ModelLit carries, one clip.
// Ours, so it carries no licence. Regenerate with the @gltf-transform/core package: node cubes.mjs cubes.glb
import { Document, NodeIO } from "@gltf-transform/core"
const doc = new Document()
const buffer = doc.createBuffer()
const p = [], n = [], uv = [], idx = []
const faces = [[[1,0,0],[0,1,0],[0,0,1]],[[-1,0,0],[0,1,0],[0,0,-1]],[[0,1,0],[0,0,1],[1,0,0]],[[0,-1,0],[0,0,-1],[1,0,0]],[[0,0,1],[0,1,0],[-1,0,0]],[[0,0,-1],[0,1,0],[1,0,0]]]
for (const [nn, up, right] of faces) {
    const base = p.length / 3
    for (const [u, v] of [[0,0],[1,0],[1,1],[0,1]]) {
        for (let k = 0; k < 3; k++) p.push(0.5 * nn[k] + (u - 0.5) * right[k] + (v - 0.5) * up[k])
        n.push(...nn); uv.push(u, 1 - v)
    }
    // Wind counter-clockwise seen from outside.
    const c = [0,1,2].map(k => nn[k]); const cr = [right[1]*up[2]-right[2]*up[1], right[2]*up[0]-right[0]*up[2], right[0]*up[1]-right[1]*up[0]]
    const ccw = cr[0]*c[0]+cr[1]*c[1]+cr[2]*c[2] > 0
    idx.push(...(ccw ? [base, base+1, base+2, base, base+2, base+3] : [base, base+2, base+1, base, base+3, base+2]))
}
const acc = (arr, type, T) => doc.createAccessor().setArray(new T(arr)).setType(type).setBuffer(buffer)
const prim = (mat) => doc.createPrimitive()
    .setAttribute("POSITION", acc(p, "VEC3", Float32Array)).setAttribute("NORMAL", acc(n, "VEC3", Float32Array))
    .setAttribute("TEXCOORD_0", acc(uv, "VEC2", Float32Array)).setIndices(acc(idx, "SCALAR", Uint16Array)).setMaterial(mat)
const metal = doc.createMaterial("Metal").setBaseColorFactor([0.8, 0.2, 0.1, 1]).setMetallicFactor(0.25).setRoughnessFactor(0.75)
    .setEmissiveFactor([1, 0.5, 0]).setAlphaMode("MASK").setAlphaCutoff(0.3).setDoubleSided(true)
const glass = doc.createMaterial("Glass").setBaseColorFactor([0.2, 0.4, 1, 0.5]).setAlphaMode("BLEND")
const a = doc.createNode("A").setMesh(doc.createMesh("A").addPrimitive(prim(metal)))
const b = doc.createNode("B").setMesh(doc.createMesh("B").addPrimitive(prim(glass))).setTranslation([0, 1, 0])
doc.getRoot().setDefaultScene(doc.createScene("Scene").addChild(a).addChild(b))
const times = acc([0, 1], "SCALAR", Float32Array)
const rot = acc([0, 0, 0, 1, 0, 0.7071068, 0, 0.7071068], "VEC4", Float32Array)
const sampler = doc.createAnimationSampler().setInput(times).setOutput(rot).setInterpolation("LINEAR")
doc.createAnimation("spin").addSampler(sampler).addChannel(doc.createAnimationChannel().setTargetNode(a).setTargetPath("rotation").setSampler(sampler))
await new NodeIO().write(process.argv[2], doc)
