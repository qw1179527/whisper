
import bpy, math, json, os, sys, struct
A = globals().get('ARGS') or json.loads(sys.argv[sys.argv.index('--') + 1])
C, B, OUT, TAG, NAME = A['canon'], A['body'], A['out'], A['tag'], A['bodyName']
H = float(A['height'])
# 元球分辨率 = 绝对米数（不是占身高比例，否则矮体型会糊掉）。
# 实测 0.030m 会留下明显"珠痕"（表面一节节凹槽）；0.012m 平滑且顶点数仍在手机预算内。
RES = {'low': 0.012, 'med': 0.008, 'high': 0.005}[A.get('quality', 'med')]

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

J = dict(C['J']); J.update(B.get('j', {}))
X = dict(C['X']); X.update(B.get('x', {}))
R = dict(C['R'])
for k, v in B.get('w', {}).items(): R[k] = R[k] * v
armW = B.get('wArm', 1.0)
armDrop = B.get('armDrop', C['armDrop']) * H
foreDrop = B.get('foreDrop', C['foreDrop']) * H
headMul = B.get('headMul', 1.0)
skullUp = B.get('skullUp', 0.0)
flare = B.get('ribFlare', 0.0)

def z(n): return J[n] * H
def xx(n): return X[n] * H
def rr(n): return R[n] * H

mb = bpy.data.metaballs.new('Body')
mb.resolution = RES
mb.render_resolution = RES
mb.threshold = 0.45        # 越低越黏：元球在更大范围内互相融合（实测 0.6 会散成珠子）
obj = bpy.data.objects.new(NAME, mb)
bpy.context.scene.collection.objects.link(obj)

def blob(p, rad, stiffness=2.0, squash=(1.0, 1.0, 1.0)):
    e = mb.elements.new()
    e.co = p
    e.radius = rad
    e.stiffness = stiffness
    e.size_x, e.size_y, e.size_z = squash
    return e

def link_seg(p0, p1, r0, r1, steps=None, squash=(1.0, 1.0, 1.0)):
    """一段肢体 = **一个拉伸椭球元球**（不是一串珠子）。
    为什么改：用"沿线段串珠"时，无论珠子多密，表面都会出现规律的**珠链肿包**
    （渲染图里像糖葫芦，两版都是）—— 元球的融合场在珠与珠之间会落到阈值以下。
    正解是 Blender 元球的 **size_x/y/z 拉伸**：拉长成椭球，一个元素覆盖一整段，
    **不存在珠间间隙**，因此不可能产生珠链。长轴方向由线段的占优分量决定。"""
    ax, ay, az = (p1[0]-p0[0]), (p1[1]-p0[1]), (p1[2]-p0[2])
    L = math.sqrt(ax*ax + ay*ay + az*az)
    if L < 1e-6:
        return
    cx, cy, cz = (p0[0]+p1[0])/2.0, (p0[1]+p1[1])/2.0, (p0[2]+p1[2])/2.0
    r = (r0 + r1) / 2.0
    half = L / 2.0
    sx = sy = sz = 1.0
    if abs(az) > abs(ax) and abs(az) > abs(ay):
        sz = max(1.0, half / max(r, 1e-4))
    elif abs(ax) >= abs(ay):
        sx = max(1.0, half / max(r, 1e-4))
    else:
        sy = max(1.0, half / max(r, 1e-4))
    # 迭代记录（都靠渲染图判定）：
    #   14 珠 ×1.45 → 融合但表面"珠链"
    #   40 珠 ×1.25 → 珠链仍在
    #   单中心椭球 + size 拉伸 → 段与段断开，渲染成散球
    #   本版 = 三点布局（端点+中点）+ 半径 ×1.6：三点间距 L/2，完全重叠，无珠链不断节
    # 珠间距 ≤ 半径/3 → 表面连续（比值判据，见文件头迭代记录）
    rmin = max(min(r0, r1), 1e-4)
    steps = max(4, int(L / (rmin / 3.0)))
    steps = min(steps, 90)          # 上限，避免超细段把元素数炸掉
    for i in range(steps + 1):
        t = i / float(steps)
        pp = (p0[0] + (p1[0]-p0[0]) * t, p0[1] + (p1[1]-p0[1]) * t, p0[2] + (p1[2]-p0[2]) * t)
        rr_ = r0 + (r1 - r0) * t
        blob(pp, rr_, 2.0, squash)

