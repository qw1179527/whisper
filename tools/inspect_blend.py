# inspect_blend.py — 体检一个 .blend：对象/网格/骨骼/顶点组/材质/尺寸
#
# 为什么需要它：要做"真人级动作"就必须先知道现有模型**有没有可绑定的拓扑**、
# 部件是如何切分的（本项目的人形是**分部件**的：躯干/头/四肢各一个网格），
# 这直接决定绑骨策略（分部件 vs 整体蒙皮）。
#
# 用法：blender -b <file.blend> --python inspect_blend.py -- <可选：只看这些对象>
import bpy, json, sys

report = {"objects": [], "armatures": [], "actions": []}

for o in bpy.data.objects:
    row = {"name": o.name, "type": o.type}
    if o.type == 'MESH':
        me = o.data
        row.update({
            "verts": len(me.vertices),
            "polys": len(me.polygons),
            "tris": sum(len(p.vertices) - 2 for p in me.polygons),
            "materials": [m.name if m else None for m in me.materials],
            "vertex_groups": [g.name for g in o.vertex_groups],
            "has_armature_mod": any(m.type == 'ARMATURE' for m in o.modifiers),
            "uv_layers": [u.name for u in me.uv_layers],
            "shape_keys": (len(me.shape_keys.key_blocks) if me.shape_keys else 0),
        })
        # 局部包围盒（判断部件在身体哪个位置 —— 绑骨要靠它定关节高度）
        bb = [tuple(round(c, 4) for c in v) for v in o.bound_box]
        row["bbox_local_min"] = [round(min(p[i] for p in bb), 4) for i in range(3)]
        row["bbox_local_max"] = [round(max(p[i] for p in bb), 4) for i in range(3)]
        # 世界坐标下的 Z 范围（Blender 是 Z-up；站姿模型的最低点应是脚底）
        wz = [(o.matrix_world @ v.co).z for v in me.vertices[:1]] if len(me.vertices) else []
        row["origin"] = [round(c, 4) for c in o.location]
    if o.type == 'ARMATURE':
        row["bones"] = [b.name for b in o.data.bones]
    report["objects"].append(row)

for a in bpy.data.armatures:
    report["armatures"].append({"name": a.name, "bones": len(a.bones)})
for act in bpy.data.actions:
    report["actions"].append({"name": act.name, "frames": list(act.frame_range)})

# 材质细节：有没有贴图接进 Base Color / Normal
mats = []
for m in bpy.data.materials:
    if not m.use_nodes:
        continue
    tex = []
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image:
            tex.append({"image": n.image.name, "size": list(n.image.size)})
    mats.append({"name": m.name, "images": tex})
report["materials"] = mats

print("BLEND_INSPECT=" + json.dumps(report, ensure_ascii=False))
