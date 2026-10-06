import bpy, os, sys
argv = sys.argv[sys.argv.index("--")+1:]
out = argv[0]
# 只导出 GEO-* 部件
bpy.ops.object.select_all(action='DESELECT')
n=0
for o in bpy.data.objects:
    if o.type=='MESH' and o.name.startswith('GEO-'):
        o.select_set(True); n+=1
print("选中部件数", n)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True,
                          export_yup=True, export_apply=False)
print("已导出", out, os.path.getsize(out), "字节")
