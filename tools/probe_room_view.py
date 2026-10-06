# probe_room_view.py — 在 Blender 里**复现取证相机的那一次取景**，用来定位"纯黑"
#
# ══════════════════════════════════════════════════════════════════════════════
# 为什么做这个（不依赖 CI 的验证通道）
# ══════════════════════════════════════════════════════════════════════════════
# `morgue_deep/eye` 在云端取证里一直**纯黑（亮度 0.0 · 颜色数 1）**，
# 我为它改了 5 次相机（离墙距离 → 放进室内 → 高度 1.6 → 按套件几何定高 → …）都没碰到真因。
# 每次改都要等约 12 分钟 CI，代价极高。
#
# 而**本机 Blender 就能复现同一取景**：我手上有套件 GLB 与所有实测坐标，
# 只需按 `LevelBuilder` 的同一口径摆放（套件中心对齐房间中心、底面在房间地面高度），
# 再把相机放到取证脚本算出的那个位姿 ⇒ **几张图就能看清"相机到底看到了什么"**。
#
# ⚠ 这不是"替代产品渲染"：它只复现**几何+取景**，不含产品着色器/URP 后处理/灯光系统。
#   因此它能回答"是不是几何/取景的问题"，**不能**回答"产品着色器对不对"。
#   对本次的目的（相机看到了什么）恰好够用 —— 这一点必须写清楚，不能当成产品证据。
#
# 用法：
#   blender -b --factory-startup --python probe_room_view.py -- \
#       <kit.glb> <roomMinX> <roomMinZ> <roomSizeX> <roomSizeY> <roomSizeZ> \
#       <camX> <camY> <camZ> <lookX> <lookY> <lookZ> <out.png>
import bpy, sys, math, os
from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
KIT = a[0]
RMINX, RMINZ = float(a[1]), float(a[2])
RSX, RSY, RSZ = float(a[3]), float(a[4]), float(a[5])
# ⚠ **坐标轴转换是这里最容易错的一步**（我第一版就错了，白出一张空图）：
#   调用方按**产品/取证口径**给 (x, y=高度, z)；而 Blender 是 **Z-up** ⇒ 必须映射为 (x, z, y)。
#   不转换的后果：相机被放到"高度 = 产品的 z"，直接飞出房间 ⇒ 渲出一张近空的图，
#   而**它看起来像"房间真的没几何"** —— 又一个"错的诊断比没有诊断更糟"。
def U2B(v):
    return Vector((v[0], v[2], v[1]))
CAM = U2B((float(a[6]), float(a[7]), float(a[8])))
LOOK = U2B((float(a[9]), float(a[10]), float(a[11])))
OUT = a[12]

bpy.ops.wm.read_factory_settings(use_empty=True)

# ── 摆套件：与 LevelBuilder 同口径 ──────────────────────────────────────────
# `BuildRoom` 把 `Room_<id>` 放在 (CenterX, Floor*3.5, CenterZ)，套件部件 localPosition=0。
# 但套件 GLB 的顶点坐标**是相对房间最小角点的**（实测：x∈[0,4] 对应房间 x[0,4]），
# 所以这里按"房间最小角点 + 地面高度"摆，与产品一致。
bpy.ops.import_scene.gltf(filepath=KIT)
cx, cz = RMINX + RSX * 0.5, RMINZ + RSZ * 0.5

# ⚠ **按实测包围盒对齐，不假定局部原点语义**（我又在这上面错过一次）
# 实测 `morgue` 套件的局部包围盒 = X[19,21] Y[-1.50,1.50] Z[2.90,6.20]：
#   · X 有 19 的偏移（不是"从 0 开始"）
#   · Y 居中（不是"从 minZ 开始"）
#   · Z（高度）从 2.90 起 —— 也就是说**这个 GLB 的原点含一个楼层高度偏移**
# ⇒ 任何"按最小角点摆"或"按中心摆"的假定都会错。正确做法：
#   把套件盒的**中心**对到房间中心、**最低点**对到地面。这与原点语义无关。
def kit_bounds():
    vs = [o.matrix_world @ v.co
          for o in bpy.context.scene.objects if o.type == 'MESH'
          for v in o.data.vertices]
    mn = Vector((min(p.x for p in vs), min(p.y for p in vs), min(p.z for p in vs)))
    mx = Vector((max(p.x for p in vs), max(p.y for p in vs), max(p.z for p in vs)))
    return mn, mx

mn, mx = kit_bounds()
# Blender 里：X=房间 x，Y=房间 z（深度），Z=高度。房间地面在 Blender z = 0（floor 0）。
shift = Vector((cx - (mn.x + mx.x) * 0.5, cz - (mn.y + mx.y) * 0.5, 0.0 - mn.z))
for o in list(bpy.context.scene.objects):
    if o.type == 'MESH':
        o.location = o.location + shift
print("KIT_SHIFT=%.3f,%.3f,%.3f  (套件盒对齐到房间中心/地面)" % (shift.x, shift.y, shift.z))

# ── 地面参照（便于判断"相机在地上还是在天上"）────────────────────────────────
bpy.ops.mesh.primitive_plane_add(size=40, location=(cx, 0.0, cz))

# ── 光照：够亮即可（这不是产品光照，只为看清几何）──────────────────────────
w = bpy.data.worlds.new("W"); bpy.context.scene.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.05, 0.06, 1)
for i, (dx, dy, dz) in enumerate([(1, -1, 1.2), (-1, -1, 0.8), (0, 1, 0.6)]):
    ld = bpy.data.lights.new("L%d" % i, type='SUN'); ld.energy = 4.0
    lo = bpy.data.objects.new("L%d" % i, ld)
    lo.location = (cx + dx * 8, 6 + dz * 4, cz + dy * 8)
    lo.rotation_euler = (math.radians(45), 0, math.radians(30 + i * 50))
    bpy.context.scene.collection.objects.link(lo)

# ── 相机：**完全照取证脚本的位姿**（FOV 70 / near 0.05，与产品同口径）──────
cd = bpy.data.cameras.new("C"); cd.lens_unit = 'FOV'; cd.angle = math.radians(70.0)
cam = bpy.data.objects.new("C", cd); bpy.context.scene.collection.objects.link(cam)
cam.location = CAM
# Blender 是 Z-up；取证给的是 Y-up 的 (x, y=高度, z)。这里相机位置已按 Blender 坐标传入。
d = LOOK - CAM
cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
cam.data.clip_start = 0.05
sc = bpy.context.scene
sc.camera = cam
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in \
    [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x = 640
sc.render.resolution_y = 400
os.makedirs(os.path.dirname(OUT), exist_ok=True)
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)

# 客观读数：相机与套件包围盒的关系（回答"相机在不在几何里"）
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name != 'Plane']
xs, ys, zs = [], [], []
for o in meshes:
    for v in o.data.vertices:
        p = o.matrix_world @ v.co
        xs.append(p.x); ys.append(p.y); zs.append(p.z)
print("PROBE={\"kit\":\"%s\",\"kitX\":[%.2f,%.2f],\"kitY\":[%.2f,%.2f],\"kitZ\":[%.2f,%.2f],"
      "\"cam\":[%.2f,%.2f,%.2f],\"insideX\":%s,\"insideDepth\":%s,\"insideHeight\":%s}"
      % (os.path.basename(KIT), min(xs), max(xs), min(ys), max(ys), min(zs), max(zs),
         CAM.x, CAM.y, CAM.z,
         str(min(xs) <= CAM.x <= max(xs)).lower(),
         str(min(ys) <= CAM.y <= max(ys)).lower(),
         str(min(zs) <= CAM.z <= max(zs)).lower()))
