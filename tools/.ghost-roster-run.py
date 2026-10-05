
import bpy, bmesh, math, json, os, sys, struct
A = globals().get('ARGS') or json.loads(sys.argv[sys.argv.index('--') + 1])
C, OUT, NAME = A['canon'], A['out'], A['name']
H = float(A['height'])
RES = {'low': 0.011, 'med': 0.008, 'high': 0.005}[A.get('quality', 'low')]
TARGETV = int(A.get('targetVerts', 6500))

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

J = dict(C['J']); J.update(A.get('joints', {}))
X = dict(C['X']); X.update(A.get('xs', {}))
R = dict(C['R'])
for k, v in A.get('widths', {}).items():
    if k in R: R[k] = R[k] * v
SQ = C['squash']
armMul = A.get('armMul', 1.0)
legMul = A.get('legMul', 1.0)
armDrop = C['armDrop'] * H * armMul
foreDrop = C['foreDrop'] * H * armMul
legSpan = (J['hip'] - J['foot']) * H

def z(n): return J[n] * H
def xx(n): return X[n] * H
def rr(n): return R[n] * H

mb = bpy.data.metaballs.new('Body')
mb.resolution = RES
mb.render_resolution = RES
mb.threshold = 0.45
obj = bpy.data.objects.new(NAME, mb)
bpy.context.scene.collection.objects.link(obj)

def blob(p, rad, squash=(1.0, 1.0, 1.0), stiffness=2.0):
    e = mb.elements.new()
    e.co = p
    e.radius = rad
    e.stiffness = stiffness
    e.size_x, e.size_y, e.size_z = squash
    return e

def seg(p0, p1, r0, r1, sq=(1.0, 1.0, 1.0)):
    """.** <= /3** ("").
     ~  radius x 0.6259( 03 ), surface = r*0.6259 ."""
    ax, ay, az = p1[0]-p0[0], p1[1]-p0[1], p1[2]-p0[2]
    L = math.sqrt(ax*ax + ay*ay + az*az)
    if L < 1e-6: return
    surf = max(min(r0, r1) * 0.6259, 1e-4)
    steps = max(4, min(120, int(L / (surf / 3.0))))
    for i in range(steps + 1):
        t = i / float(steps)
        blob((p0[0]+ax*t, p0[1]+ay*t, p0[2]+az*t), r0 + (r1-r0)*t, sq)

#  :(,)
sp = [(0,0,z('hip')), (0,0,z('s1')), (0,0,z('s2')), (0,0,z('chest'))]
sr = [rr('hip'), rr('spine1'), rr('spine2'), rr('chest')]
for i in range(3):
    seg(sp[i], sp[i+1], sr[i], sr[i+1], (SQ['torso'][0], SQ['torso'][1], SQ['torso'][2]))
# :()
seg((0,0,z('hip')*1.02), sp[0], rr('hip')*0.95, rr('hip'), (SQ['torso'][0], SQ['torso'][1], SQ['torso'][2]))
#  + ( = )
seg(sp[3], (0,0,z('neck')), rr('chest')*0.52, rr('neck'))
blob((0, 0.004*H, z('headC')), rr('head'), (1.0, 0.90, 1.05))
blob((0, -0.006*H, z('headC') + (z('headTop')-z('headC'))*0.55), rr('headTop'), (1.0, 0.92, 1.0))
blob((0, 0.010*H, z('headC') - (z('headC')-z('neck'))*0.5), rr('head')*0.62)

#  ( -> )
for sx in (1.0, -1.0):
    hip  = (sx*xx('leg'), 0, z('hip'))
    knee = (sx*xx('leg')*1.03, 0.004*H, z('knee'))
    ank  = (sx*xx('leg')*1.00, 0.010*H, z('ankle'))
    foot = (sx*xx('leg')*1.00, -0.048*H, z('foot'))
    seg((sx*xx('leg')*0.5, 0, z('hip')*1.01), hip, rr('hip')*0.9, rr('thigh'), (SQ['leg'][0], SQ['leg'][1], SQ['leg'][2]))
    seg(hip, knee, rr('thigh'), rr('knee'), (SQ['leg'][0], SQ['leg'][1], SQ['leg'][2]))
    seg(knee, ank, rr('knee')*A.get('calfMul', 1.0), rr('ankle'), (SQ['leg'][0], SQ['leg'][1], SQ['leg'][2]))
    seg(ank, foot, rr('ankle'), rr('foot'), (1.0, 1.45, 0.62))
    sh  = (sx*xx('shoulder'), 0, z('shoulder'))
    elb = (sx*xx('elbow'), 0.004*H, z('shoulder') - armDrop)
    wri = (sx*xx('wrist'), 0.012*H, z('shoulder') - armDrop - foreDrop)
    seg((sx*xx('shoulder')*0.45, 0, z('shoulder')-0.004*H), sh, rr('shoulder')*0.9, rr('shoulder'))
    seg(sh, elb, rr('arm')*1.12, rr('elbow'), (SQ['arm'][0], SQ['arm'][1], SQ['arm'][2]))
    seg(elb, wri, rr('elbow'), rr('wrist'), (SQ['arm'][0], SQ['arm'][1], SQ['arm'][2]))
    blob(wri, rr('wrist')*1.5, (1.0, 1.35, 1.0))

