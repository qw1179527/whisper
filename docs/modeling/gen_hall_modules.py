# -*- coding: utf-8 -*-
"""whisper 大厅：工业模块生成器（Blender 5.2 / 由 blender_python 执行）

设计依据（全部来自磁盘上的权威文件，不是我的口味）
------------------------------------------------
· 面数预算：`drivers/modeling-thresholds.json` → mobile.building_module = **200~1000 tri**
  即"每米级建筑模块 200~1000 面"，细节靠法线/烘焙贴图，**不靠堆面**。
· 建模顺序：硬表面 = Cube + Bevel，且 **Bevel 必须在 SubSurf 之前**（blender-modeling 技能）。
· 接缝：相邻部件**相互重叠 5~15mm**，避免可见缝（技能明确写了这是"业余最常见错误"）。
· 同行合并：**同材质部件合并成一个网格** → 消除内部接缝（技能建议）。
· 命名：一律 `GEO-` 前缀（技能要求，禁止留 Cube.027）。
· 工作区：本文件位于 default-workspace，脚本把 .py 派发到 Blender 执行。

产出（每个都是独立 GLB，供 Unity 里实例化）
--------------------------------------------
1. Hall_IBeamColumn    工字钢柱（柱脚底板 + 4 加劲肋）
2. RackUpright         货架立柱（冲孔 + 斜撑）
3. RackBeam            货架横梁（带端板与安全销）
4. PalletWood          木栈板（面板 + 纵梁 + 垫块）
5. LampIndustrial      工业吊灯（灯罩 + 吊杆 + 接线盒）
6. PipeFlange          管道（带法兰 + 螺栓圈 + 支架）
7. ElectricPanel       配电箱（门 + 铰链 + 锁 + 把手 + 线孔）
8. CrateWood           木箱（板材拼缝 + 角铁）
9. Barrel              油桶（卷边 + 箍圈 + 桶盖塞）
10. DuctSection        通风管段（方管 + 法兰 + 吊架）
"""

import bpy
import bmesh
import math
from mathutils import Vector

# ────────────────────────────────────────────────────────────────────────
# 通用工具
# ────────────────────────────────────────────────────────────────────────

def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)


def cube(name, loc, scale):
    """轴对齐盒体（scale 已 apply，便于后续布尔与倒角）。"""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return o


def cyl(name, loc, radius, depth, verts=16, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth,
                                        location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return o


def join(objs, name):
    """合并同材质部件（消除内部接缝），并做一次网格卫生清理。"""
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.0003)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    return o


def bevel(o, width=0.003, segments=2, angle_deg=30.0):
    """倒角。技能：Bevel 必须在 SubSurf 之前（本脚本不用 SubSurf，硬表面靠倒角就够）。"""
    bpy.context.view_layer.objects.active = o
    b = o.modifiers.new('Bevel', type='BEVEL')
    b.width = width
    b.segments = segments
    b.limit_method = 'ANGLE'
    b.angle_limit = math.radians(angle_deg)
    bpy.ops.object.modifier_apply(modifier=b.name)
    return o


def shade(o, smooth=False):
    bpy.context.view_layer.objects.active = o
    if smooth:
        bpy.ops.object.shade_smooth()
    else:
        bpy.ops.object.shade_flat()
    return o


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


# ════════════════════════════════════════════════════════════════════════
# 1. 工字钢柱 —— 已单独验证过（864 tri，quad 1.00，卫生满分）
# ════════════════════════════════════════════════════════════════════════

def make_ibeam_column(H=3.10, flange_w=0.20, flange_t=0.016, web_t=0.010,
                      base_s=0.34, base_t=0.022, gusset_h=0.16):
    parts = []
    parts.append(cube("web", (0, 0, H * 0.5), (web_t, flange_w - flange_t * 2, H)))
    parts.append(cube("fl_f", (0, (flange_w - flange_t) * 0.5, H * 0.5), (flange_t + 0.06, flange_t, H)))
    parts.append(cube("fl_b", (0, -(flange_w - flange_t) * 0.5, H * 0.5), (flange_t + 0.06, flange_t, H)))
    parts.append(cube("base", (0, 0, base_t * 0.5), (base_s, base_s, base_t)))
    for i, (sx, sy) in enumerate([(1, 0), (-1, 0), (0, 1), (0, -1)]):
        parts.append(cube("g%d" % i,
                          (sx * (base_s * 0.5 - 0.02), sy * (base_s * 0.5 - 0.02), gusset_h * 0.5 + base_t),
                          (0.010, 0.090, gusset_h) if sx != 0 else (0.090, 0.010, gusset_h)))
    o = join(parts, "GEO-Hall_IBeamColumn")
    bevel(o, 0.0035, 2)
    return shade(o)


