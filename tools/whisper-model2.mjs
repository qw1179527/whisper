// `tools/whisper-model2.mjs` · **骨架 + Skin 修改器**生成连续人体（替代"胶囊拼装"）。
//
// ## 为什么推翻重写
// 前一版是"若干锥形胶囊摆在一起"。我连修 6 处（端点半径、腿被截断、相机取景、身高单位…）
// 仍不对 —— 因为**架构错了**：分离图元之间必然有接缝/错位，比例也无法整体协调。
//
// ## 正确的架构（Blender 的 Skin 修改器就是为此而生）
//   ① 用**顶点**表示关节、**边**表示骨骼，建一张骨架图
//   ② 每个顶点给一个半径（`skin_vertices[i].radius`）
//   ③ `SKIN` 修改器沿骨架**蒙皮**出一整块连续有机体（自动生成关节过渡）
//   ④ `SUBSURF` 平滑 → 得到真正的"人体"，而不是"一堆圆柱"
// 这是程序化角色生成的通行做法；改比例只需改关节坐标与半径。
//
// 用法：node tools/whisper-model2.mjs build --body lanky [--quality low] [--height 1.95] [--out <dir>]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const BLENDER = process.env.WHISPER_BLENDER || 'C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe';
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// ── 比例表：全部是"占身高 H 的比例" ─────────────────────────────────────────
const CANON = {
  J: {   // 关节高度（占 H）
    foot: 0.000, ankle: 0.040, knee: 0.285, hip: 0.530, spine1: 0.600, spine2: 0.680,
    chest: 0.735, shoulder: 0.815, neck: 0.868, headBase: 0.885, headTop: 1.000,
  },
  X: {   // 横向位置（占 H）
    leg: 0.058, shoulder: 0.100, elbow: 0.108, wrist: 0.112,
  },
  R: {   // 半径（占 H）
    foot: 0.026, ankle: 0.023, knee: 0.033, hip: 0.055, spine1: 0.062, spine2: 0.070,
    chest: 0.082, shoulder: 0.058, neck: 0.028, headBase: 0.030, headMid: 0.062,
    headTop: 0.045, arm: 0.026, elbow: 0.023, wrist: 0.017,
  },
  armDrop: 0.395,     // 肩→肘 的竖直下降（占 H）
  foreDrop: 0.360,    // 肘→腕
};

// 体型修饰：只改比例常数
const BODIES = {
  human: {},
  tall: { headHeads: 7.9, w: { chest: 0.92, shoulder: 0.95, hip: 0.95 } },
  stocky: { headHeads: 7.0, w: { chest: 1.20, shoulder: 1.22, hip: 1.15, knee: 1.12 }, wArm: 1.25 },
  child: { headHeads: 5.6, w: { chest: 0.86, hip: 0.92 }, headMul: 1.55, armDrop: 0.34, foreDrop: 0.30 },
  lanky: { headHeads: 8.6, w: { chest: 0.80, hip: 0.85, knee: 0.90 }, wArm: 0.78,
           armDrop: 0.470, foreDrop: 0.430, elongateSkull: 0.26, ribFlare: 0.16 },
  gaunt: { headHeads: 8.2, w: { chest: 0.70, hip: 0.78, knee: 0.82 }, wArm: 0.66,
           armDrop: 0.500, foreDrop: 0.460, elongateSkull: 0.34, ribFlare: 0.30 },
  hulk: { headHeads: 7.2, w: { chest: 1.38, shoulder: 1.45, hip: 1.30, knee: 1.25 }, wArm: 1.45 },
};

