
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
obj = bpy.data.objects.new('GEO-GhostBody_hulk', me)
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
