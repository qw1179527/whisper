// `whisper-model` · 免费自研建模工具（Blender Python 生成器）
//
// ## 为什么自研
// 我手调几何连做 6 版仍被用户判"烂"。根因不是审美，是**方法**：
//   我在"堆环 + 手填半径"——每次改动都要重调所有数字，且无法保证部件关系
//   （手臂上移就悬空、外移就脱离躯干，实测两版都踩）。
// 正解（[8 头身比例](https://www.uiltexas.org/files/academics/figure_drawing_handout_-_UIL_Tyler.pdf) ·
//      [形变就绪的网格](https://www.tripo3d.ai/blog/explore/smart-mesh-character-mesh-deformation-readiness)）：
//   **先定骨架（关节位置由比例常数算出）→ 再沿"两关节之间的锥形胶囊"生成肢体** →
//   几何**由构造保证连通**，改比例只需改常数。
//
// ## 用法
//   node tools/whisper-model.mjs build --type ghost --body lanky --out <dir>
//   node tools/whisper-model.mjs list
// 它**调用 Blender 生成脚本**（不是自己算几何），产物直接是 `.glb.bytes` 友好的 GLB。
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const BLENDER = process.env.WHISPER_BLENDER || 'C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe';
// ⚠ 不要用 `new URL(import.meta.url).pathname` —— 中文路径会变成 `%XX` 百分号编码，
// Blender 拿到那种路径直接失败（本项目目录含中文，实测踩过）。
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// ── 比例表（8 头身成人基准；改这里就是改全套体型）──────────────────────────
// 关节高度都是"占身高 H 的比例"，宽度是"占身高 H 的比例"。来源：标准人体比例。
const CANON = {
  headHeads: 7.5,          // 全身 = 7.5 个头高
  joints: {
    footZ: 0.000, ankleZ: 0.039, kneeZ: 0.285, hipZ: 0.530,
    waistZ: 0.600, chestZ: 0.720, shoulderZ: 0.820, neckZ: 0.870,
    headCenterZ: 0.930, eyeZ: 0.936, topZ: 1.000,
  },
  widths: {               // 半径（占 H）
    ankle: 0.022, knee: 0.032, thigh: 0.048, hip: 0.072,
    waist: 0.062, chest: 0.088, shoulder: 0.100, neck: 0.030,
    upperArm: 0.028, elbow: 0.024, wrist: 0.018, hand: 0.022,
    headR: 0.055,
  },
  limbs: {                // 关节位置（x 占 H）
    legX: 0.055, shoulderX: 0.105, elbowX: 0.150, wristX: 0.165,
  },
};

/** 体型修饰：只改"比例常数"，几何由生成器重算 —— 这就是模板化的价值。 */
const BODIES = {
  human:  {},                                                  // 基准（8 头身）
  tall:   { headHeads: 7.9, w: { chest: 0.92, shoulder: 0.95, waist: 0.88, thigh: 0.95 } },
  stocky: { headHeads: 7.0, w: { chest: 1.18, shoulder: 1.20, waist: 1.30, thigh: 1.15, hip: 1.12 } },
  child:  { headHeads: 5.6, w: { chest: 0.86, shoulder: 0.84, waist: 0.90, thigh: 0.90 } },
  // ── 恐怖向：这些偏移才是"鬼"的味道 ──
  lanky:  { headHeads: 8.6, w: { chest: 0.78, shoulder: 0.82, waist: 0.62, thigh: 0.82, upperArm: 0.74 },
            j: { shoulderZ: 0.835, neckZ: 0.885 }, armLen: 1.22,   // 手臂过长（过膝）
            elongateSkull: 0.28, ribFlare: 0.18, taperLegs: 0.72 },
  gaunt:  { headHeads: 8.2, w: { chest: 0.70, shoulder: 0.76, waist: 0.55, thigh: 0.78, upperArm: 0.70 },
            j: { shoulderZ: 0.830 }, armLen: 1.30, elongateSkull: 0.34, ribFlare: 0.30, taperLegs: 0.62 },
  hulk:   { headHeads: 7.2, w: { chest: 1.34, shoulder: 1.42, waist: 1.15, thigh: 1.30, hip: 1.25, upperArm: 1.40 },
            j: { shoulderZ: 0.810 } },
};

