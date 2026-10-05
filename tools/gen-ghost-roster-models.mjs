// `tools/gen-ghost-roster-models.mjs` · 生成**男女双性别**的鬼怪模型集。
//
// ## 用户规则（2026-10-05 明令，已钉长期记忆）
// 「鬼的建模是通用的，不与鬼的类型绑定……所有的鬼都随机几个建模（**分男女建模**），
//   只是按机制体现不同而已」→ 所以：
//   · 模型与鬼类型**完全解耦**（配置里只写"模型池"，绝不写"类型→模型"）
//   · 每只鬼开局从池里**随机取一个**
//   · **全部是完整人形**（有腿、有臂、有头）—— 禁止"幻影没腿""幽灵飘着"这类做法
//
// ## 与升级文档的冲突（以用户规则为准）
// `docs/spec/modeling-knowledge-upgrade.md` 曾写"无腿漂浮 / 下半身消失 = 经典手法，保留"。
// 那条按用户 2026-10-05 的指示**作废** —— 本文与生成器一律产出完整人形。
//
// ## 方法（来自知识库 11-恐怖游戏美术专项 + 03-有机体与角色，且经我 9 版实测）
// · 有机体唯一通途 = **Metaball**；珠间距 ≤ 表面半径/3（否则"糖葫芦"）
// · 融合半径与设计粗细**解耦**：radius 保融合、用 `size` squash 压细（知识库 03 条）
// · 恐怖感来自**比例失真**而非加细节：瘦长（身高/肩宽 ≥ 4.5）、头小眼大
// · 导出前 UV 展开（否则 glTF 按面角拆点，实测 10× 顶点膨胀）+ Decimate 到预算
// · `export_yup=False`（与 Unity 同约定）
//
// 用法：node tools/gen-ghost-roster-models.mjs [--quality low|med]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const BLENDER = process.env.WHISPER_BLENDER || 'C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe';
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const OUT = path.join(ROOT, 'unity/Assets/Resources/Models/ghostbody');

// ── 比例表：关节高度 / 横向 / 半径，全部"占身高 H 的比例" ──────────────────────
const CANON = {
  J: { foot: 0.018, ankle: 0.042, knee: 0.280, hip: 0.525, s1: 0.598, s2: 0.672,
       chest: 0.732, shoulder: 0.812, neck: 0.858, headC: 0.928, headTop: 0.982 },
  X: { leg: 0.060, shoulder: 0.096, elbow: 0.102, wrist: 0.106 },
  R: { foot: 0.050, ankle: 0.046, knee: 0.058, thigh: 0.074, hip: 0.076, spine1: 0.078,
       spine2: 0.084, chest: 0.094, shoulder: 0.068, neck: 0.048, head: 0.100, headTop: 0.064,
       arm: 0.042, elbow: 0.038, wrist: 0.030 },
  // 横截面 squash：**压细而不破坏融合**（知识库 03 条：radius 管融合，size 管粗细）
  squash: { leg: [1.0, 0.92, 1.0], arm: [1.0, 0.88, 1.0], torso: [1.0, 0.72, 1.0] },
  armDrop: 0.372, foreDrop: 0.338,
};

// ── 男女 × 体型（4 男 4 女 = 8 个模型）──────────────────────────────────────
// 性别差异用**可度量**的骨架参数表达（不是靠贴图/头发）：
//   · 女：肩略窄、髋略宽、腰更细、身高略矮、头略小
//   · 男：肩更宽、髋略窄、腰粗、身高略高
const SEX = {
  female: { w: { shoulder: 0.86, hip: 1.08, chest: 0.90, waistMul: 0.86 }, heightMul: 0.94 },
  male:   { w: { shoulder: 1.10, hip: 0.97, chest: 1.06, waistMul: 1.06 }, heightMul: 1.02 },
};

// 体型档：恐怖感来自比例失真 → 瘦长档最远（身高/肩宽 ≥ 4.5）
const FRAMES = {
  lanky:  { headHeads: 8.6, armMul: 1.18, legMul: 1.10, widths: { shoulder: 0.68, chest: 0.72, spine1: 0.70, s2: 0.72, thigh: 0.84 } },
  gaunt:  { headHeads: 8.2, armMul: 1.24, legMul: 1.06, widths: { shoulder: 0.62, chest: 0.62, spine1: 0.60, s2: 0.62, thigh: 0.74 } },
  normal: { headHeads: 7.5, armMul: 1.00, legMul: 1.00, widths: { shoulder: 0.80, chest: 0.84 } },
  stocky: { headHeads: 7.0, armMul: 0.96, legMul: 0.96, widths: { shoulder: 1.00, chest: 1.04, spine1: 1.14, s2: 1.12, thigh: 1.10 } },
};

