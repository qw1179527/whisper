# rig_ghost.py — 给鬼模型绑骨架并烘一段**漂浮待机**动画，导出带动画/蒙皮的 GLB
#
# ══════════════════════════════════════════════════════════════════════════════
# 为什么做（承接 `tools/rig_player.py` 的同一缺口）
# ══════════════════════════════════════════════════════════════════════════════
# 实测（`tools/inspect_blend.py` 对 ghost-source-v6.blend）：
#   19 个 `GEO-Ghost*` 部件，**全部无顶点组、无骨骼修改器**；
#   `bpy.data.armatures` 与 `bpy.data.actions` **均为空**；
#   导出的 ghost.glb **无 skin、无 animation** ⇒ 鬼是**静态网格**。
#
# ══════════════════════════════════════════════════════════════════════════════
# 鬼不是人形 —— 结构由实测决定，不套用玩家骨架
# ══════════════════════════════════════════════════════════════════════════════
# 实测世界坐标（Blender Z-up，整体高 **2.485m**，底面 -0.026）：
#   Head  [1.596, 1.844]  X±0.117          ← 头在 **1.6~1.84**
#   ArmL/R[1.599, 2.459]  X±(0.199~0.295)  ← 手臂**从肩一路举到 2.46，高过头顶**
#   Torso 308 顶点                          ← 无腿：躯干向下收成飘浮体
# ⇒ 与玩家（1.814m、臂 0.93~1.55）**完全不同**：鬼是"漂浮体 + 高举双臂"，
#   **没有腿**。所以骨架只做：根/髋/脊/胸/颈/头 + 双臂（上臂/前臂/手），
#   并给无腿的下摆单列一根 `Hem`（腰以下），用于"飘动"。
#
# 动画：**漂浮待机**（不是走路）—— 整体缓慢上下浮动 + 轻微前后倾 + 双臂微摆。
# 口径与行走同样用整周期正弦 ⇒ 循环无缝。
#
# 用法：
#   blender -b --factory-startup --python rig_ghost.py -- <ghost_geo.glb> <out.glb>
import bpy, os, sys, math, json
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC, OUT = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not parts:
    raise SystemExit("导入后没有网格")

allz = [(o.matrix_world @ v.co).z for o in parts for v in o.data.vertices]
GROUND, TOP = min(allz), max(allz)
H = TOP - GROUND
print(f"[ghost-rig] 高 {H:.3f}m · 地面 z={GROUND:.4f} · 部件 {len(parts)}")

def z(frac):
    return GROUND + H * frac

# 关节高度**按实测比例**（头 1.596/2.485=0.64H、臂顶 2.459/2.485=0.99H）
J = {
    "hip": z(0.30),      # 飘浮体的下摆起点
    "spine": z(0.45),
    "chest": z(0.58),
    "neck": z(0.62),
    "head": z(0.64),
    "shoulder": z(0.66),
    "elbow": z(0.80),
    "wrist": z(0.93),
}

def part(pred):
    for o in parts:
        if pred(o.name): return o
    return None

armL, armR = part(lambda n: "ArmL" in n), part(lambda n: "ArmR" in n)
def cx(o):
    if o is None: return 0.0
    xs = [(o.matrix_world @ v.co).x for v in o.data.vertices]
    return (min(xs) + max(xs)) * 0.5
SIDE = {"L": cx(armL), "R": cx(armR)}
print("[ghost-rig] 臂中心 X:", json.dumps({k: round(v, 3) for k, v in SIDE.items()}))

# ── 骨架 ────────────────────────────────────────────────────────────────────
arm_data = bpy.data.armatures.new("GhostRig")
rig = bpy.data.objects.new("GhostRig", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name); b.head = Vector(head); b.tail = Vector(tail)
    if parent: b.parent = parent; b.use_connect = connect
    return b

root  = bone("Root",  (0, 0, GROUND), (0, 0, GROUND + H * 0.05))
hem   = bone("Hem",   (0, 0, GROUND), (0, 0, J["hip"]), root)
hips  = bone("Hips",  (0, 0, J["hip"]), (0, 0, J["spine"]), hem, True)
spine = bone("Spine", (0, 0, J["spine"]), (0, 0, J["chest"]), hips, True)
chest = bone("Chest", (0, 0, J["chest"]), (0, 0, J["neck"]), spine, True)
neck  = bone("Neck",  (0, 0, J["neck"]), (0, 0, J["head"]), chest, True)
head  = bone("Head",  (0, 0, J["head"]), (0, 0, GROUND + H), neck, True)
for s in ("L", "R"):
    xa = SIDE[s]
    sh = bone(f"Shoulder{s}", (xa * 0.4, 0, J["shoulder"]), (xa, 0, J["shoulder"]), chest)
    ua = bone(f"UpperArm{s}", (xa, 0, J["shoulder"]), (xa, 0, J["elbow"]), sh, True)
    la = bone(f"LowerArm{s}", (xa, 0, J["elbow"]), (xa, 0, J["wrist"]), ua, True)
    bone(f"Hand{s}", (xa, 0, J["wrist"]), (xa, 0, GROUND + H * 0.99), la, True)
bpy.ops.object.mode_set(mode='OBJECT')
print(f"[ghost-rig] 骨骼 {len(arm_data.bones)}: {[b.name for b in arm_data.bones]}")

# ── 蒙皮：按部件名 → 骨，沿 Z 加权过渡 ──────────────────────────────────────
BLEND = 0.25
def zrange(o):
    zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
    return min(zs), max(zs)
