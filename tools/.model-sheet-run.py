
import bpy, math, os, sys, json
A = json.loads(sys.argv[sys.argv.index('--') + 1])
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

def build_one(P, tag, dx, height, q):
    # 注意：生成脚本用 ARGS['body'] 取**体型参数对象**（不是体型名的字符串），
    # 我第一次传成字符串 → KeyError: 'body'。这里按生成脚本的真实契约传。
    globals()['ARGS'] = {'canon': A['canon'], 'body': P, 'bodyName': tag, 'out': A['out'],
                         'tag': 'X', 'height': height, 'quality': q}
    # 重新执行生成脚本（它会往当前场景里加物体）
    exec(compile(A['gen'], '<gen>', 'exec'), globals())
    # 把这次新建的网格整体沿 X 平移
    made = [o for o in bpy.data.objects if o.name.startswith(('Torso','Head','Neck','Arm','Leg','Eye'))]
    for o in made:
        o.location.x += dx
    return made

allmesh = []
for i, (tag, P) in enumerate(A['bodies'].items()):
    allmesh += build_one(P, tag, i * 1.25, A['height'], A['quality'])

# 材质：统一近黑（对比图看**比例**，不看材质）
m = bpy.data.materials.new("MAT-Sheet"); m.use_nodes = True
b = m.node_tree.nodes.get("Principled BSDF")
b.inputs['Base Color'].default_value = (0.16, 0.17, 0.20, 1.0)
b.inputs['Roughness'].default_value = 0.72
for o in allmesh:
    if o.type == 'MESH':
        o.data.materials.clear(); o.data.materials.append(m)
        for md in list(o.modifiers):   # 烘修改器，让渲染与导出所见一致
            dg = bpy.context.evaluated_depsgraph_get()
            me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
            old = o.data; o.data = me; bpy.data.meshes.remove(old)
            o.modifiers.remove(md)
            o.data.materials.clear(); o.data.materials.append(m)

n = len(A['bodies'])
cam_d = bpy.data.cameras.new("CAM"); cam_d.type = 'ORTHO'
cam_d.ortho_scale = max(2.4, 1.25 * n + 0.5)
cam = bpy.data.objects.new("CAM", cam_d); bpy.context.scene.collection.objects.link(cam)
cam.location = ((n - 1) * 0.625, -8.0, 1.00)
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam
for nm, e, loc, rot in (("K", 900, (-2.4, -4.2, 3.6), (math.radians(46), 0, math.radians(-28))),
                        ("F", 300, ( 2.8, -3.8, 1.6), (math.radians(78), 0, math.radians(38))),
                        ("R", 520, ( 0.4,  3.8, 3.1), (math.radians(122), 0, math.radians(178)))):
    d = bpy.data.lights.new(nm, type='AREA'); d.energy = e; d.size = 3.2
    o = bpy.data.objects.new(nm, d); bpy.context.scene.collection.objects.link(o)
    o.location = loc; o.rotation_euler = rot
w = bpy.data.worlds.new("W"); bpy.context.scene.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.020, 0.021, 0.025, 1.0); bg.inputs[1].default_value = 0.45
sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y = A['w'], A['h']
sc.render.engine = 'BLENDER_EEVEE'
sc.render.filepath = A['out']
bpy.ops.render.render(write_still=True)

# 客观校验：每个体型的**世界包围盒**（判断是否真的不同、是否站姿）
for i, tag in enumerate(A['bodies'].keys()):
    xs = [o for o in allmesh if o.type == 'MESH' and abs(o.location.x - i * 1.25) < 0.01]
    if not xs: continue
    mn = [1e9]*3; mx = [-1e9]*3
    for o in xs:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            mn = [min(mn[k], p[k]) for k in range(3)]
            mx = [max(mx[k], p[k]) for k in range(3)]
    print('SHEET_ONE %s X=%.3f Y=%.3f Z=%.3f' % (tag, mx[0]-mn[0], mx[1]-mn[1], mx[2]-mn[2]))
print("WHISPER_SHEET_RESULT " + json.dumps({"out": A['out'], "count": n}))
