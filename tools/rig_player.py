# rig_player.py — 给玩家模型绑**人形骨架**并烘一段**行走动画**，导出带动画/蒙皮的 GLB
#
# ══════════════════════════════════════════════════════════════════════════════
# 为什么做这件事（目标③「真实动作引擎」的硬缺口）
# ══════════════════════════════════════════════════════════════════════════════
# 实测（tools/inspect_blend.py 对 player-source-v5.blend）：
#   · 8 个 GEO-Player* 部件，**全部无顶点组、无骨骼修改器、无形态键**
#   · `bpy.data.armatures` 与 `bpy.data.actions` **均为空**
#   · 导出的 player.glb：**无 skin、无 animation**
# ⇒ 角色是**静态网格**。没有骨骼就没有"动作引擎"可谈。
#
# ══════════════════════════════════════════════════════════════════════════════
# 关节位置从哪来（**实测，不猜**）
# ══════════════════════════════════════════════════════════════════════════════
# 由 tmp/inspect-world.py 量出的世界坐标（Blender Z-up，身高 1.814m）：
#   头 [1.599,1.831] · 颈 [1.600,1.760] · 躯干 [0.820,1.680] X±0.215
#   臂 [0.930,1.554] X±(0.183~0.297) · 腿 [0.020,0.880] · 脚 [0.018,0.052] Y-0.140~0.035
# ⇒ 髋在 ~0.82、膝在 ~0.45、踝在 ~0.06、肩在 ~1.50、肘在 ~1.24、腕在 ~0.95
#
# ══════════════════════════════════════════════════════════════════════════════
# 蒙皮策略（**如实说明取舍**）
# ══════════════════════════════════════════════════════════════════════════════
# 这些部件是**分离的刚性件**（各肢体一个网格），不是连续蒙皮网格。
# 所以采用**刚性绑定 + 关节处加权过渡**：
#   · 部件主体 → 绑到该肢体的骨（权重 1）
#   · 邻近关节的一小段 → 按沿肢体的参数 t 在两根骨之间线性过渡（避免关节撕裂）
# 这是"分部件角色"的标准做法（很多移动端角色就是这么做的），
# 但它**不是**次表面级别的连续蒙皮 —— 真实人形需要重拓扑成整体网格再刷权重，
# 那属于后续里程碑（已登记）。本脚本的目标是让角色**先能动**，且动得**不撕裂**。
#
# 用法：
#   blender -b --factory-startup --python rig_player.py -- <player_geo.glb> <out.glb> [walk|idle]
import bpy, os, sys, math, json
from mathutils import Vector, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = argv[0]
OUT = argv[1]
CLIP = argv[2] if len(argv) > 2 else "walk"

# ── 清空并导入几何 ────────────────────────────────────────────────────────────
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not parts:
    raise SystemExit("导入后没有网格")

# glTF 导入会把 Y-up 转回 Z-up ⇒ 坐标系与导出前的 .blend 一致（身高沿 +Z）
allz = [(o.matrix_world @ v.co).z for o in parts for v in o.data.vertices]
GROUND = min(allz)
TOP = max(allz)
H = TOP - GROUND
print(f"[rig] 身高 {H:.3f}m · 地面 z={GROUND:.4f} · 部件 {len(parts)}")

# ── 关节高度（按实测比例定；用相对身高表达，换模型也能用）────────────────────
def z(frac):
    return GROUND + H * frac

J = {
    "hip":   z(0.44),   # 0.80（实测躯干底 0.82）
    "spine": z(0.55),
    "chest": z(0.68),
    "neck":  z(0.87),
    "head":  z(0.95),
    "shoulder": z(0.82),
    "elbow": z(0.68),
    "wrist": z(0.52),
    "knee":  z(0.25),
    "ankle": z(0.03),
}
# 左右：从部件 X 范围取（不写死 ±0.2）
def part_by(pred):
    for o in parts:
        if pred(o.name):
            return o
    return None

armL = part_by(lambda n: "ArmL" in n); armR = part_by(lambda n: "ArmR" in n)
legL = part_by(lambda n: "LegL" in n); legR = part_by(lambda n: "LegR" in n)
footL = part_by(lambda n: "FootL" in n); footR = part_by(lambda n: "FootR" in n)

def cx(o):
    if o is None: return 0.0
    xs = [(o.matrix_world @ v.co).x for v in o.data.vertices]
    return (min(xs) + max(xs)) * 0.5

SIDE = {
    "L": {"arm": cx(armL), "leg": cx(legL), "foot": cx(footL)},
    "R": {"arm": cx(armR), "leg": cx(legR), "foot": cx(footR)},
}
print("[rig] 左右中心 X:", json.dumps({k: {kk: round(vv, 3) for kk, vv in v.items()} for k, v in SIDE.items()}))

