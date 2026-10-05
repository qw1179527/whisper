// `tools/body-views.mjs` · 在**生成进程内**渲染多角度视图（正/侧/背/斜）。
//
// ## 为什么在生成进程内渲
// 我已经在"导出 GLB → 再导入 → 渲染"这条回环上错了 5 次：轴向、父级变换、取景、截断…
// `export_yup=False`（Unity 约定，套件同理，不能改）产出的 GLB 在 Blender 里再导入必然要处理轴向，
// 而**目视检查根本不需要经过 GLB**。直接在生成脚本里架相机、换角度、连拍即可。
// GLB 是否有效由 `tools/glb-bbox.mjs` 用客观数字单独证明（已验证 Z=1.95 ✓）。
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const BLENDER = process.env.WHISPER_BLENDER || 'C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe';
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
// 用 metaball 版（model3）—— 平滑有机体，Skin 版有方块感与肘部伪影
const src = fs.readFileSync(path.join(ROOT, 'tools/whisper-model3.mjs'), 'utf8');

const CANON = eval('(' + src.match(/const CANON = (\{[\s\S]*?\n\};)/)[1].replace(/;$/, '') + ')');
const BODIES = eval('(' + src.match(/const BODIES = (\{[\s\S]*?\n\};)/)[1].replace(/;$/, '') + ')');
let gen = src.match(/const PY = String\.raw`([\s\S]*?)`;/)[1];
// 截到"落地对齐"之前 —— 那一段之后是导出与读字节，目视检查用不上。
const cutMark = '# 落地对齐 + 精确身高';
const cut = gen.indexOf(cutMark);
if (cut > 0) gen = gen.slice(0, cut);
else throw new Error('未找到截断锚点');

// 生成体在原点、Z-up（与 Blender 同约定），脚底约在 z=0。相机绕 Z 轴转。
gen += String.raw`
# 截断丢掉了生成脚本里的 ARGS（它在那段之后才定义），这里直接给出来
ARGS = json.loads(sys.argv[sys.argv.index('--') + 1])
A = ARGS
# 落地对齐 + 精确身高（与正式导出保持一致，否则预览框不全或比例失真）
_mn = [1e9]*3; _mx = [-1e9]*3
for v in body.data.vertices:
    q = body.matrix_world @ v.co
    _mn = [min(_mn[k], q[k]) for k in range(3)]; _mx = [max(_mx[k], q[k]) for k in range(3)]
_curH = _mx[2] - _mn[2]
if _curH > 1e-4:
    _k = float(ARGS['height']) / _curH
    for v in body.data.vertices:
        v.co = (v.co.x * _k, v.co.y * _k, v.co.z * _k)
    _zmin = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices: v.co.z -= _zmin
    body.data.update()
# ── 目视检查：绕 Z 轴多角度连拍 ──
mesh = body
dg = bpy.context.evaluated_depsgraph_get()
em = mesh.evaluated_get(dg).to_mesh()
mn = [1e9]*3; mx = [-1e9]*3
for v in em.vertices:
    p = mesh.matrix_world @ v.co
    mn = [min(mn[k], p[k]) for k in range(3)]; mx = [max(mx[k], p[k]) for k in range(3)]
mesh.evaluated_get(dg).to_mesh_clear()
H = mx[2] - mn[2]; cz = (mx[2] + mn[2]) / 2.0
R = H * 1.35
cam_d = bpy.data.cameras.new('C'); cam_d.type = 'ORTHO'; cam_d.ortho_scale = H * 1.06
cam = bpy.data.objects.new('C', cam_d); bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam
for nm, e, loc, rot in (("K", 300, (-1.2, -2.2, 3.0), (math.radians(42), 0, math.radians(-28))),
                        ("F", 110, ( 1.6, -2.0, 1.4), (math.radians(74), 0, math.radians(38))),
                        ("R", 200, ( 0.2,  2.2, 2.6), (math.radians(118), 0, math.radians(176)))):
    d = bpy.data.lights.new(nm, type='AREA'); d.energy = e; d.size = 2.0
    ob = bpy.data.objects.new(nm, d); bpy.context.scene.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = rot
w = bpy.data.worlds.new("W"); bpy.context.scene.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.035, 0.037, 0.045, 1.0); bg.inputs[1].default_value = 0.7
sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y = A['w'], A['h']
sc.render.engine = 'BLENDER_EEVEE'
outs = []
for yaw in A['yaws']:
    a = math.radians(yaw)
    cam.location = (R * math.sin(a), -R * math.cos(a), cz + H * 0.02)
    cam.rotation_euler = (math.radians(90), 0, a)
    fp = A['out'] + '/view-' + ARGS['bodyName'] + '-' + str(yaw) + '.png'
    sc.render.filepath = fp
    bpy.ops.render.render(write_still=True)
    outs.append(fp)
print('VIEWS_OK ' + json.dumps({"H": round(H,3), "files": outs}))
`;

const args = process.argv.slice(2);
const opt = (n, d) => { const i = args.indexOf('--' + n); return i >= 0 ? args[i + 1] : d; };
const outDir = opt('out', path.join(ROOT, '_evidence/model-views'));
fs.mkdirSync(outDir, { recursive: true });
const names = opt('bodies', 'human,stocky,child,lanky,gaunt,hulk').split(',').filter((n) => BODIES[n]);
const tmp = path.join(ROOT, 'tools', '.body-views-run.py');
fs.writeFileSync(tmp, gen, 'utf8');

for (const n of names) {
  const res = execFileSync(BLENDER, ['--background', '--factory-startup', '--python', tmp, '--',
    JSON.stringify({ canon: CANON, body: BODIES[n], bodyName: n, out: outDir, tag: 'X',
                     height: parseFloat(opt('height', '1.95')), quality: opt('quality', 'low'),
                     yaws: [0, 40, 90, 180], w: 300, h: 620 })],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const m = res.match(/VIEWS_OK (\{.*\})/s);
  console.log(m ? `  ${n.padEnd(7)} 身高 ${JSON.parse(m[1]).H}m · 4 视图 ✓` : `  ${n.padEnd(7)} 失败`);
}
console.log(`  → ${outDir}`);