def assign(o, upper, lower):
    lo, hi = zrange(o); span = max(hi - lo, 1e-6)
    gu = o.vertex_groups.get(upper) or o.vertex_groups.new(name=upper)
    gl = o.vertex_groups.get(lower) or o.vertex_groups.new(name=lower)
    for v in o.data.vertices:
        t = (hi - (o.matrix_world @ v.co).z) / span     # 0 = 靠 upper 端
        wl = 0.0 if t <= BLEND else (1.0 if t >= 1.0 - BLEND else (t - BLEND) / max(1.0 - 2 * BLEND, 1e-6))
        gu.add([v.index], 1.0 - wl, 'REPLACE')
        gl.add([v.index], wl, 'REPLACE')
def single(o, name):
    g = o.vertex_groups.get(name) or o.vertex_groups.new(name=name)
    g.add([v.index for v in o.data.vertices], 1.0, 'REPLACE')

for o in parts:
    n = o.name
    if "ArmL" in n: assign(o, "UpperArmL", "LowerArmL")
    elif "ArmR" in n: assign(o, "UpperArmR", "LowerArmR")
    elif "Head" in n: single(o, "Head")
    elif "Eye" in n: single(o, "Head")          # 眼球/眼窝随头动 —— 否则会"眼睛留在原地"
    elif "Torso" in n:
        lo, hi = zrange(o); span = max(hi - lo, 1e-6)
        gh = o.vertex_groups.get("Hem") or o.vertex_groups.new(name="Hem")
        gs = o.vertex_groups.get("Spine") or o.vertex_groups.new(name="Spine")
        gc = o.vertex_groups.get("Chest") or o.vertex_groups.new(name="Chest")
        for v in o.data.vertices:
            t = ((o.matrix_world @ v.co).z - lo) / span
            wh = max(0.0, 1.0 - t / 0.40)
            wc = max(0.0, (t - 0.60) / 0.40)
            ws = max(0.0, 1.0 - wh - wc)
            s = wh + ws + wc
            gh.add([v.index], wh / s, 'REPLACE')
            gs.add([v.index], ws / s, 'REPLACE')
            gc.add([v.index], wc / s, 'REPLACE')
    else: single(o, "Spine")
    m = o.modifiers.new("Armature", 'ARMATURE'); m.object = rig
    o.parent = rig
print("[ghost-rig] 蒙皮完成")

# ── 漂浮待机动画（24 帧 @24fps = 1s 一周期）────────────────────────────────
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE')
act = bpy.data.actions.new("Float")
rig.animation_data_create(); rig.animation_data.action = act
FPS, CYCLE = 24, 24
pb = rig.pose.bones
# ⚠ 与 rig_player 同一坑：**每根被动画的骨，在每个关键帧都要打键**，
#   否则 glTF 只导出"首尾两帧"、动画看着像静止（实测过）。
ANIM = ["Hem", "Hips", "Spine", "Chest", "Neck", "Head", "UpperArmL", "UpperArmR", "LowerArmL", "LowerArmR"]
FLOAT_H = H * 0.035      # 整体浮动幅度（约 8.7cm）
for i in range(0, 5):
    f = 1 + i * (CYCLE // 4)
    ph = (i % 4) / 4.0 * 2 * math.pi
    s = math.sin(ph); c = math.cos(ph)
    # 整体上下浮动：一个周期一次（飘浮感）
    pb["Root"].location = Vector((0.0, 0.0, FLOAT_H * s))
    pb["Root"].keyframe_insert("location", frame=f)
    # 躯干微微前后倾 + 颈头轻微反相（像被无形之力托着）
    for bn, amp, axis in (("Spine", 2.5, 0), ("Chest", 1.8, 0), ("Neck", -1.2, 0), ("Head", -1.0, 0)):
        b = pb[bn]; b.rotation_mode = 'XYZ'
        e = list(b.rotation_euler); e[axis] = math.radians(amp * s); b.rotation_euler = e
    # 双臂缓慢摆动（高举的双臂像漂在水里）
    for bn, sgn in (("UpperArmL", 1.0), ("UpperArmR", -1.0)):
        b = pb[bn]; b.rotation_mode = 'XYZ'
        e = list(b.rotation_euler); e[0] = math.radians(4.0 * s * sgn); e[2] = math.radians(3.0 * c * sgn)
        b.rotation_euler = e
    for bn, sgn in (("LowerArmL", 1.0), ("LowerArmR", -1.0)):
        b = pb[bn]; b.rotation_mode = 'XYZ'
        e = list(b.rotation_euler); e[0] = math.radians(3.0 * c * sgn); b.rotation_euler = e
    # **本帧把所有参与骨骼都打键**（见上方警告）
    for bn in ANIM: pb[bn].keyframe_insert("rotation_euler", frame=f)
    pb["Root"].keyframe_insert("location", frame=f)
pb["Hem"].rotation_mode = 'XYZ'
pb["Hem"].rotation_euler = (0, 0, 0)
for i in range(0, 5):
    f = 1 + i * (CYCLE // 4)
    pb["Hem"].keyframe_insert("rotation_euler", frame=f)
bpy.ops.object.mode_set(mode='OBJECT')
sc = bpy.context.scene; sc.frame_start = 1; sc.frame_end = 1 + CYCLE; sc.render.fps = FPS

bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True,
                          export_yup=True, export_skins=True, export_animations=True,
                          export_animation_mode='ACTIONS', export_frame_range=True, export_apply=False)
print("[ghost-rig] 已导出", OUT, os.path.getsize(OUT), "字节")
print("GHOST_RIG_REPORT=" + json.dumps({"height_m": round(H, 3), "bones": len(arm_data.bones),
      "parts": len(parts), "clip": "Float", "frames": CYCLE, "fps": FPS}, ensure_ascii=False))