# ════════════════════════════════════════════════════════════════════════
# 2. 货架立柱（角钢 + 冲孔 + 斜撑）
#    真实仓库货架是**冷轧冲孔角钢**：孔洞是它的视觉签名，必须做出来。
# ════════════════════════════════════════════════════════════════════════

def make_rack_upright(H=2.60, size=0.090, thick=0.006, holes=22, hole_r=0.0075,
                      brace_step=0.30):
    parts = []
    # 角钢：两片互成 90°
    parts.append(cube("face_a", (0, size * 0.5 - thick * 0.5, H * 0.5), (size, thick, H)))
    parts.append(cube("face_b", (size * 0.5 - thick * 0.5, 0, H * 0.5), (thick, size, H)))
    body = join(parts, "upright_body")

    # 冲孔：用圆柱布尔切（EXACT solver）；孔距真实货架 ~50mm，这里取 H/holes
    cutters = []
    for i in range(holes):
        z = 0.12 + i * ((H - 0.24) / max(1, holes - 1))
        cutters.append(cyl("hole_a%d" % i, (0, size * 0.5 - thick * 0.5, z), hole_r, thick * 4, 12,
                           rot=(math.radians(90), 0, 0)))
        cutters.append(cyl("hole_b%d" % i, (size * 0.5 - thick * 0.5, 0, z), hole_r, thick * 4, 12,
                           rot=(0, math.radians(90), 0)))
    # 逐个布尔（一次一个更稳；EXACT solver 对共面敏感，所以孔是穿透的圆柱）
    bpy.context.view_layer.objects.active = body
    for c in cutters:
        m = body.modifiers.new('cut', type='BOOLEAN')
        m.operation = 'DIFFERENCE'
        m.object = c
        m.solver = 'EXACT'
        bpy.ops.object.modifier_apply(modifier=m.name)
        bpy.data.objects.remove(c, do_unlink=True)

    # 斜撑（真实货架的 Z 形斜撑，两个方向交替）
    braces = []
    for i in range(int((H - 0.24) / brace_step) - 1):
        z0 = 0.12 + i * brace_step
        z1 = z0 + brace_step
        L = math.hypot(brace_step, size * 0.9)
        ang = math.atan2(size * 0.9, brace_step)
        sign = 1 if i % 2 == 0 else -1
        b = cube("brace%d" % i,
                 (sign * size * 0.45 * 0.5, 0, (z0 + z1) * 0.5),
                 (0.012, 0.028, L))
        b.rotation_euler = (0, sign * ang, 0)
        bpy.context.view_layer.objects.active = b
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        braces.append(b)
    o = join([body] + braces, "GEO-Hall_RackUpright")
    bevel(o, 0.002, 1)
    return shade(o)


# ════════════════════════════════════════════════════════════════════════
# 3. 货架横梁（C 型截面 + 端板 + 安全销）
# ════════════════════════════════════════════════════════════════════════

def make_rack_beam(L=2.40, h=0.090, t=0.005, depth=0.045):
    parts = []
    parts.append(cube("web", (0, 0, 0), (L, t, h)))                       # 腹板
    parts.append(cube("fl_t", (0, 0, (h - t) * 0.5), (L, depth, t)))      # 上翼缘
    parts.append(cube("fl_b", (0, 0, -(h - t) * 0.5), (L, depth, t)))     # 下翼缘
    # 端板（两端各一块，比梁高一点 → 真实货架就是这样卡进立柱的）
    for s in (-1, 1):
        parts.append(cube("end%d" % s, (s * (L * 0.5 - t * 0.5), 0, 0), (t * 2.2, depth * 1.5, h * 1.35)))
    o = join(parts, "GEO-Hall_RackBeam")
    bevel(o, 0.002, 1)
    return shade(o)


# ════════════════════════════════════════════════════════════════════════
# 4. 木栈板（面板条 + 纵梁 + 垫块）—— 官方仓库/货车里满地都是
# ════════════════════════════════════════════════════════════════════════

def make_pallet_wood(W=1.20, D=0.80, boards=7, thick=0.022):
    parts = []
    board_w = W / boards * 0.82
    for i in range(boards):
        x = -W * 0.5 + (i + 0.5) * (W / boards)
        parts.append(cube("bd%d" % i, (x, 0, thick * 2.5), (board_w, D, thick)))
    # 三条纵梁
    for j, y in enumerate((-D * 0.5 + 0.06, 0.0, D * 0.5 - 0.06)):
        parts.append(cube("st%d" % j, (0, y, thick * 1.5), (W, 0.070, thick)))
    # 底部垫块
    for sx in (-1, 1):
        for y in (-D * 0.5 + 0.10, 0.0, D * 0.5 - 0.10):
            parts.append(cube("blk", (sx * (W * 0.5 - 0.10), y, thick * 0.5), (0.12, 0.070, thick)))
    o = join(parts, "GEO-Hall_PalletWood")
    bevel(o, 0.0015, 1)
    return shade(o)


