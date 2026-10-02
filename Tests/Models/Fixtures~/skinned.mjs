// The Models tests' skinned fixture (skinned.glb): a box on two joints with vertex colours, UVs and
// one blend shape, which is the shape glTFast lays out in four vertex streams. Ours, so it carries no licence.
// Regenerate with the @gltf-transform/core package: node skinned.mjs skinned.glb
import { Document, NodeIO } from "@gltf-transform/core"
const doc = new Document()
const buffer = doc.createBuffer()
const p = [], n = [], uv = [], col = [], joints = [], weights = [], idx = []
const faces = [[[1,0,0],[0,1,0],[0,0,1]],[[-1,0,0],[0,1,0],[0,0,-1]],[[0,1,0],[0,0,1],[1,0,0]],[[0,-1,0],[0,0,-1],[1,0,0]],[[0,0,1],[0,1,0],[-1,0,0]],[[0,0,-1],[0,1,0],[1,0,0]]]
for (const [nn, up, right] of faces) {
    const base = p.length / 3
    for (const [u, v] of [[0,0],[1,0],[1,1],[0,1]]) {
        const pos = [0, 1, 2].map(k => 0.5 * nn[k] + (u - 0.5) * right[k] + (v - 0.5) * up[k])
        p.push(pos[0], pos[1] + 0.5, pos[2])
        n.push(...nn); uv.push(u, 1 - v)
        col.push(u, v, 0.5, 1)
        // The top half follows the second joint.
        const top = pos[1] > 0 ? 1 : 0
        joints.push(top, 0, 0, 0); weights.push(1, 0, 0, 0)
    }
    const cr = [right[1]*up[2]-right[2]*up[1], right[2]*up[0]-right[0]*up[2], right[0]*up[1]-right[1]*up[0]]
    const ccw = cr[0]*nn[0]+cr[1]*nn[1]+cr[2]*nn[2] > 0
    idx.push(...(ccw ? [base, base+1, base+2, base, base+2, base+3] : [base, base+2, base+1, base, base+3, base+2]))
}
const acc = (arr, type, T) => doc.createAccessor().setArray(new T(arr)).setType(type).setBuffer(buffer)
const prim = doc.createPrimitive()
    .setAttribute("POSITION", acc(p, "VEC3", Float32Array)).setAttribute("NORMAL", acc(n, "VEC3", Float32Array))
    .setAttribute("TEXCOORD_0", acc(uv, "VEC2", Float32Array)).setAttribute("COLOR_0", acc(col, "VEC4", Float32Array))
    .setAttribute("JOINTS_0", acc(joints, "VEC4", Uint16Array)).setAttribute("WEIGHTS_0", acc(weights, "VEC4", Float32Array))
    .setIndices(acc(idx, "SCALAR", Uint16Array))
    .setMaterial(doc.createMaterial("Skin").setBaseColorFactor([0.9, 0.9, 0.9, 1]))
// "Grow" pushes every vertex out along its normal by a quarter.
prim.addTarget(doc.createPrimitiveTarget("Grow").setAttribute("POSITION", acc(n.map((v) => v * 0.25), "VEC3", Float32Array)))
const root = doc.createNode("Root")
const tip = doc.createNode("Tip").setTranslation([0, 0.5, 0])
root.addChild(tip)
const skin = doc.createSkin("Skin").addJoint(root).addJoint(tip)
    .setInverseBindMatrices(acc([1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1, 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,-0.5,0,1], "MAT4", Float32Array))
const body = doc.createNode("Body").setMesh(doc.createMesh("Body").addPrimitive(prim).setWeights([0])).setSkin(skin)
doc.getRoot().setDefaultScene(doc.createScene("Scene").addChild(root).addChild(body))
const times = acc([0, 1], "SCALAR", Float32Array)
const rot = acc([0, 0, 0, 1, 0, 0, 0.3826834, 0.9238795], "VEC4", Float32Array)
const sampler = doc.createAnimationSampler().setInput(times).setOutput(rot).setInterpolation("LINEAR")
doc.createAnimation("bend").addSampler(sampler).addChannel(doc.createAnimationChannel().setTargetNode(tip).setTargetPath("rotation").setSampler(sampler))
await new NodeIO().write(process.argv[2], doc)
