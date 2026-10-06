# measure_kit_heights.py — 量出每个套件的**真实竖向范围**（哪个轴是"上"由数据说了算）
#
# ══════════════════════════════════════════════════════════════════════════════
# 为什么必须用 Blender 量，而不是读 GLB 的 accessor
# ══════════════════════════════════════════════════════════════════════════════
# 【2026-10-06 我在这上面栽了一整条错误链路，代价是 5 轮 CI】
# 我原先直接读 GLB 里 POSITION accessor 的 min/max，并**假定 glTF 是 Y-up**
# ⇒ 把 `morgue` 的高度读成 **1.69m**（那是它的水平进深）。
# 由此连锁出错：
#   · `kit-heights.json` 里 33 个套件的高度**全是错的**（量的是水平尺寸）
#   · 新增的"房间↔套件高度落差审计"据此报了 3 个**我自己造的**缺陷
#   · 为了绕开那个假高度，我把取证相机放到 y=0.30（荒谬的高度）
#   · 而 morgue 纯黑的真因始终没被碰到
#
# 真相（Blender 实测 + 部件命名交叉验证）：
#   `skirt_*`（踢脚线）Z∈[-0.01,0.10]、`cornice_*`（顶角线）Z∈[3.02,3.14]、
#   `ceiling` Z∈[3.08,3.19] ⇒ **垂直轴是 Blender 的 Z，套件高 3.30m**，
#   与房间 DSL 声明的 3.2m 基本吻合。
#
# ⇒ 教训：**"哪个轴是上"这件事必须由数据/图证明，不能靠约定假定**；
#   而且量完要**交叉验证**（这次是靠部件命名——skirt 在底、ceiling 在顶——才发现的）。
#
# Blender 的 glTF 导入会把 glTF 坐标转成 Blender 的 Z-up，
# 所以取 `matrix_world @ v.co` 的 **Z** 范围即为"真实高度"（世界空间）。
#
# 用法：blender -b --factory-startup --python measure_kit_heights.py -- <glb1> <glb2> ... 
#   输出：KIT_HEIGHTS={...}  一行 JSON，可直接写进 Assets/Data/kit-heights.json
import bpy, sys, json, os

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out = {}
for path in argv:
    kit = os.path.basename(path).replace('.glb', '').replace('.bytes', '')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        bpy.ops.import_scene.gltf(filepath=path)
    except Exception as e:
        out[kit] = {"error": str(e)}
        continue
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        out[kit] = {"error": "导入后没有网格"}
        continue
    xs, ys, zs = [], [], []
    for o in meshes:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            xs.append(w.x); ys.append(w.y); zs.append(w.z)
    # 竖向 = 世界 Z（Blender Z-up）。同时记下三个轴的范围，便于人工复核"到底哪个是上"。
    out[kit] = {
        "up_axis": "Z",
        "yMin": round(min(zs), 4),      # 兼容既有字段名：yMin/yMax 现在表示**竖向**
        "yMax": round(max(zs), 4),
        "xRange": [round(min(xs), 4), round(max(xs), 4)],
        "yRange": [round(min(ys), 4), round(max(ys), 4)],
        "height": round(max(zs) - min(zs), 4),
    }

print("KIT_HEIGHTS=" + json.dumps(out, ensure_ascii=False))
