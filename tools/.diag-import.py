import bpy, math, json, sys
A = json.loads(sys.argv[sys.argv.index("--") + 1])
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.gltf(filepath=A["glb"])
for o in bpy.data.objects:
    if o.type == "MESH":
        bb = [o.matrix_world @ __import__("mathutils").Vector(c) for c in o.bound_box]
        zs = [p.z for p in bb]; xs = [p.x for p in bb]; ys = [p.y for p in bb]
        print("MESH %s rot=%s scale=%s  worldBB x=%.3f y=%.3f z=%.3f" % (
            o.name, [round(math.degrees(v)) for v in o.rotation_euler], [round(v,3) for v in o.scale],
            max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)))
    else:
        print("NODE %s type=%s rot=%s" % (o.name, o.type, [round(math.degrees(v)) for v in o.rotation_euler]))