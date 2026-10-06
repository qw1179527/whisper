# hall_props_threeview.py — 给 10 件大厅工业道具出**正/左/俯 三视图 + 客观读数**
#
# ══════════════════════════════════════════════════════════════════════════════
# 为什么要做这件事（交接文档里的一条死结）
# ══════════════════════════════════════════════════════════════════════════════
# `HallScene.Furnishing.cs` 的自述（原文）：
#   > 这套货架两次都没摆对，已回退 Box…我不知道这批构件的【原点与朝向语义】，
#   > 只验证了包围盒，没验证"哪一端是它的连接面"
# 这是"六重断链"的第 6 环，也是**唯一一环靠读代码解决不了**的：
# 锚点在 Blender 里被烘进网格，代码里没有任何一行说明"哪一端是连接面"。
# ⇒ 只能**看图**。本脚本批量出三视图 + 打印客观读数（包围盒/最大轴/原点偏移）。
#
# ══════════════════════════════════════════════════════════════════════════════
# 关键前提：Blender 的 glTF 导入会**自动把 Y-up 转回 Z-up**
#   （glTF 规范要求 Y-up，Blender 是 Z-up，导入时 `bpy.ops.import_scene.gltf` 默认做转换）
# ⇒ 本脚本渲染出的"上"就是**创作时的 +Z**。若某件道具在渲染里是躺着的，
#   说明它导出时没走 yup 转换（正是建模知识文档里记的"4 件仍 Z-up"）。
#
# 用法：blender -b --factory-startup --python hall_props_threeview.py -- <glb目录> <输出目录>
import bpy, os, sys, json, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = argv[0] if argv else "/tmp/hallprops"
OUT = argv[1] if len(argv) > 1 else "/tmp/hallprops_out"
os.makedirs(OUT, exist_ok=True)

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def add_ortho_cam(name, loc, rot, scale):
    cam_data = bpy.data.cameras.new(name)
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = scale
    cam = bpy.data.objects.new(name, cam_data)
    cam.location = loc
    cam.rotation_euler = rot
    bpy.context.scene.collection.objects.link(cam)
    return cam

def render(path, cam):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)

report = {}
files = sorted(f for f in os.listdir(SRC) if f.endswith(".glb"))
for f in files:
    name = f[:-4]
    clear()
    try:
        bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, f))
    except Exception as e:
        report[name] = {"error": str(e)}
        continue

    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        report[name] = {"error": "导入后没有网格"}
        continue

    # 世界包围盒（含所有网格）
    mn = Vector((1e9, 1e9, 1e9)); mx = Vector((-1e9, -1e9, -1e9))
    for o in meshes:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            for i in range(3):
                mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
    size = mx - mn
    center = (mn + mx) * 0.5

    # 客观读数：尺寸、最大轴、原点相对几何中心/底面的偏移
    axes = ["X", "Y", "Z"]
    longest = axes[max(range(3), key=lambda i: size[i])]
    report[name] = {
        "size": [round(v, 4) for v in size],
        "min": [round(v, 4) for v in mn],
        "max": [round(v, 4) for v in mx],
        "origin_offset_from_bbox_min": [round(mn[i], 4) for i in range(3)],
        "origin_offset_from_center": [round(-center[i], 4) for i in range(3)],
        "longest_axis": longest,
        # 底面是否落在 z=0（套件约定：底面贴地）
        "bottom_at_z0": abs(mn.z) < 0.02,
    }

    # 光照：三点 + 世界背景（正交三视图只要能看清轮廓与朝向）
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in \
        [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_WORKBENCH'
    sc.render.resolution_x = 480
    sc.render.resolution_y = 480
    sc.render.film_transparent = False
    world = bpy.data.worlds.new("W"); sc.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.05, 0.06, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    for i, (dx, dy, dz) in enumerate([(1, -1, 1), (-1, -1, 0.6), (0, 1, 0.4)]):
        ld = bpy.data.lights.new("L%d" % i, type='SUN')
        ld.energy = 3.0
        lo = bpy.data.objects.new("L%d" % i, ld)
        lo.location = (center.x + dx * 5, center.y + dy * 5, center.z + dz * 5)
        lo.rotation_euler = (math.radians(55), 0, math.radians(35 + i * 40))
        sc.collection.objects.link(lo)

    m = max(size) if max(size) > 0 else 1.0
    pad = m * 1.25
    d = m * 3
    # 正视图（从 -Y 看）：看 XZ 平面
    cam = add_ortho_cam("cam_front", (center.x, center.y - d, center.z),
                        (math.radians(90), 0, 0), pad)
    render(os.path.join(OUT, name + "_front.png"), cam)
    # 左视图（从 -X 看）：看 YZ 平面
    cam = add_ortho_cam("cam_left", (center.x - d, center.y, center.z),
                        (math.radians(90), 0, math.radians(-90)), pad)
    render(os.path.join(OUT, name + "_left.png"), cam)
    # 俯视图（从 +Z 往下看）：看 XY 平面
    cam = add_ortho_cam("cam_top", (center.x, center.y, center.z + d),
                        (0, 0, 0), pad)
    render(os.path.join(OUT, name + "_top.png"), cam)

print("HALL_PROPS_THREEVIEW=" + json.dumps(report, ensure_ascii=False))
print("PROPS_DONE=%d" % len(files))
