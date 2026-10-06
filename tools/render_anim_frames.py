# render_anim_frames.py — 渲染带动画的 GLB 的若干帧，用来**目视确认关节真的在动、且没撕裂**
#
# 为什么需要：glTF 里"有 17 个采样"只证明数据在，不证明**看起来对**
# （绑定权重错、关节反折、部件错位都可能数据完整而视觉崩坏）。
# 本项目铁律：门禁全绿不算通过 —— 动画必须看图。
#
# 用法：blender -b --factory-startup --python render_anim_frames.py -- <glb> <outdir> <frame1,frame2,...>
import bpy, os, sys, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
FRAMES = [int(x) for x in argv[2].split(",")] if len(argv) > 2 else [1, 5, 9, 13]
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith('GEO-')]
if not meshes:
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
allco = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
mn = Vector((min(p[i] for p in allco) for i in range(3)))
mx = Vector((max(p[i] for p in allco) for i in range(3)))
center = (mn + mx) * 0.5
size = mx - mn
m = max(size)

sc = bpy.context.scene
# 引擎标识符随版本变（5.0 是 'BLENDER_EEVEE'，4.x 曾是 'BLENDER_EEVEE_NEXT'）
# ⇒ **探测式选择**，不要硬编码（我第一版就硬编码了 4.x 的名字，直接报 enum 找不到）
_engines = [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
for _want in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE', 'CYCLES'):
    if _want in _engines:
        sc.render.engine = _want
        break
sc.render.resolution_x = 360
sc.render.resolution_y = 420
sc.render.film_transparent = False
# 世界（避免纯黑）
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.08, 0.09, 0.11, 1)

# 相机：侧前方 3/4 视角（走路姿态最能看出来）
cam_d = bpy.data.cameras.new("C"); cam_d.type = 'ORTHO'; cam_d.ortho_scale = m * 1.5
cam = bpy.data.objects.new("C", cam_d); sc.collection.objects.link(cam)
cam.location = (center.x + m * 1.6, center.y - m * 1.6, center.z + m * 0.25)
cam.rotation_euler = (math.radians(84), 0, math.radians(45))
sc.camera = cam

# 三点光
for i, (dx, dy, dz) in enumerate([(1.5, -1.5, 1.2), (-1.5, -1.0, 0.6), (0, 1.5, 0.5)]):
    ld = bpy.data.lights.new("L%d" % i, type='SUN'); ld.energy = 4.0
    lo = bpy.data.objects.new("L%d" % i, ld)
    lo.location = (center.x + dx * m * 2, center.y + dy * m * 2, center.z + dz * m * 2)
    lo.rotation_euler = (math.radians(50), 0, math.radians(40 + i * 45))
    sc.collection.objects.link(lo)

# 地面参考线（判定脚是否穿地 / 是否离地）
bpy.ops.mesh.primitive_plane_add(size=m * 3, location=(center.x, center.y, 0.0))

for f in FRAMES:
    sc.frame_set(f)
    sc.render.filepath = os.path.join(OUT, "frame_%03d.png" % f)
    bpy.ops.render.render(write_still=True)
print("ANIM_FRAMES_DONE=%d" % len(FRAMES))