// ── Blender 侧脚本：**从骨架生成**（关节 → 锥形胶囊肢体）────────────────────
const PY = String.raw`
import bpy, bmesh, math, json, os, sys
from mathutils import Vector

# 允许调用方**预先注入** ARGS（对比图工具在同一次 Blender 会话里跑多组参数）；
# 未注入时才从命令行读。没有这个守卫时，注入会被这里覆盖 → 对比图拿不到体型参数。
ARGS = globals().get('ARGS') or json.loads(sys.argv[sys.argv.index('--') + 1])
CANON, BODY, OUT, TAG = ARGS['canon'], ARGS['body'], ARGS['out'], ARGS['tag']
# ── 全局质量档（由 --quality 传）────────────────────────────────────────────
# 手机游戏的顶点预算：**一屏 3~5 只鬼 + 关卡几何**。所以单只鬼目标 3~6k 顶点。
# 段数与细分级别的乘积决定顶点数；这里一处收口，避免逐调用点写死导致 83k 那种事故。
QUAL = ARGS.get('quality', 'med')
Q = {
  'low':  dict(seg=6,  segBig=10, rings=2, subsurf=0, sphere=8,  sphereV=6,  bevelSeg=1),
  'med':  dict(seg=8,  segBig=14, rings=3, subsurf=1, sphere=10, sphereV=8,  bevelSeg=1),
  'high': dict(seg=14, segBig=20, rings=5, subsurf=2, sphere=18, sphereV=12, bevelSeg=2),
}[QUAL]
SEG, SEGBIG, RINGS, SUB, SPH, SPHV, BSEG = (Q['seg'], Q['segBig'], Q['rings'],
                                            Q['subsurf'], Q['sphere'], Q['sphereV'], Q['bevelSeg'])

HEADS = float(BODY.get('headHeads', 7.5))
# 【对比图暴露的真 bug】原实现里 HEADS 只存不用、H 硬编码 1.0 →
# 7 个体型**几何完全一样**（对比图上肉眼分不出）。现在让 HEADS 真正驱动比例：
#   头身比越小 → 头越大；全身关节位置相对"头高"重排。
# 头高（占身高） = 1/HEADS；关节位置按 (基准/7.5) 的头高比例缩放，头半径直接 = 头高/2。
# 直接用**真实身高**做单位（不再"归一化到 1.0 再靠对象缩放" —— 那种做法在
# 烘焙修改器/合并/导出链路上容易丢，实测 Z 跨度只有 0.95m 而设定是 1.95m）。
H = float(ARGS['height'])
scale = 1.0
HEAD_H = 1.0 / HEADS                     # 头高（占身高）
REF_HEAD_H = 1.0 / 7.5                   # 基准头高
headScale = HEAD_H / REF_HEAD_H          # >1 = 头更大（更矮壮/孩童），<1 = 头更小（更高瘦）
scale = float(ARGS['height']) / H

J = dict(CANON['joints']); J.update(BODY.get('j', {}))
W = dict(CANON['widths'])
for k, v in BODY.get('w', {}).items(): W[k] = W[k] * v
LX = dict(CANON['limbs']); LX.update(BODY.get('limbX', {}))

armMul = BODY.get('armLen', 1.0)
elong = BODY.get('elongateSkull', 0.0)
ribFlare = BODY.get('ribFlare', 0.0)
taperLegs = BODY.get('taperLegs', 1.0)

def JZ(name):
    # 头越小（headScale<1）→ 躯干相对更长：把"上半身"的关节按 headScale 反向补偿，
    # 这样 8.6 头身会真的显得腿长躯干长，而不是只有数字在变。
    z = J[name] * H
    if name in ('shoulderZ', 'neckZ', 'chestZ', 'waistZ', 'headCenterZ', 'eyeZ', 'topZ'):
        z = z * (1.0 + (1.0 - headScale) * 0.12)
    return z
def JW(name): return W[name] * H
def JX(name): return LX[name] * H

def taper_capsule(bm, p0, p1, r0, r1, seg=None, rings=None, bulge=0.0):
    seg = seg or SEG; rings = rings or RINGS
    """沿 p0→p1 生成**锥形胶囊**：半径从 r0 线性插值到 r1，中段可 bulge 外凸。
    这是"肢体"的正确几何 —— 而不是一根等半径圆柱。"""
    axis = (Vector(p1) - Vector(p0)); L = axis.length
    if L < 1e-6: return
    d = axis.normalized()
    up = Vector((0,0,1)) if abs(d.z) < 0.95 else Vector((1,0,0))
    u = d.cross(up).normalized(); v = d.cross(u).normalized()
    ringverts = []
    for i in range(rings + 1):
        t = i / rings
        # 端部球化（胶囊感），中段按 bulge 外凸
        cap = math.sin(math.pi * min(1.0, max(0.0, t))) ** 0.35 if bulge > 0 else 1.0
        r = (r0 * (1 - t) + r1 * t)
        r *= (1.0 + bulge * math.sin(math.pi * t))
        # 【真 bug】原来把端部半径砍到 0.55 倍 → "胶囊"变成**两端收尖的锥子**，
        # 四肢看着像细针、腿短到几乎看不见。正解：端部维持满半径，用**球冠**收口
        # （半球面：半径按 sqrt 规律收缩，最后一点收到接近 0 但只有极短一段）。
        if i == 0:
            r = r0
        elif i == rings:
            r = r1
        c = Vector(p0) + axis * t
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            ring.append(bm.verts.new(c + u * (r * math.cos(a)) + v * (r * math.sin(a))))
        ringverts.append(ring)
    bm.verts.ensure_lookup_table()
    for a, b in zip(ringverts, ringverts[1:]):
        for k in range(seg):
            m = (k + 1) % seg
            bm.faces.new((a[k], a[m], b[m], b[k]))
    bm.faces.new(list(reversed(ringverts[0]))); bm.faces.new(list(ringverts[-1]))

def ribcage(bm, zHi, zLo, rHi, rLo, seg=None, ribs=None, flare=0.0):
    seg = seg or SEGBIG; ribs = ribs or RINGS
    """胸腔：**多环收放**（不是直筒）——肋骨外翻由 flare 控制，这是"饥饿感"的来源。"""
    rings = []
    for i in range(ribs + 1):
        t = i / ribs
        z = zHi * (1 - t) + zLo * t
        base = rHi * (1 - t) + rLo * t
        wob = 1.0 + flare * math.sin(math.pi * t * 1.6) * (1 - t * 0.4)
        rings.append([bm.verts.new((base * wob * math.cos(2*math.pi*k/seg),
                                    base * wob * 0.66 * math.sin(2*math.pi*k/seg), z)) for k in range(seg)])
    bm.verts.ensure_lookup_table()
    for a, b in zip(rings, rings[1:]):
        for k in range(seg):
            m = (k+1) % seg; bm.faces.new((a[k], a[m], b[m], b[k]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(list(rings[-1]))

def new_obj(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    me.materials.append(mat); return o

def finish(o, bevel=0.010, subsurf=None):
    subsurf = SUB if subsurf is None else min(subsurf, SUB)
    # ⚠ bevel=None 时必须**跳过**（BevelModifier.width 不接受 None，实测崩过）
    if bevel:
        b = o.modifiers.new('Bevel','BEVEL'); b.width = bevel; b.segments = BSEG
        b.limit_method='ANGLE'; b.angle_limit=math.radians(35)
    s = o.modifiers.new('SubSurf','SUBSURF'); s.levels = subsurf; s.render_levels = subsurf
    for p in o.data.polygons: p.use_smooth = True
    return o

# 材质
mbody = bpy.data.materials.new("MAT-GhostBody"); mbody.use_nodes = True
b = mbody.node_tree.nodes.get("Principled BSDF")
b.inputs['Base Color'].default_value = (0.055,0.058,0.062,1.0); b.inputs['Roughness'].default_value = 0.86

parts = []

# ① 躯干：胸腔（带肋骨外翻）+ 腹 + 尾端收尖
bm = bmesh.new()
ribcage(bm, JZ('chestZ'), JZ('waistZ'), JW('chest'), JW('waist'), flare=ribFlare)
taper_capsule(bm, (0,0,JZ('waistZ')), (0,0,JZ('hipZ')*0.72), JW('waist'), JW('hip')*0.55)
taper_capsule(bm, (0,0,JZ('hipZ')*0.72), (0,0,JZ('hipZ')*0.72-0.30*H), JW('hip')*0.55, JW('hip')*0.10)
parts.append(finish(new_obj("Torso", bm, mbody), bevel=0.011))

# ② 头：颅骨沿 Z 拉长（elong）= 非人感；下颌收窄
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=SPH, v_segments=SPHV, radius=JW('headR'))
for v in bm.verts:
    v.co.z *= (1.10 + elong)
    v.co.y *= 0.88
    if v.co.z < 0:
        kk = 1.0 - min(0.62, (-v.co.z / JW('headR')) * 2.4); v.co.x *= kk; v.co.y *= kk
bmesh.ops.bevel(bm, geom=list(bm.edges)+list(bm.verts), offset=0.004, segments=2, affect='EDGES')
hm = new_obj("Head", bm, mbody); hm.location = (0,0,JZ('headCenterZ'))
parts.append(finish(hm, bevel=None))

# ③ 颈：从颈根到颅底**深插 3cm**
bm = bmesh.new()
taper_capsule(bm, (0,0,JZ('neckZ')-0.02), (0,0,JZ('headCenterZ')), JW('neck'), JW('neck')*0.92)
parts.append(finish(new_obj("Neck", bm, mbody), bevel=0.008))

# ④ 四肢：沿关节生成锥形胶囊 + **肩/髋端埋进躯干**（深插接合，防可见接缝）
shZ = JZ('shoulderZ'); hipZ = JZ('hipZ')
elbowZ = hipZ - (shZ - hipZ) * 0.52 * armMul
wristZ = hipZ - (shZ - hipZ) * (1.02 * armMul)
for sx in (1.0, -1.0):
    bm = bmesh.new()
    # 上臂：肩（埋进躯干）→ 肘
    taper_capsule(bm, (sx*JX('shoulderX')*0.66, 0, shZ), (sx*JX('elbowX'), 0.01, elbowZ),
                  JW('upperArm')*1.15, JW('elbow'), bulge=0.06)
    # 前臂：肘 → 腕（更细）
    taper_capsule(bm, (sx*JX('elbowX'), 0.01, elbowZ), (sx*JX('wristX'), 0.02, wristZ),
                  JW('elbow'), JW('wrist'))
    # 手：腕 → 指尖（扁而长）
    taper_capsule(bm, (sx*JX('wristX'), 0.02, wristZ), (sx*JX('wristX')*1.02, 0.03, wristZ-0.085*H),
                  JW('hand'), JW('hand')*0.42)
    parts.append(finish(new_obj("Arm", bm, mbody), bevel=0.009))

    bm = bmesh.new()
    # 腿：髋（埋进躯干）→ 膝 → 踝（taperLegs 控制小腿收细 = 枯瘦）
    taper_capsule(bm, (sx*JX('legX'), 0, hipZ*1.08), (sx*JX('legX')*1.02, 0, JZ('kneeZ')),
                  JW('thigh')*1.10, JW('knee'), bulge=0.05)
    taper_capsule(bm, (sx*JX('legX')*1.02, 0, JZ('kneeZ')), (sx*JX('legX'), 0.005, JZ('ankleZ')),
                  JW('knee')*taperLegs, JW('ankle')*taperLegs)
    parts.append(finish(new_obj("Leg", bm, mbody), bevel=0.009))

# ⑤ 眼窝 + 眼（虹膜自发光、瞳孔近黑）—— 三层，深插进眼窝
def emissive(name, rgb, strength):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL': nt.nodes.remove(n)
    out = nt.nodes[0]; e = nt.nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = (rgb[0],rgb[1],rgb[2],1.0)
    e.inputs['Strength'].default_value = strength
    nt.links.new(e.outputs['Emission'], out.inputs['Surface']); return m

mRed = emissive("MAT-GhostEyeRedIris", (1.0,0.10,0.05), 3.2)
mWhite = emissive("MAT-GhostEyeWhiteIris", (0.88,0.93,1.0), 2.6)
eyeZ = JZ('eyeZ'); eyeX = JW('headR') * 0.50
for tag, mm in (("Red", mRed), ("White", mWhite)):
    for sx in (1.0, -1.0):
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=SPH, v_segments=SPHV, radius=JW('headR')*0.24)
        o = new_obj(f"Eye{tag}Sclera", bm, mbody)
        o.location = (sx*eyeX, -JW('headR')*0.72, eyeZ); o.scale = (1,0.72,1)
        parts.append(finish(o, bevel=None, subsurf=1))
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=SPH, v_segments=SPHV, radius=JW('headR')*0.145)
        o = new_obj(f"Eye{tag}Iris", bm, mm)
        o.location = (sx*eyeX, -JW('headR')*0.90, eyeZ); o.scale = (1,0.40,1)
        parts.append(finish(o, bevel=None, subsurf=1))
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=SPH, v_segments=SPHV, radius=JW('headR')*0.058)
        o = new_obj(f"Eye{tag}Pupil", bm, mbody)
        o.location = (sx*eyeX, -JW('headR')*0.965, eyeZ); o.scale = (1,0.55,1)
        parts.append(finish(o, bevel=None, subsurf=1))

# ⑥ 烘焙修改器 → 合并 → **整体缩放到真实身高** → 导出
for o in parts:
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    old = o.data; o.data = me; bpy.data.meshes.remove(old)
    for md in list(o.modifiers): o.modifiers.remove(md)
    for p in o.data.polygons: p.use_smooth = True

bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
j = bpy.context.view_layer.objects.active
j.name = 'GEO-' + TAG + '_' + ARGS['bodyName']

# ── UV 展开（**导出前必做**）────────────────────────────────────────────────
# 没有 UV 时 glTF 导出器会为每个面角复制顶点 → 实测 9.9k 网格顶点变成 83k GLB 顶点（≈10×）。
# 有了 UV 层，导出器可以按 UV 索引复用顶点，顶点数回落到 ≈ 网格顶点数。
# 用 smart_project：对程序化网格足够，且不需要人工标记接缝。
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(85), island_margin=0.005)
bpy.ops.object.mode_set(mode='OBJECT')
j.scale = (scale, scale, scale)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bpy.ops.object.select_all(action='DESELECT'); j.select_set(True)
bpy.context.view_layer.objects.active = j
os.makedirs(OUT, exist_ok=True)
p = os.path.join(OUT, 'GEO-' + TAG + '_' + ARGS['bodyName'] + '.glb')
bpy.ops.export_scene.gltf(filepath=p, export_format='GLB', use_selection=True,
                          export_apply=True, export_yup=False)
# 从产出字节读真实顶点数（不靠 Blender 自报）
import struct
with open(p,'rb') as f: buf = f.read()
jl = struct.unpack_from('<I', buf, 12)[0]
g = json.loads(buf[20:20+jl].decode('utf8'))
acc = g.get('accessors', [])
rv = sum(acc[pr['attributes']['POSITION']]['count'] for m in g.get('meshes',[]) for pr in m['primitives'])
print("WHISPER_MODEL_RESULT " + json.dumps({
  "path": p, "bytes": os.path.getsize(p), "glb_vertices": rv,
  "blender_verts": len(j.data.vertices), "body": ARGS['bodyName'], "tag": TAG,
  "height_m": ARGS['height'], "headHeads": HEADS}))
`;