const PY = String.raw`
import bpy, bmesh, math, json, os, sys, struct
A = globals().get('ARGS') or json.loads(sys.argv[sys.argv.index('--') + 1])
C, OUT, NAME = A['canon'], A['out'], A['name']
H = float(A['height'])
RES = {'low': 0.011, 'med': 0.008, 'high': 0.005}[A.get('quality', 'low')]
TARGETV = int(A.get('targetVerts', 6500))

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

J = dict(C['J']); J.update(A.get('joints', {}))
X = dict(C['X']); X.update(A.get('xs', {}))
R = dict(C['R'])
for k, v in A.get('widths', {}).items():
    if k in R: R[k] = R[k] * v
SQ = C['squash']
armMul = A.get('armMul', 1.0)
legMul = A.get('legMul', 1.0)
armDrop = C['armDrop'] * H * armMul
foreDrop = C['foreDrop'] * H * armMul
legSpan = (J['hip'] - J['foot']) * H

def z(n): return J[n] * H
def xx(n): return X[n] * H
def rr(n): return R[n] * H

mb = bpy.data.metaballs.new('Body')
mb.resolution = RES
mb.render_resolution = RES
mb.threshold = 0.45
obj = bpy.data.objects.new(NAME, mb)
bpy.context.scene.collection.objects.link(obj)

def blob(p, rad, squash=(1.0, 1.0, 1.0), stiffness=2.0):
    e = mb.elements.new()
    e.co = p
    e.radius = rad
    e.stiffness = stiffness
    e.size_x, e.size_y, e.size_z = squash
    return e

def seg(p0, p1, r0, r1, sq=(1.0, 1.0, 1.0)):
    """.** <= /3** ("").
     ~  radius x 0.6259( 03 ), surface = r*0.6259 ."""
    ax, ay, az = p1[0]-p0[0], p1[1]-p0[1], p1[2]-p0[2]
    L = math.sqrt(ax*ax + ay*ay + az*az)
    if L < 1e-6: return
    surf = max(min(r0, r1) * 0.6259, 1e-4)
    steps = max(4, min(120, int(L / (surf / 3.0))))
    for i in range(steps + 1):
        t = i / float(steps)
        blob((p0[0]+ax*t, p0[1]+ay*t, p0[2]+az*t), r0 + (r1-r0)*t, sq)

#  :(,)
sp = [(0,0,z('hip')), (0,0,z('s1')), (0,0,z('s2')), (0,0,z('chest'))]
sr = [rr('hip'), rr('spine1'), rr('spine2'), rr('chest')]
for i in range(3):
    seg(sp[i], sp[i+1], sr[i], sr[i+1], (SQ['torso'][0], SQ['torso'][1], SQ['torso'][2]))
# :()
seg((0,0,z('hip')*1.02), sp[0], rr('hip')*0.95, rr('hip'), (SQ['torso'][0], SQ['torso'][1], SQ['torso'][2]))
#  + ( = )
seg(sp[3], (0,0,z('neck')), rr('chest')*0.52, rr('neck'))
blob((0, 0.004*H, z('headC')), rr('head'), (1.0, 0.90, 1.05))
blob((0, -0.006*H, z('headC') + (z('headTop')-z('headC'))*0.55), rr('headTop'), (1.0, 0.92, 1.0))
blob((0, 0.010*H, z('headC') - (z('headC')-z('neck'))*0.5), rr('head')*0.62)

#  ( -> )
for sx in (1.0, -1.0):
    hip  = (sx*xx('leg'), 0, z('hip'))
    knee = (sx*xx('leg')*1.03, 0.004*H, z('knee'))
    ank  = (sx*xx('leg')*1.00, 0.010*H, z('ankle'))
    foot = (sx*xx('leg')*1.00, -0.048*H, z('foot'))
    seg((sx*xx('leg')*0.5, 0, z('hip')*1.01), hip, rr('hip')*0.9, rr('thigh'), (SQ['leg'][0], SQ['leg'][1], SQ['leg'][2]))
    seg(hip, knee, rr('thigh'), rr('knee'), (SQ['leg'][0], SQ['leg'][1], SQ['leg'][2]))
    seg(knee, ank, rr('knee')*A.get('calfMul', 1.0), rr('ankle'), (SQ['leg'][0], SQ['leg'][1], SQ['leg'][2]))
    seg(ank, foot, rr('ankle'), rr('foot'), (1.0, 1.45, 0.62))
    sh  = (sx*xx('shoulder'), 0, z('shoulder'))
    elb = (sx*xx('elbow'), 0.004*H, z('shoulder') - armDrop)
    wri = (sx*xx('wrist'), 0.012*H, z('shoulder') - armDrop - foreDrop)
    seg((sx*xx('shoulder')*0.45, 0, z('shoulder')-0.004*H), sh, rr('shoulder')*0.9, rr('shoulder'))
    seg(sh, elb, rr('arm')*1.12, rr('elbow'), (SQ['arm'][0], SQ['arm'][1], SQ['arm'][2]))
    seg(elb, wri, rr('elbow'), rr('wrist'), (SQ['arm'][0], SQ['arm'][1], SQ['arm'][2]))
    blob(wri, rr('wrist')*1.5, (1.0, 1.35, 1.0))

#  -> mesh
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.convert(target='MESH')
body = bpy.context.view_layer.objects.active
if body.type != 'MESH':
    body = [o for o in bpy.context.scene.collection.objects if o.type == 'MESH'][0]
bpy.ops.object.shade_smooth()

#  +
mn = [1e9]*3; mx = [-1e9]*3
for v in body.data.vertices:
    q = body.matrix_world @ v.co
    mn = [min(mn[k], q[k]) for k in range(3)]; mx = [max(mx[k], q[k]) for k in range(3)]
curH = mx[2]-mn[2]
if curH > 1e-4:
    k = H/curH
    for v in body.data.vertices: v.co = (v.co.x*k, v.co.y*k, v.co.z*k)
    zmin = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices: v.co.z -= zmin
    body.data.update()

# Decimate (, 20k+)
cur = len(body.data.vertices)
if cur > TARGETV*1.15:
    d = body.modifiers.new('Decimate','DECIMATE'); d.decimate_type='COLLAPSE'
    d.ratio = max(0.10, min(1.0, TARGETV/float(cur)))
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=d.name)
    bpy.ops.object.shade_smooth()

# : + SSS( = "");
m = bpy.data.materials.new('MAT-GhostBody'); m.use_nodes = True
b = m.node_tree.nodes.get('Principled BSDF')
b.inputs['Base Color'].default_value = (0.62, 0.60, 0.58, 1.0)
b.inputs['Roughness'].default_value = 0.55
for nm, val in (('Subsurface Weight', 0.30),):
    if nm in b.inputs: b.inputs[nm].default_value = val
if 'Subsurface Radius' in b.inputs:
    b.inputs['Subsurface Radius'].default_value = (1.0, 0.3, 0.25)
body.data.materials.append(m)

# :Emission( Base Color)-- /,
def emis(name, rgb, strength):
    mm = bpy.data.materials.new(name); mm.use_nodes = True; nt = mm.node_tree
    for nd in list(nt.nodes):
        if nd.type != 'OUTPUT_MATERIAL': nt.nodes.remove(nd)
    out = nt.nodes[0]; e = nt.nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    e.inputs['Strength'].default_value = strength
    nt.links.new(e.outputs['Emission'], out.inputs['Surface']); return mm

eyes = []
eyeZ = z('headC') + (z('headTop')-z('headC'))*0.10
eyeX = rr('head')*0.44
eyeR = rr('head')*0.21
for tag, mm in (('Red', emis('MAT-GhostEyeRedIris', (1.0, 0.04, 0.03), 1500.0)),
                ('White', emis('MAT-GhostEyeWhiteIris', (1.0, 1.0, 0.98), 1500.0))):
    for sx in (1.0, -1.0):
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=10, radius=eyeR)
        me = bpy.data.meshes.new('Eye'); bm.to_mesh(me); bm.free()
        o = bpy.data.objects.new('GEO-Eye' + tag + ('L' if sx > 0 else 'R'), me)
        bpy.context.scene.collection.objects.link(o)
        o.location = (sx*eyeX, -rr('head')*0.62, eyeZ); o.scale = (1.0, 0.55, 1.0)
        me.materials.append(mm)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.shade_smooth()
        eyes.append(o)

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
for o in eyes: o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
final = bpy.context.view_layer.objects.active

# UV(****: UV  glTF , 10x )
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(85), island_margin=0.005)
bpy.ops.object.mode_set(mode='OBJECT')

os.makedirs(OUT, exist_ok=True)
p = os.path.join(OUT, NAME + '.glb')
bpy.ops.object.select_all(action='DESELECT'); final.select_set(True)
bpy.context.view_layer.objects.active = final
bpy.ops.export_scene.gltf(filepath=p, export_format='GLB', use_selection=True,
                          export_apply=True, export_yup=False)

mn = [1e9]*3; mx = [-1e9]*3
for v in final.data.vertices:
    q = final.matrix_world @ v.co
    mn = [min(mn[k], q[k]) for k in range(3)]; mx = [max(mx[k], q[k]) for k in range(3)]
with open(p, 'rb') as f: buf = f.read()
jl = struct.unpack_from('<I', buf, 12)[0]
g = json.loads(buf[20:20+jl].decode('utf8'))
acc = g.get('accessors', [])
rv = sum(acc[pr['attributes']['POSITION']]['count'] for me2 in g.get('meshes', []) for pr in me2['primitives'])
print('GHOSTMODEL ' + json.dumps({
    'file': os.path.basename(p), 'bytes': os.path.getsize(p), 'verts': rv,
    'height': round(mx[2]-mn[2], 3), 'width': round(mx[0]-mn[0], 3), 'depth': round(mx[1]-mn[1], 3),
    'below_ground': round(min(mn[2], 0.0), 4), 'base': NAME, 'ratio': round((mx[2]-mn[2])/max(mx[0]-mn[0], 1e-4), 2)}))
`;