#  -> mesh
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.convert(target='MESH')
body = bpy.context.view_layer.objects.active
if body.type != 'MESH':
    body = [o for o in bpy.context.scene.collection.objects if o.type == 'MESH'][0]
bpy.ops.object.shade_smooth()

#  +
mn = [1e9]*3; mx = [-1e9]*3
for v in body.data.vertices:
    q = body.matrix_world @ v.co
    mn = [min(mn[k], q[k]) for k in range(3)]; mx = [max(mx[k], q[k]) for k in range(3)]
curH = mx[2]-mn[2]
if curH > 1e-4:
    k = H/curH
    for v in body.data.vertices: v.co = (v.co.x*k, v.co.y*k, v.co.z*k)
    zmin = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices: v.co.z -= zmin
    body.data.update()

# Decimate (, 20k+)
cur = len(body.data.vertices)
if cur > TARGETV*1.15:
    d = body.modifiers.new('Decimate','DECIMATE'); d.decimate_type='COLLAPSE'
    d.ratio = max(0.10, min(1.0, TARGETV/float(cur)))
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=d.name)
    bpy.ops.object.shade_smooth()

# : + SSS( = "");
m = bpy.data.materials.new('MAT-GhostBody'); m.use_nodes = True
b = m.node_tree.nodes.get('Principled BSDF')
b.inputs['Base Color'].default_value = (0.62, 0.60, 0.58, 1.0)
b.inputs['Roughness'].default_value = 0.55
for nm, val in (('Subsurface Weight', 0.30),):
    if nm in b.inputs: b.inputs[nm].default_value = val
if 'Subsurface Radius' in b.inputs:
    b.inputs['Subsurface Radius'].default_value = (1.0, 0.3, 0.25)
body.data.materials.append(m)

# :Emission( Base Color)-- /,
def emis(name, rgb, strength):
    mm = bpy.data.materials.new(name); mm.use_nodes = True; nt = mm.node_tree
    for nd in list(nt.nodes):
        if nd.type != 'OUTPUT_MATERIAL': nt.nodes.remove(nd)
    out = nt.nodes[0]; e = nt.nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    e.inputs['Strength'].default_value = strength
    nt.links.new(e.outputs['Emission'], out.inputs['Surface']); return mm

eyes = []
eyeZ = z('headC') + (z('headTop')-z('headC'))*0.10
eyeX = rr('head')*0.44
eyeR = rr('head')*0.21
for tag, mm in (('Red', emis('MAT-GhostEyeRedIris', (1.0, 0.04, 0.03), 1500.0)),
                ('White', emis('MAT-GhostEyeWhiteIris', (1.0, 1.0, 0.98), 1500.0))):
    for sx in (1.0, -1.0):
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=10, radius=eyeR)
        me = bpy.data.meshes.new('Eye'); bm.to_mesh(me); bm.free()
        o = bpy.data.objects.new('GEO-Eye' + tag + ('L' if sx > 0 else 'R'), me)
        bpy.context.scene.collection.objects.link(o)
        o.location = (sx*eyeX, -rr('head')*0.62, eyeZ); o.scale = (1.0, 0.55, 1.0)
        me.materials.append(mm)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.shade_smooth()
        eyes.append(o)

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
for o in eyes: o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
final = bpy.context.view_layer.objects.active

# UV(****: UV  glTF , 10x )
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(85), island_margin=0.005)
bpy.ops.object.mode_set(mode='OBJECT')

os.makedirs(OUT, exist_ok=True)
p = os.path.join(OUT, NAME + '.glb')
bpy.ops.object.select_all(action='DESELECT'); final.select_set(True)
bpy.context.view_layer.objects.active = final
bpy.ops.export_scene.gltf(filepath=p, export_format='GLB', use_selection=True,
                          export_apply=True, export_yup=False)

mn = [1e9]*3; mx = [-1e9]*3
for v in final.data.vertices:
    q = final.matrix_world @ v.co
    mn = [min(mn[k], q[k]) for k in range(3)]; mx = [max(mx[k], q[k]) for k in range(3)]
with open(p, 'rb') as f: buf = f.read()
jl = struct.unpack_from('<I', buf, 12)[0]
g = json.loads(buf[20:20+jl].decode('utf8'))
acc = g.get('accessors', [])
rv = sum(acc[pr['attributes']['POSITION']]['count'] for me2 in g.get('meshes', []) for pr in me2['primitives'])
print('GHOSTMODEL ' + json.dumps({
    'file': os.path.basename(p), 'bytes': os.path.getsize(p), 'verts': rv,
    'height': round(mx[2]-mn[2], 3), 'width': round(mx[0]-mn[0], 3), 'depth': round(mx[1]-mn[1], 3),
    'below_ground': round(min(mn[2], 0.0), 4), 'base': NAME, 'ratio': round((mx[2]-mn[2])/max(mx[0]-mn[0], 1e-4), 2)}))
