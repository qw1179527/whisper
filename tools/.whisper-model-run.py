
import bpy, bmesh, math, json, os, sys
from mathutils import Vector

ARGS = json.loads(sys.argv[sys.argv.index('--') + 1])
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
H = 1.0
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
        if i == 0: r = r0 * 0.55
        if i == rings: r = r1 * 0.55
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
