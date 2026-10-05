
import bpy, math, json, sys, os
A = json.loads(sys.argv[sys.argv.index('--') + 1])
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
mesh = None
bpy.ops.import_scene.gltf(filepath=A['glb'])
# GLB 是 Z-up（Unity 约定）；Blender 导入器按 Y-up 解释 → 绕 X 轴 -90° 立回来
# ⚠ 不要转！GLB 是 export_yup=False 导出的 **Z-up**，恰好与 Blender 同约定；
# 导进来是站着的。我先前加 -90° 反而把身高转到了 Y 轴（实测 H=0.218 而不是 1.95）。
for o in bpy.data.objects:
    if o.parent is None:
        o.location = (0, 0, 0)
bpy.context.view_layer.update()
mats = []
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
dg = bpy.context.evaluated_depsgraph_get()
mn = [1e9]*3; mx = [-1e9]*3
for o in meshes:
    em = o.evaluated_get(dg).to_mesh()
    for v in em.vertices:
        p = o.matrix_world @ v.co
        mn = [min(mn[k], p[k]) for k in range(3)]; mx = [max(mx[k], p[k]) for k in range(3)]
    o.evaluated_get(dg).to_mesh_clear()
H = mx[2] - mn[2]; W = mx[0] - mn[0]
cam_d = bpy.data.cameras.new('C'); cam_d.type = 'ORTHO'
cam_d.ortho_scale = max(H * 1.10, W * 1.35)
cam = bpy.data.objects.new('C', cam_d); bpy.context.scene.collection.objects.link(cam)
cam.location = ((mx[0]+mn[0])/2, -6.0, (mx[2]+mn[2])/2)
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam
for nm, e, loc, rot in (("K", 420, (-1.6, -3.2, 3.2), (math.radians(48), 0, math.radians(-26))),
                        ("F", 150, ( 2.0, -3.0, 1.5), (math.radians(78), 0, math.radians(36))),
                        ("R", 260, ( 0.3,  3.0, 2.8), (math.radians(120), 0, math.radians(178)))):
    d = bpy.data.lights.new(nm, type='AREA'); d.energy = e; d.size = 2.4
    ob = bpy.data.objects.new(nm, d); bpy.context.scene.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = rot
w = bpy.data.worlds.new("W"); bpy.context.scene.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.030, 0.032, 0.038, 1.0); bg.inputs[1].default_value = 0.6
sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y = A['w'], A['h']
sc.render.engine = 'BLENDER_EEVEE'
sc.render.filepath = A['out']
bpy.ops.render.render(write_still=True)
print('PREVIEW_OK ' + A['out'] + ' H=%.3f W=%.3f' % (H, W))