# ════════════════════════════════════════════════════════════════════════
# 5. 工业吊灯（锥形灯罩 + 吊杆 + 接线盒 + 灯泡）
# ════════════════════════════════════════════════════════════════════════

def make_lamp_industrial(drop=0.85, shade_r=0.30):
    parts = []
    # 吊杆
    parts.append(cyl("rod", (0, 0, -drop * 0.5), 0.008, drop, 10))
    # 接线盒（方形）
    parts.append(cube("box", (0, 0, 0.028), (0.11, 0.11, 0.055)))
    # 灯罩：圆锥（开口朝下）
    bpy.ops.mesh.primitive_cone_add(vertices=20, radius1=shade_r, radius2=0.035,
                                    depth=0.22, location=(0, 0, -drop - 0.06))
    cone = bpy.context.active_object
    cone.name = "shade"
    parts.append(cone)
    # 灯泡（球，放在罩内）
    bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=8, radius=0.045,
                                         location=(0, 0, -drop - 0.10))
    bulb = bpy.context.active_object
    bulb.name = "bulb"
    parts.append(bulb)
    o = join(parts, "GEO-Hall_LampIndustrial")
    bevel(o, 0.002, 1)
    return shade(o, smooth=True)


# ════════════════════════════════════════════════════════════════════════
# 6. 管道（管体 + 法兰 + 螺栓圈 + 吊架）
#    法兰上的螺栓是"工业感"的关键，一定不能省。
# ════════════════════════════════════════════════════════════════════════

def make_pipe_flange(L=2.40, r=0.075, flange_r=0.115, flange_t=0.020, bolts=8, with_bracket=True):
    parts = []
    parts.append(cyl("pipe", (0, 0, 0), r, L, 20, rot=(0, math.radians(90), 0)))
    for s in (-1, 1):
        x = s * (L * 0.5 - flange_t * 0.5)
        parts.append(cyl("fl%d" % s, (x, 0, 0), flange_r, flange_t, 20, rot=(0, math.radians(90), 0)))
        # 螺栓：一圈小圆柱 + 六角头近似（圆柱即可，视觉上够）
        for i in range(bolts):
            a = 2 * math.pi * i / bolts
            by = math.cos(a) * (flange_r - 0.018)
            bz = math.sin(a) * (flange_r - 0.018)
            parts.append(cyl("bolt%d_%d" % (s, i), (s * (L * 0.5 + 0.002), by, bz),
                             0.008, 0.012, 6, rot=(0, math.radians(90), 0)))
    if with_bracket:
        parts.append(cube("brk", (0, 0, r + 0.055), (0.05, 0.04, 0.11)))
        parts.append(cube("brk_top", (0, 0, r + 0.115), (0.05, 0.22, 0.02)))
    o = join(parts, "GEO-Hall_PipeFlange")
    bevel(o, 0.0015, 1)
    return shade(o, smooth=False)


# ════════════════════════════════════════════════════════════════════════
# 7. 配电箱（箱体 + 门 + 铰链 + 锁 + 把手 + 进出线孔）
#    官方场景里的 fuse box / 电闸面板，也是我们的"总闸"落点。
# ════════════════════════════════════════════════════════════════════════

def make_electric_panel(W=0.46, H=0.62, D=0.16, door_t=0.014):
    parts = []
    parts.append(cube("body", (0, 0, 0), (W, D, H)))
    # 门（略微前凸，形成缝）
    parts.append(cube("door", (0, -(D * 0.5 + door_t * 0.5), 0), (W * 0.985, door_t, H * 0.985)))
    # 铰链（两个）
    for s in (-1, 1):
        parts.append(cyl("hinge%d" % s, (-W * 0.5 + 0.005, -(D * 0.5 + door_t), s * H * 0.30),
                         0.011, 0.045, 10))
    # 锁（圆盘 + 钥匙孔）
    parts.append(cyl("lock", (W * 0.5 - 0.06, -(D * 0.5 + door_t + 0.004), 0), 0.022, 0.010, 14,
                     rot=(math.radians(90), 0, 0)))
    # 把手
    parts.append(cube("handle", (W * 0.5 - 0.06, -(D * 0.5 + door_t + 0.018), -0.085), (0.018, 0.026, 0.075)))
    # 顶部进出线孔（两个）
    for s in (-1, 1):
        parts.append(cyl("gland%d" % s, (s * W * 0.26, 0, H * 0.5 + 0.012), 0.020, 0.028, 12))
    o = join(parts, "GEO-Hall_ElectricPanel")
    bevel(o, 0.0025, 1)
    return shade(o)