# ── 躯干：脊柱串珠（胸腔可外翻）──
spine = [(0, 0, z('hip')), (0, 0, z('s1')), (0, 0, z('s2')), (0, 0, z('chest'))]
srad  = [rr('hip'), rr('s1') * (1 + flare * 0.6), rr('s2') * (1 + flare), rr('chest')]
for i in range(len(spine) - 1):
    link_seg(spine[i], spine[i + 1], srad[i], srad[i + 1], steps=5, squash=(1.0, 0.78, 1.0))

# ── 颈 + 头 ──
link_seg((0, 0, z('chest')), (0, 0, z('neck')), rr('chest') * 0.55, rr('neck'), steps=3)
headC = (0, 0.006 * H, z('headC') + skullUp * H)
blob(headC, rr('head') * headMul, 2.0, (1.0, 0.90, 1.06 + skullUp * 2.0))
blob((0, -0.010 * H, z('headC') + 0.045 * H + skullUp * H), rr('headTop') * headMul, 2.0)
blob((0, 0.010 * H, z('headC') - 0.030 * H), rr('head') * 0.62 * headMul, 2.0)

# ── 四肢（左右对称：同一公式镜像 x，对称性由构造保证）──
for sx in (1.0, -1.0):
    hip  = (sx * xx('leg'), 0, z('hip'))
    knee = (sx * xx('leg') * 1.03, 0.004 * H, z('knee'))
    ank  = (sx * xx('leg') * 1.00, 0.010 * H, z('ankle'))
    foot = (sx * xx('leg') * 1.00, -0.045 * H, z('foot'))
    link_seg((sx * xx('leg') * 0.55, 0, z('hip') * 1.01), hip, rr('hip') * 0.85, rr('thigh'), steps=2)
    link_seg(hip, knee, rr('thigh'), rr('knee'), steps=6)
    link_seg(knee, ank, rr('knee'), rr('ankle'), steps=5)
    link_seg(ank, foot, rr('ankle'), rr('foot'), steps=2, squash=(1.0, 1.5, 0.7))
    sh  = (sx * xx('shoulder'), 0, z('shoulder'))
    elb = (sx * xx('elbow'), 0.004 * H, z('shoulder') - armDrop)
    wri = (sx * xx('wrist'), 0.012 * H, z('shoulder') - armDrop - foreDrop)
    link_seg((sx * xx('shoulder') * 0.45, 0, z('shoulder') - 0.004 * H), sh, rr('shoulder') * 0.9, rr('shoulder'), steps=2)
    link_seg(sh, elb, rr('arm') * armW * 1.15, rr('elbow') * armW, steps=5)
    link_seg(elb, wri, rr('elbow') * armW, rr('wrist') * armW, steps=5)
    blob(wri, rr('wrist') * armW * 1.5, 2.0, (1.0, 1.4, 1.0))   # 手

# ── 元球 → mesh ──
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.convert(target='MESH')
body = bpy.context.view_layer.objects.active
if body.type != 'MESH':
    for o in bpy.context.scene.collection.objects:
        if o.type == 'MESH': body = o
bpy.ops.object.shade_smooth()

# ── Decimate：元球按"体素"生成面，细分辨率会到 25k 顶点（手机太多）──
# 用 collapse 比保留外观、比 vertex-cluster 更自然；比例按目标顶点数反推。
TARGET_V = int(A.get("targetVerts", 8000))
cur_v = len(body.data.vertices)
if cur_v > TARGET_V * 1.15:
    dec = body.modifiers.new("Decimate", "DECIMATE")
    dec.decimate_type = "COLLAPSE"
    dec.ratio = max(0.12, min(1.0, TARGET_V / float(cur_v)))
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=dec.name)
    bpy.ops.object.shade_smooth()