const args = process.argv.slice(2);
const opt = (n, d) => { const i = args.indexOf('--' + n); return i >= 0 ? args[i + 1] : d; };
const quality = opt('quality', 'low');
fs.mkdirSync(OUT, { recursive: true });

const rows = [];
const tmp = path.join(ROOT, 'tools', '.ghost-roster-run.py');
fs.writeFileSync(tmp, PY, 'utf8');

for (const [sexName, sex] of Object.entries(SEX)) {
  for (const [frameName, frame] of Object.entries(FRAMES)) {
    const name = `GEO-GhostBody_${sexName}_${frameName}`;
    const height = 1.86 * sex.heightMul * (frameName === 'stocky' ? 0.94 : frameName === 'lanky' ? 1.05 : 1.0);
    const widths = {};
    for (const [k, v] of Object.entries(frame.widths || {})) widths[k] = v;
    for (const [k, v] of Object.entries(sex.w || {})) {
      // R 表里**没有 `waist` 这个键**（腰由 spine1 + s2 表达）—— 我先前直接写 widths.waist 导致
      // Python 侧 `R[k] = R[k] * v` 抛 KeyError: 'waist'。这里映射到真实存在的键。
      if (k === 'waistMul') { widths.spine1 = (widths.spine1 ?? 1) * v; widths.s2 = (widths.s2 ?? 1) * v; }
      else widths[k] = (widths[k] ?? 1) * v;
    }
    const res = execFileSync(BLENDER, ['--background', '--factory-startup', '--python', tmp, '--',
      JSON.stringify({ canon: CANON, out: OUT, name, height, quality,
                       widths, armMul: frame.armMul, legMul: frame.legMul,
                       calfMul: sexName === 'female' ? 0.94 : 1.0,
                       targetVerts: 4800 })],
      { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
    const m = res.match(/GHOSTMODEL (\{.*\})/s);
    if (!m) { console.log(`  ${name} 失败，完整输出：\n` + res.slice(-1800)); continue; }
    const r = JSON.parse(m[1]);
    rows.push(r);
    console.log(`  ${name.padEnd(40)} ${String(r.verts).padStart(5)} 顶点 · ${(r.bytes / 1024).toFixed(0)} KB · 高 ${r.height}m · 宽 ${r.width}m · 高宽比 ${r.ratio}`);
  }
}
fs.writeFileSync(path.join(OUT, 'roster-models.json'), JSON.stringify({
  note: '男女双性别 × 4 体型 = 8 个模型。**模型与鬼类型完全解耦**（用户规则）：配置里只写这个池，运行时随机取。',
  models: rows.map((r) => ({ file: r.file, sex: r.base.includes('female') ? 'female' : 'male', verts: r.verts, height: r.height })),
}, null, 2) + '\n', 'utf8');
console.log(`  ✓ ${rows.length} 个模型 → ${OUT}`);