const PY = String.raw`
import bpy, bmesh, math, json, os, sys
ARGS = globals().get('ARGS') or json.loads(sys.argv[sys.argv.index('--') + 1])
C, B, OUT, TAG = ARGS['canon'], ARGS['body'], ARGS['out'], ARGS['tag']
H = float(ARGS['height'])
Q = {'low': (1, 1), 'med': (2, 1), 'high': (2, 2)}[ARGS.get('quality', 'med')]
SUBLV, SKINSUB = Q

J = dict(C['J']); J.update(B.get('j', {}))
X = dict(C['X']); X.update(B.get('x', {}))
R = dict(C['R'])
for k, v in B.get('w', {}).items(): R[k] = R[k] * v
armW = B.get('wArm', 1.0)
armDrop = B.get('armDrop', C['armDrop']) * H
foreDrop = B.get('foreDrop', C['foreDrop']) * H
elong = B.get('elongateSkull', 0.0)
flare = B.get('ribFlare', 0.0)
headMul = B.get('headMul', 1.0)

def z(n): return J[n] * H
def x_(n): return X[n] * H
def r_(n): return R[n] * H * headMul

# ── 骨架：顶点 = 关节，边 = 骨骼 ──────────────────────────────────────────────
# 居中脊柱 + 左右对称肢体。**对称性由构造保证**（同一条定义镜像 x），不靠复制-翻转。
verts, radii = [], []
def V(p, rad):
    verts.append(p); radii.append(rad); return len(verts) - 1

def build_side(sx):
    """返回该侧的索引字典（sx=+1 右 / -1 左）"""
    hip   = V((sx * x_('leg'), 0, z('hip')),      r_('hip'))
    knee  = V((sx * x_('leg') * 1.02, 0, z('knee')), r_('knee'))
    ankle = V((sx * x_('leg') * 1.00, 0.006 * H, z('ankle')), r_('ankle'))
    foot  = V((sx * x_('leg') * 1.00, -0.055 * H, z('foot') + 0.012 * H), r_('foot'))
    sh    = V((sx * x_('shoulder'), 0, z('shoulder')), r_('shoulder'))
    elb   = V((sx * x_('elbow'), 0.004 * H, z('shoulder') - armDrop), r_('elbow') * armW)
    wri   = V((sx * x_('wrist'), 0.012 * H, z('shoulder') - armDrop - foreDrop), r_('wrist') * armW)
    return dict(hip=hip, knee=knee, ankle=ankle, foot=foot, sh=sh, elb=elb, wri=wri)

# 脊柱（居中）
sp0 = V((0, 0, z('hip') * 1.02),            r_('hip') * 1.10)
sp1 = V((0, 0, z('spine1')),                r_('spine1') * (1 + flare * 0.5))
sp2 = V((0, 0, z('spine2')),                r_('spine2') * (1 + flare))
che = V((0, 0, z('chest')),                 r_('chest'))
nek = V((0, 0, z('neck')),                  r_('neck'))
hb  = V((0, 0, z('headBase')),              r_('headBase'))
hm  = V((0, 0, z('headBase') + (z('headTop') - z('headBase')) * 0.42 * (1 + elong)), r_('headMid') * headMul)
ht  = V((0, 0, z('headTop')),               r_('headTop') * headMul)

S = {1: build_side(1.0), -1: build_side(-1.0)}
edges = [(sp0,sp1),(sp1,sp2),(sp2,che),(che,nek),(nek,hb),(hb,hm),(hm,ht)]
for sx, d in S.items():
    edges += [(sp0, d['hip']), (d['hip'], d['knee']), (d['knee'], d['ankle']), (d['ankle'], d['foot'])]
    edges += [(che, d['sh']), (d['sh'], d['elb']), (d['elb'], d['wri'])]

me = bpy.data.meshes.new('Body')
me.from_pydata(verts, edges, [])
me.update()
obj = bpy.data.objects.new('TAGNAME', me)
bpy.context.scene.collection.objects.link(obj)
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

# ── Skin 修改器：沿骨架蒙皮成一整块连续机体 ──────────────────────────────────
sk = obj.modifiers.new('Skin', 'SKIN')
sk.use_smooth_shade = True
sk.branch_smoothing = 0.6          # 分支处（肩/髋）过渡更柔和，避免硬折角
sv = obj.data.skin_vertices[0].data
for i, rad in enumerate(radii):
    sv[i].radius = (max(rad, 0.004 * H), max(rad, 0.004 * H))
# 根节点
obj.data.skin_vertices[0].data[sp0].use_root = True

sub = obj.modifiers.new('SubSurf', 'SUBSURF')
sub.levels = SUBLV; sub.render_levels = SUBLV
bpy.ops.object.shade_smooth()

# 烘修改器
dg = bpy.context.evaluated_depsgraph_get()
baked = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
old = obj.data; obj.data = baked; bpy.data.meshes.remove(old)
for m in list(obj.modifiers): obj.modifiers.remove(m)

# 材质
m = bpy.data.materials.new('MAT-GhostBody'); m.use_nodes = True
bsdf = m.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Base Color'].default_value = (0.055, 0.058, 0.062, 1.0)
bsdf.inputs['Roughness'].default_value = 0.86
obj.data.materials.append(m)

# ── 眼：独立小球（虹膜自发光 / 瞳孔近黑），**嵌进眼窝** ─────────────────────
def emissive(name, rgb, strength):
    mm = bpy.data.materials.new(name); mm.use_nodes = True; nt = mm.node_tree
    for nd in list(nt.nodes):
        if nd.type != 'OUTPUT_MATERIAL': nt.nodes.remove(nd)
    out = nt.nodes[0]; e = nt.nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    e.inputs['Strength'].default_value = strength
    nt.links.new(e.outputs['Emission'], out.inputs['Surface']); return mm

eyeZ = z('headBase') + (z('headTop') - z('headBase')) * 0.46 * (1 + elong)
eyeX = r_('headMid') * headMul * 0.46
eyeR = r_('headMid') * headMul * 0.26
eyes = []
for tag, mm in (('Red', emissive('MAT-GhostEyeRedIris', (1.0, 0.10, 0.05), 3.4)),
                ('White', emissive('MAT-GhostEyeWhiteIris', (0.88, 0.93, 1.0), 2.8))):
    for sx in (1.0, -1.0):
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=10, radius=eyeR)
        me2 = bpy.data.meshes.new('Eye'); bm.to_mesh(me2); bm.free()
        o = bpy.data.objects.new('Eye' + tag, me2); bpy.context.scene.collection.objects.link(o)
        o.location = (sx * eyeX, -r_('headMid') * headMul * 0.62, eyeZ); o.scale = (1, 0.55, 1)
        me2.materials.append(mm)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.shade_smooth()
        eyes.append(o)

# 合并眼到身体（**同一物体**，方便一个 GLB 一只鬼）
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
for o in eyes: o.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active

# UV（导出前必做：没有 UV 时 glTF 按面角复制顶点，实测 10× 膨胀）
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(85), island_margin=0.005)
bpy.ops.object.mode_set(mode='OBJECT')

# ── 落地对齐 + 精确身高（Skin 出来的包络与骨架略有出入，必须用**实际包围盒**校正）──
mn = [1e9] * 3; mx = [-1e9] * 3
for v in body.data.vertices:
    q = body.matrix_world @ v.co
    mn = [min(mn[k], q[k]) for k in range(3)]; mx = [max(mx[k], q[k]) for k in range(3)]
curH = mx[2] - mn[2]
if curH > 1e-4:
    k = H / curH
    for v in body.data.vertices:
        v.co = ((v.co.x) * k, (v.co.y) * k, (v.co.z) * k)
    # 缩完重新落地（脚底 z=0）
    zmin = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices: v.co.z -= zmin
    body.data.update()

os.makedirs(OUT, exist_ok=True)
p = os.path.join(OUT, 'GEO-GhostBody_' + ARGS['bodyName'] + '.glb')
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.export_scene.gltf(filepath=p, export_format='GLB', use_selection=True,
                          export_apply=True, export_yup=False)

# 客观尺寸 + 从产出字节读真实顶点数
import struct
mn = [1e9] * 3; mx = [-1e9] * 3
for v in body.data.vertices:
    q = body.matrix_world @ v.co
    mn = [min(mn[k], q[k]) for k in range(3)]; mx = [max(mx[k], q[k]) for k in range(3)]
with open(p, 'rb') as f: buf = f.read()
jl = struct.unpack_from('<I', buf, 12)[0]
g = json.loads(buf[20:20 + jl].decode('utf8'))
acc = g.get('accessors', [])
rv = sum(acc[pr['attributes']['POSITION']]['count'] for me3 in g.get('meshes', []) for pr in me3['primitives'])
print('WHISPER_MODEL2 ' + json.dumps({
    'path': p, 'bytes': os.path.getsize(p), 'glb_vertices': rv,
    'spanX': round(mx[0] - mn[0], 3), 'spanY': round(mx[1] - mn[1], 3), 'spanZ': round(mx[2] - mn[2], 3),
    'zmin': round(mn[2], 3), 'zmax': round(mx[2], 3), 'body': ARGS['bodyName']}))
`;

