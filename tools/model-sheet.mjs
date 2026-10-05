// `tools/model-sheet.mjs` · **直接在生成侧**搭"体型对比图"，不做 GLB 往返。
//
// ## 为什么另写一条
// 我先前是"生成 GLB → 重新导入 → 摆一排 → 渲染"。那条路有一个我连错 3 轮的坑：
// `export_yup=False`（**Unity 约定，套件同理，不能改**）产出的 GLB 在 Blender 里再导入会躺平，
// 而"转根节点"在 glTF 的层级里未必带得动网格。
// **正解是根本不往返**：在**同一个 Blender 会话**里把各体型的几何并排搭好、直接渲染。
// 轴向问题从根上消失，而且快得多（一次 Blender 调用出全部体型 + 一张图）。
//
// 用法：node tools/model-sheet.mjs [--quality low] [--bodies human,tall,...] [--height 1.95]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const BLENDER = process.env.WHISPER_BLENDER || 'C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe';
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// 复用 whisper-model.mjs 里的比例表与生成函数（单源，避免两处漂移）
const modelSrc = fs.readFileSync(path.join(ROOT, 'tools/whisper-model.mjs'), 'utf8');
const CANON_M = modelSrc.match(/const CANON = (\{[\s\S]*?\n\};)/);
const BODIES_M = modelSrc.match(/const BODIES = (\{[\s\S]*?\n\};)/);
const PY_M = modelSrc.match(/const PY = String\.raw`([\s\S]*?)`;/);
if (!CANON_M || !BODIES_M || !PY_M) { console.error('无法从 whisper-model.mjs 提取比例表/生成脚本'); process.exit(2); }
const CANON = eval('(' + CANON_M[1].replace(/;$/, '') + ')');
const BODIES = eval('(' + BODIES_M[1].replace(/;$/, '') + ')');
// 生成脚本：去掉它自己的导出/读取/打印尾巴，只留"建几何"部分，后面接对比图逻辑
let bodyPy = PY_M[1];
const cut = bodyPy.indexOf('bpy.ops.object.select_all(action=\'DESELECT\')');
if (cut > 0) bodyPy = bodyPy.slice(0, cut);

const SHEET_PY = String.raw`
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
`;

const args = process.argv.slice(2);
const opt = (n, d) => { const i = args.indexOf('--' + n); return i >= 0 ? args[i + 1] : d; };
const quality = opt('quality', 'low');
const height = parseFloat(opt('height', '1.95'));
const names = opt('bodies', 'human,tall,stocky,child,lanky,gaunt,hulk').split(',');
const outDir = opt('out', path.join(ROOT, '_evidence/model-sheet'));
fs.mkdirSync(outDir, { recursive: true });
const bodies = {};
for (const n of names) if (BODIES[n]) bodies[n] = BODIES[n];

const tmp = path.join(ROOT, 'tools', '.model-sheet-run.py');
fs.writeFileSync(tmp, SHEET_PY, 'utf8');
const out = path.join(outDir, 'body-types.png');
const res = execFileSync(BLENDER, ['--background', '--factory-startup', '--python', tmp, '--',
  JSON.stringify({ canon: CANON, bodies, gen: bodyPy, out, quality, height, w: 240 * names.length, h: 760 })],
  { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
for (const line of res.split('\n')) if (line.includes('SHEET_ONE')) console.log('  ' + line.trim());
console.log(res.includes('WHISPER_SHEET_RESULT') ? `  ✓ 对比图 → ${out}` : '  ! 渲染失败');