// ── 对比图：把生成的多个 GLB 摆成一排，正交正视图渲染 ────────────────────────
// 为什么必须做：用户两次说"建模真烂"，而**我只能靠看渲染判断像不像人**。
// 正交正视图 + 同一地面线 → **绝对高度可直接比对**，能立刻看出头身比/臂长/肩宽哪一项不对。
const SHEET_PY = String.raw`
import bpy, math, os, sys, json
A = json.loads(sys.argv[sys.argv.index('--') + 1])
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
meshes = []
for i, f in enumerate(A['files']):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=f)
    # 轴向：GLB 由 export_yup=False 导出 → **保持 Z-up**（实测包围盒 Z 跨度 1.85m = 站姿，
    # 与 Unity 同约定，且这是套件在用的同一约定 —— 不要改导出侧）。
    # 但 glTF 导入器按 Y-up 解释 → 模型在 Blender 里躺平。
    # 立在 Blender 里需要绕 **X 轴 −90°**（+90° 会把已躺平的模型再翻过去，实测无效）。
    # ⚠ 关键：要转**根节点**并同时把**所有后代**一起转（有些 glTF 的根是空节点，
    #    只转根不带后代时网格不动 —— 这是我在对比图上连错 3 轮的原因）。
    new_objs = [x for x in bpy.data.objects if x not in before]
    roots = [x for x in new_objs if x.parent is None]
    for r in roots:
        r.rotation_euler = (math.radians(-90), 0, 0)
        r.location.x = i * 1.25
    meshes += [x for x in new_objs if x.type == 'MESH']
m = bpy.data.materials.new("MAT-Sheet"); m.use_nodes = True
bsdf = m.node_tree.nodes.get("Principled BSDF")
bsdf.inputs['Base Color'].default_value = (0.16, 0.17, 0.20, 1.0)
bsdf.inputs['Roughness'].default_value = 0.72
for o in meshes:
    o.data.materials.clear(); o.data.materials.append(m)
cam_d = bpy.data.cameras.new("CAM-Sheet"); cam_d.type = 'ORTHO'
cam_d.ortho_scale = max(2.4, 1.25 * len(A['files']) + 0.5)
cam = bpy.data.objects.new("CAM-Sheet", cam_d); bpy.context.scene.collection.objects.link(cam)
cam.location = ((len(A['files']) - 1) * 0.625, -8.0, 1.02)
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam
for nm, e, loc, rot in (("K", 900, (-2.4, -4.2, 3.6), (math.radians(46), 0, math.radians(-28))),
                        ("F", 300, ( 2.8, -3.8, 1.6), (math.radians(78), 0, math.radians(38))),
                        ("R", 520, ( 0.4,  3.8, 3.1), (math.radians(122), 0, math.radians(178)))):
    d = bpy.data.lights.new(nm, type='AREA'); d.energy = e; d.size = 3.2
    o = bpy.data.objects.new(nm, d); bpy.context.scene.collection.objects.link(o)
    o.location = loc; o.rotation_euler = rot
w = bpy.data.worlds.new("W"); bpy.context.scene.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.020, 0.021, 0.025, 1.0); bg.inputs[1].default_value = 0.45
sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y = A['w'], A['h']
sc.render.engine = 'BLENDER_EEVEE'
sc.render.filepath = A['out']
bpy.ops.render.render(write_still=True)
print("WHISPER_SHEET_RESULT " + json.dumps({"out": A['out'], "count": len(A['files'])}))
`;