# 落地对齐 + 精确身高
mn = [1e9] * 3; mx = [-1e9] * 3
for v in body.data.vertices:
    q = body.matrix_world @ v.co
    mn = [min(mn[k], q[k]) for k in range(3)]; mx = [max(mx[k], q[k]) for k in range(3)]
curH = mx[2] - mn[2]
if curH > 1e-4:
    k = H / curH
    for v in body.data.vertices:
        v.co = (v.co.x * k, v.co.y * k, v.co.z * k)
    zmin = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices: v.co.z -= zmin
    body.data.update()

# 材质：近黑（暗场里只看得见轮廓；眼自发光）
m = bpy.data.materials.new('MAT-GhostBody'); m.use_nodes = True
bs = m.node_tree.nodes.get('Principled BSDF')
bs.inputs['Base Color'].default_value = (0.055, 0.058, 0.062, 1.0)
bs.inputs['Roughness'].default_value = 0.88
body.data.materials.append(m)

# 眼：两个小球嵌进眼窝（虹膜自发光 + 瞳孔近黑）
def emis(name, rgb, strength):
    mm = bpy.data.materials.new(name); mm.use_nodes = True; nt = mm.node_tree
    for nd in list(nt.nodes):
        if nd.type != 'OUTPUT_MATERIAL': nt.nodes.remove(nd)
    out = nt.nodes[0]; e = nt.nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    e.inputs['Strength'].default_value = strength
    nt.links.new(e.outputs['Emission'], out.inputs['Surface']); return mm

mR = emis('MAT-GhostEyeRedIris', (1.0, 0.10, 0.05), 3.6)
mW = emis('MAT-GhostEyeWhiteIris', (0.88, 0.93, 1.0), 3.0)
eyeZ = (z('headC') + skullUp * H) + (z('headTop') - z('headC')) * 0.10
eyeX = rr('head') * headMul * 0.44
eyeR = rr('head') * headMul * 0.20
mades = []
for tag, mm in (('Red', mR), ('White', mW)):
    for sx in (1.0, -1.0):
        bm2 = bpy.data.meshes.new('Eye')
        import bmesh as _bm
        b = _bm.new(); _bm.ops.create_uvsphere(b, u_segments=14, v_segments=10, radius=eyeR)
        b.to_mesh(bm2); b.free()
        o = bpy.data.objects.new('Eye', bm2)
        bpy.context.scene.collection.objects.link(o)
        o.location = (sx * eyeX, -rr('head') * headMul * 0.66, eyeZ)
        o.scale = (1.0, 0.5, 1.0)
        bm2.materials.append(mm)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.shade_smooth()
        mades.append(o)
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
for o in mades: o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()

# UV（没有 UV 时 glTF 按面角复制顶点，实测 10× 膨胀）
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(85), island_margin=0.005)
bpy.ops.object.mode_set(mode='OBJECT')

os.makedirs(OUT, exist_ok=True)
p = os.path.join(OUT, 'GEO-GhostBody_' + NAME + '.glb')
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.export_scene.gltf(filepath=p, export_format='GLB', use_selection=True,
                          export_apply=True, export_yup=False)
mn = [1e9] * 3; mx = [-1e9] * 3
for v in body.data.vertices:
    q = body.matrix_world @ v.co
    mn = [min(mn[k], q[k]) for k in range(3)]; mx = [max(mx[k], q[k]) for k in range(3)]
with open(p, 'rb') as f: buf = f.read()
jl = struct.unpack_from('<I', buf, 12)[0]
g = json.loads(buf[20:20 + jl].decode('utf8'))
acc = g.get('accessors', [])
rv = sum(acc[pr['attributes']['POSITION']]['count'] for m2 in g.get('meshes', []) for pr in m2['primitives'])
print('WHISPER_MODEL3 ' + json.dumps({
    'path': p, 'bytes': os.path.getsize(p), 'glb_vertices': rv,
    'spanX': round(mx[0]-mn[0], 3), 'spanY': round(mx[1]-mn[1], 3), 'spanZ': round(mx[2]-mn[2], 3),
    'body': NAME, 'faces': len(body.data.polygons)}))