# ── 建骨架（正视图：角色面朝 -Y，与 Blender 人体惯例一致）────────────────────
arm_data = bpy.data.armatures.new("WhisperRig")
rig = bpy.data.objects.new("WhisperRig", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')

eb = arm_data.edit_bones
def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head = Vector(head); b.tail = Vector(tail)
    if parent: b.parent = parent; b.use_connect = connect
    return b

root  = bone("Root",  (0, 0, GROUND), (0, 0, GROUND + H * 0.06))
hips  = bone("Hips",  (0, 0, J["hip"]), (0, 0, J["spine"]), root)
spine = bone("Spine", (0, 0, J["spine"]), (0, 0, J["chest"]), hips, True)
chest = bone("Chest", (0, 0, J["chest"]), (0, 0, J["neck"]), spine, True)
neck  = bone("Neck",  (0, 0, J["neck"]), (0, 0, J["head"]), chest, True)
head  = bone("Head",  (0, 0, J["head"]), (0, 0, GROUND + H * 1.0), neck, True)

for s in ("L", "R"):
    xa = SIDE[s]["arm"]
    xl = SIDE[s]["leg"]
    xf = SIDE[s]["foot"]
    sh = bone(f"Shoulder{s}", (xa * 0.35, 0, J["shoulder"]), (xa, 0, J["shoulder"]), chest)
    ua = bone(f"UpperArm{s}", (xa, 0, J["shoulder"]), (xa, 0, J["elbow"]), sh, True)
    la = bone(f"LowerArm{s}", (xa, 0, J["elbow"]), (xa, 0, J["wrist"]), ua, True)
    bone(f"Hand{s}", (xa, 0, J["wrist"]), (xa, 0, J["wrist"] - H * 0.09), la, True)
    th = bone(f"Thigh{s}", (xl, 0, J["hip"]), (xl, 0, J["knee"]), hips)
    sn = bone(f"Shin{s}", (xl, 0, J["knee"]), (xl, 0, J["ankle"]), th, True)
    bone(f"Foot{s}", (xf, 0.02, J["ankle"]), (xf, -0.11, J["ankle"] * 0.35), sn, True)

bpy.ops.object.mode_set(mode='OBJECT')
print(f"[rig] 骨骼数 {len(arm_data.bones)}: {[b.name for b in arm_data.bones]}")

# ── 蒙皮：按部件名 → 骨骼，关节处按沿轴参数加权过渡 ──────────────────────────
# 权重口径：对每个顶点算它在本部件主轴（Z）上的归一化位置 t∈[0,1]，
#   t<BLEND  → 偏向"靠上/靠躯干"那根骨
#   t>1-BLEND→ 偏向"靠下/靠末端"那根骨
#   中间     → 两骨之间线性插值
BLEND = 0.22

def zrange(o):
    zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
    return min(zs), max(zs)

def assign(o, bone_upper, bone_lower, upper_at_top=True):
    """把 o 的顶点按 Z 位置在 bone_upper / bone_lower 之间加权。"""
    lo, hi = zrange(o)
    span = max(hi - lo, 1e-6)
    gu = o.vertex_groups.get(bone_upper) or o.vertex_groups.new(name=bone_upper)
    gl = o.vertex_groups.get(bone_lower) or o.vertex_groups.new(name=bone_lower)
    for v in o.data.vertices:
        zv = (o.matrix_world @ v.co).z
        t = (hi - zv) / span if upper_at_top else (zv - lo) / span   # 0=靠 upper 端
        if t <= BLEND:
            wl = 0.0
        elif t >= 1.0 - BLEND:
            wl = 1.0
        else:
            wl = (t - BLEND) / max(1.0 - 2 * BLEND, 1e-6)
        gu.add([v.index], 1.0 - wl, 'REPLACE')
        gl.add([v.index], wl, 'REPLACE')

def assign_single(o, bone_name):
    g = o.vertex_groups.get(bone_name) or o.vertex_groups.new(name=bone_name)
    g.add([v.index for v in o.data.vertices], 1.0, 'REPLACE')

for o in parts:
    n = o.name
    if "ArmL" in n:   assign(o, "UpperArmL", "LowerArmL", upper_at_top=True)
    elif "ArmR" in n: assign(o, "UpperArmR", "LowerArmR", upper_at_top=True)
    elif "LegL" in n: assign(o, "ThighL", "ShinL", upper_at_top=True)
    elif "LegR" in n: assign(o, "ThighR", "ShinR", upper_at_top=True)
    elif "FootL" in n or "FootR" in n: assign_single(o, "FootL" if "FootL" in n else "FootR")
    elif "Neck" in n: assign_single(o, "Neck")
    elif "Head" in n: assign_single(o, "Head")
    elif "Torso" in n:
        # 躯干跨 髋/脊/胸：按 Z 分三段刚性 + 交界过渡
        lo, hi = zrange(o); span = max(hi - lo, 1e-6)
        gh = o.vertex_groups.get("Hips") or o.vertex_groups.new(name="Hips")
        gs = o.vertex_groups.get("Spine") or o.vertex_groups.new(name="Spine")
        gc = o.vertex_groups.get("Chest") or o.vertex_groups.new(name="Chest")
        for v in o.data.vertices:
            t = ((o.matrix_world @ v.co).z - lo) / span     # 0=底 1=顶
            wh = max(0.0, 1.0 - t / 0.45)
            wc = max(0.0, (t - 0.55) / 0.45)
            ws = max(0.0, 1.0 - wh - wc)
            s = wh + ws + wc
            gh.add([v.index], wh / s, 'REPLACE')
            gs.add([v.index], ws / s, 'REPLACE')
            gc.add([v.index], wc / s, 'REPLACE')
    else:
        assign_single(o, "Hips")
    # 挂骨架修改器
    mod = o.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig
    o.parent = rig

print("[rig] 蒙皮完成（各部件顶点组已建）")

# ── 行走动画（8 帧循环 @ 24fps = 0.33s 一个循环 → 约 3 步/秒）──────────────────
# 这是**最简可信的行走**：髋部上下 + 大腿前后摆 + 手臂反向摆 + 躯干微前倾。
# 口径：位移量按身高比例给（换模型不用改数），相位用整周期正弦 —— 循环无缝。
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE')
action = bpy.data.actions.new("Walk")
rig.animation_data_create()
rig.animation_data.action = action

FPS = 24
CYCLE = 16          # 16 帧一个完整步态周期（约 0.67s ⇒ 1.5 步/秒，接近成人慢走）
pb = rig.pose.bones

def key_rot(bone_name, frame, axis, deg):
    b = pb[bone_name]
    b.rotation_mode = 'XYZ'
    e = list(b.rotation_euler)
    e[axis] = math.radians(deg)
    b.rotation_euler = e

def key_loc(bone_name, frame, dz=0.0, dy=0.0):
    b = pb[bone_name]
    b.location = Vector((0.0, dy, dz))

SWING = 26.0        # 大腿最大前后摆角（度）—— 成人慢走约 20~30°
ARM = 18.0          # 手臂反摆
BOB = H * 0.022     # 髋部上下起伏（约 4cm）—— 真实走路的重心起伏

# ⚠ 【2026-10-06 实测踩坑】**每根被动画的骨骼，必须在每一个关键帧都打键。**
# 我第一版只在"该骨骼有变化"的帧打键（例如 Hips 只在起伏帧打 location、
# Spine/Chest/Neck 只在部分帧打 rotation），导出后：
#     Hips 的 location 轨道 17 个采样 ✓ 而 rotation 轨道只有 **2 个** ✗
# 后果：整段动画塌成"首尾两帧插值"，看起来像静止 —— 而**导出不报任何错**
# （glTF 只是如实地把"你给的关键帧"写出去）。
# ⇒ 正确做法：把每个关键帧上**所有**参与骨骼都写一遍（哪怕数值没变），
#   轨道才会成为完整的采样序列。这也是"导出成功 ≠ 动画正确"的又一例。
ANIM_BONES = ["ThighL", "ThighR", "ShinL", "ShinR", "UpperArmL", "UpperArmR",
              "Hips", "Spine", "Chest"]

for i in range(0, 5):          # 用 5 个关键帧描述一个周期（0/4/8/12/16）
    f = 1 + i * (CYCLE // 4)
    ph = (i % 4) / 4.0 * 2 * math.pi
    s = math.sin(ph)
    # 大腿：左右反相
    key_rot("ThighL", f, 0, SWING * s)
    key_rot("ThighR", f, 0, -SWING * s)
    # 小腿：只在向后摆时弯曲（膝盖不会向前反折）
    key_rot("ShinL", f, 0, max(0.0, -SWING * s) * 0.9)
    key_rot("ShinR", f, 0, max(0.0, SWING * s) * 0.9)
    # 手臂反向摆
    key_rot("UpperArmL", f, 0, -ARM * s)
    key_rot("UpperArmR", f, 0, ARM * s)
    # 髋部起伏：一个周期两次（每步一次）
    key_loc("Hips", f, dz=BOB * abs(math.cos(ph)) - BOB * 0.5)
    # 躯干微前倾 + 随步态轻侧转（让人看着"在走"而不是"在飘"）
    key_rot("Spine", f, 0, 4.0)
    key_rot("Chest", f, 2, 3.0 * s)

    # ── 每一帧把全部参与骨骼打键（含未显式变化的那几条轴）──────────────────
    for bn in ANIM_BONES:
        pb[bn].keyframe_insert("rotation_euler", frame=f)
    pb["Hips"].keyframe_insert("location", frame=f)

bpy.ops.object.mode_set(mode='OBJECT')
sc = bpy.context.scene
sc.frame_start = 1
sc.frame_end = 1 + CYCLE
sc.render.fps = FPS

# ── 导出：**必须带蒙皮与动画** ────────────────────────────────────────────────
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.gltf(
    filepath=OUT, export_format='GLB', use_selection=True,
    export_yup=True,
    export_skins=True,
    export_animations=True,
    export_animation_mode='ACTIONS',
    export_frame_range=True,
    export_apply=False,
)
print("[rig] 已导出", OUT, os.path.getsize(OUT), "字节")
print("RIG_REPORT=" + json.dumps({
    "height_m": round(H, 3), "bones": len(arm_data.bones),
    "parts": len(parts), "clip": CLIP, "frames": CYCLE, "fps": FPS,
}, ensure_ascii=False))