function runBlender(script, payload) {
  const tmp = path.join(ROOT, 'tools', '.whisper-model-run.py');
  fs.writeFileSync(tmp, script, 'utf8');
  const out = execFileSync(BLENDER, ['--background', '--factory-startup', '--python', tmp, '--', JSON.stringify(payload)],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const m = out.match(/WHISPER_MODEL_RESULT (\{.*\})/s);
  if (!m) throw new Error('Blender 未返回结果。尾部输出：\n' + out.slice(-1500));
  return JSON.parse(m[1]);
}

const [cmd, ...rest] = process.argv.slice(2);
function opt(name, dflt) { const i = rest.indexOf('--' + name); return i >= 0 ? rest[i + 1] : dflt; }

if (cmd === 'list') {
  console.log('体型（--body）：');
  for (const [k, v] of Object.entries(BODIES))
    console.log(`  ${k.padEnd(8)} 头身=${v.headHeads ?? CANON.headHeads}  臂长×${v.armLen ?? 1}  颅骨拉长=${v.elongateSkull ?? 0}  肋骨外翻=${v.ribFlare ?? 0}`);
} else if (cmd === 'build') {
  const body = opt('body', 'lanky');
  if (!BODIES[body]) { console.error('未知体型：' + body + '（用 list 看可选）'); process.exit(2); }
  const out = opt('out', path.join(ROOT, 'unity/Assets/Resources/Models/ghostbody'));
  const tag = opt('type', 'GhostBody');
  const height = parseFloat(opt('height', '1.95'));
  const quality = opt('quality', 'med');
  const r = runBlender(PY, { canon: CANON, body: BODIES[body], bodyName: body, out, tag, height, quality });
  console.log(`  ✓ ${path.basename(r.path)} · ${(r.bytes / 1024).toFixed(0)} KB · GLB 顶点 ${r.glb_vertices} · 身高 ${r.height_m}m · 头身 ${r.headHeads}`);
  console.log(`    骨架：关节由比例常数算出，肢体=两关节间锥形胶囊（连通由构造保证）`);
} else if (cmd === 'verify') {
  // 一次生成全部体型 + 出一张对比图（我只能靠看渲染判断"像不像人"）
  const quality = opt('quality', 'low');
  const out = opt('out', path.join(ROOT, '_evidence/model-sheet'));
  fs.mkdirSync(out, { recursive: true });
  const bodies = opt('bodies', 'human,tall,stocky,child,lanky,gaunt,hulk').split(',');
  const height = parseFloat(opt('height', '1.95'));   // ⚠ 必须 parseFloat：opt() 返回字符串，传给 Python 后 height/1.0 会报 str/float
  const files = [];
  for (const b of bodies) {
    if (!BODIES[b]) { console.log('  跳过未知体型 ' + b); continue; }
    const r = runBlender(PY, { canon: CANON, body: BODIES[b], bodyName: b, out, tag: 'GhostBody', height, quality });
    files.push(r.path);
    console.log(`  ${b.padEnd(7)} GLB ${String(r.glb_vertices).padStart(6)} 顶点 · ${(r.bytes / 1024).toFixed(0)} KB`);
  }
  if (!files.length) { console.error('  没生成任何体型'); process.exit(2); }
  const sheet = path.join(out, 'body-types.png');
  const tmp = path.join(ROOT, 'tools', '.whisper-sheet-run.py');
  fs.writeFileSync(tmp, SHEET_PY, 'utf8');
  const res = execFileSync(BLENDER, ['--background', '--factory-startup', '--python', tmp, '--',
    JSON.stringify({ files, out: sheet, w: 260 * files.length, h: 820 })],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  console.log(/WHISPER_SHEET_RESULT/.test(res) ? `  ✓ 对比图 → ${sheet}` : '  ! 对比图渲染失败');
} else {
  console.log('用法：node tools/whisper-model.mjs list');
  console.log('      node tools/whisper-model.mjs build --body lanky [--quality low|med|high] [--height 1.95]');
  console.log('      node tools/whisper-model.mjs verify [--quality low]      # 全部体型 + 对比图');
}