const args = process.argv.slice(2);
const opt = (n, d) => { const i = args.indexOf('--' + n); return i >= 0 ? args[i + 1] : d; };
const cmd = args[0];
const OUTDIR = opt('out', path.join(ROOT, 'unity/Assets/Resources/Models/ghostbody'));

function run(body, outOverride) {
  const tmp = path.join(ROOT, 'tools', '.whisper-model2-run.py');
  const py = PY.replace(/TAGNAME/g, 'GEO-GhostBody_' + body);
  fs.writeFileSync(tmp, py, 'utf8');
  const res = execFileSync(BLENDER, ['--background', '--factory-startup', '--python', tmp, '--',
    JSON.stringify({ canon: CANON, body: BODIES[body], bodyName: body, out: outOverride || OUTDIR,
                     tag: 'GhostBody', height: parseFloat(opt('height', '1.95')), quality: opt('quality', 'low') })],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const m = res.match(/WHISPER_MODEL2 (\{.*\})/s);
  if (!m) throw new Error('Blender 未返回结果：\n' + res.slice(-1200));
  return JSON.parse(m[1]);
}

if (cmd === 'list') {
  for (const k of Object.keys(BODIES)) console.log('  ' + k);
} else if (cmd === 'build') {
  const b = opt('body', 'lanky');
  if (!BODIES[b]) { console.error('未知体型 ' + b); process.exit(2); }
  const r = run(b);
  console.log(`  ${b.padEnd(7)} GLB ${r.glb_vertices} 顶点 · ${(r.bytes / 1024).toFixed(0)} KB · X=${r.spanX} Y=${r.spanY} Z=${r.spanZ} (脚 z=${r.zmin})`);
} else if (cmd === 'all') {
  const out = path.join(ROOT, '_evidence/model-sheet');
  fs.mkdirSync(out, { recursive: true });
  for (const b of Object.keys(BODIES)) {
    const r = run(b, out);
    console.log(`  ${b.padEnd(7)} GLB ${String(r.glb_vertices).padStart(6)} 顶点 · Z=${r.spanZ}m 肩宽 X=${r.spanX}m`);
  }
} else {
  console.log('用法：node tools/whisper-model2.mjs list | build --body lanky | all');
}