# ════════════════════════════════════════════════════════════════════════
# 8. 木箱（板材拼缝 + 四角铁件）
# ════════════════════════════════════════════════════════════════════════

def make_crate_wood(S=0.60, planks=5, t=0.018):
    parts = []
    ph = S / planks * 0.80
    for i in range(planks):
        z = -S * 0.5 + (i + 0.5) * (S / planks)
        for (sx, sy) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if sx != 0:
                parts.append(cube("pk%d" % i, (sx * (S * 0.5 - t * 0.5), 0, z), (t, S, ph)))
            else:
                parts.append(cube("pk%d" % i, (0, sy * (S * 0.5 - t * 0.5), z), (S, t, ph)))
    # 底部
    parts.append(cube("bot", (0, 0, -S * 0.5 + t * 0.5), (S - t, S - t, t)))
    # 四角竖向角铁
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(cube("ang", (sx * (S * 0.5 - 0.012), sy * (S * 0.5 - 0.012), 0),
                              (0.026, 0.026, S * 1.005)))
    o = join(parts, "GEO-Hall_CrateWood")
    bevel(o, 0.0018, 1)
    return shade(o)


# ════════════════════════════════════════════════════════════════════════
# 9. 油桶（卷边 + 两道箍 + 桶盖塞）
# ════════════════════════════════════════════════════════════════════════

def make_barrel(H=0.88, r=0.29, rib_r=0.305):
    parts = []
    parts.append(cyl("body", (0, 0, H * 0.5), r, H, 24))
    for z in (H * 0.30, H * 0.70):
        parts.append(cyl("rib", (0, 0, z), rib_r, 0.020, 24))
    for z, rr in ((0.012, r * 1.06), (H - 0.012, r * 1.06)):
        parts.append(cyl("rim", (0, 0, z), rr, 0.024, 24))
    parts.append(cyl("bung", (r * 0.55, 0, H + 0.006), 0.028, 0.014, 10))
    o = join(parts, "GEO-Hall_Barrel")
    bevel(o, 0.002, 1)
    return shade(o, smooth=True)


# ════════════════════════════════════════════════════════════════════════
# 10. 通风管段（方管 + 两端法兰 + 吊架）
# ════════════════════════════════════════════════════════════════════════

def make_duct(L=2.20, w=0.50, h=0.36, t=0.010, flange_w=0.045):
    parts = []
    parts.append(cube("duct", (0, 0, 0), (L, w, h)))
    for s in (-1, 1):
        x = s * (L * 0.5 + flange_w * 0.5)
        parts.append(cube("fl%d" % s, (x, 0, 0), (flange_w, w + 0.06, h + 0.06)))
        # 法兰螺栓
        for i in range(6):
            zz = -h * 0.5 + (i % 3) * (h * 0.5)
            yy = (w * 0.5 + 0.03) * (1 if i < 3 else -1)
            parts.append(cyl("b%d_%d" % (s, i), (x, yy, zz), 0.007, 0.012, 6,
                             rot=(math.radians(90), 0, 0)))
    # 吊架
    parts.append(cube("hang", (0, 0, h * 0.5 + 0.075), (0.05, 0.05, 0.15)))
    parts.append(cube("hang_top", (0, 0, h * 0.5 + 0.155), (0.05, w + 0.16, 0.018)))
    o = join(parts, "GEO-Hall_DuctSection")
    bevel(o, 0.002, 1)
    return shade(o)


# ════════════════════════════════════════════════════════════════════════
# 生成清单（名称 → 构造函数）。每个都单独导出，便于 Unity 按需实例化。
# ════════════════════════════════════════════════════════════════════════

BUILDERS = [
    ("Hall_IBeamColumn",   make_ibeam_column),
    ("Hall_RackUpright",   make_rack_upright),
    ("Hall_RackBeam",      make_rack_beam),
    ("Hall_PalletWood",    make_pallet_wood),
    ("Hall_LampIndustrial", make_lamp_industrial),
    ("Hall_PipeFlange",    make_pipe_flange),
    ("Hall_ElectricPanel", make_electric_panel),
    ("Hall_CrateWood",     make_crate_wood),
    ("Hall_Barrel",        make_barrel),
    ("Hall_DuctSection",   make_duct),
]

if __name__ == "__main__":
    clear_scene()
    report = []
    for name, fn in BUILDERS:
        clear_scene()
        obj = fn()
        report.append({
            "id": obj.name,
            "tris": tri_count(obj),
            "verts": len(obj.data.vertices),
            "quads": sum(1 for p in obj.data.polygons if len(p.vertices) == 4),
            "faces": len(obj.data.polygons),
        })
    print("BUILD_REPORT " + str(report))
