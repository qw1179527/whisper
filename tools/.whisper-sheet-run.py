
import bpy, math, os, sys, json
A = json.loads(sys.argv[sys.argv.index('--') + 1])
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
meshes = []
for i, f in enumerate(A['files']):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=f)
    # 轴向：GLB 由 export_yup=False 导出 → **保持 Z-up**（实测包围盒 Z 跨度 1.85m = 站姿，
    # 与 Unity 同约定，且这是套件在用的同一约定 —— 不要改导出侧）。
    # 但 glTF 导入器按 Y-up 解释 → 模型在 Blender 里躺平。
    # 立在 Blender 里需要绕 **X 轴 −90°**（+90° 会把已躺平的模型再翻过去，实测无效）。
    # ⚠ 关键：要转**根节点**并同时把**所有后代**一起转（有些 glTF 的根是空节点，
    #    只转根不带后代时网格不动 —— 这是我在对比图上连错 3 轮的原因）。
    new_objs = [x for x in bpy.data.objects if x not in before]
    roots = [x for x in new_objs if x.parent is None]
    for r in roots:
        r.rotation_euler = (math.radians(-90), 0, 0)
        r.location.x = i * 1.25
    meshes += [x for x in new_objs if x.type == 'MESH']
m = bpy.data.materials.new("MAT-Sheet"); m.use_nodes = True
bsdf = m.node_tree.nodes.get("Principled BSDF")
bsdf.inputs['Base Color'].default_value = (0.16, 0.17, 0.20, 1.0)
bsdf.inputs['Roughness'].default_value = 0.72
for o in meshes:
    o.data.materials.clear(); o.data.materials.append(m)
cam_d = bpy.data.cameras.new("CAM-Sheet"); cam_d.type = 'ORTHO'
cam_d.ortho_scale = max(2.4, 1.25 * len(A['files']) + 0.5)
cam = bpy.data.objects.new("CAM-Sheet", cam_d); bpy.context.scene.collection.objects.link(cam)
cam.location = ((len(A['files']) - 1) * 0.625, -8.0, 1.02)
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
print("WHISPER_SHEET_RESULT " + json.dumps({"out": A['out'], "count": len(A['files'])}))
