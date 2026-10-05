// `tools/model-sheet2.mjs` · 逐体型独立渲染 + 客观尺寸表（正解：**不做同场景叠加**）。
//
// ## 为什么每次一个 Blender 进程
// 上一个版本想在**同一次会话**里跑 7 组参数 → 生成脚本每次用同名建体（Torso/Head/…），
// 后一次把前一次**覆盖**掉，结果只剩最后一个（实测 `SHEET_ONE` 只打出 hulk）。
// 逐体型独立会话最稳，代价只是启动开销（每次 ~5s）。
//
// ## 关键输出：**客观尺寸**而不只是图
// 每个体型报 `X/Y/Z 跨度` + 顶点数。Z 跨度 = 身高（站姿），X = 肩宽方向，Y = 前后厚度。
// 这组数字能直接验证：① 是否站姿 ② 体型之间是否真的不同 ③ 有没有超出预期。
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const BLENDER = process.env.WHISPER_BLENDER || 'C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe';
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const modelSrc = fs.readFileSync(path.join(ROOT, 'tools/whisper-model.mjs'), 'utf8');
const CANON = eval('(' + modelSrc.match(/const CANON = (\{[\s\S]*?\n\};)/)[1].replace(/;$/, '') + ')');
const BODIES = eval('(' + modelSrc.match(/const BODIES = (\{[\s\S]*?\n\};)/)[1].replace(/;$/, '') + ')');
let bodyPy = modelSrc.match(/const PY = String\.raw`([\s\S]*?)`;/)[1];
// 只保留"生成几何"的部分，截断在**导出/合并**之前。
// ⚠ 必须用 **lastIndexOf**：脚本里 select_all(action='DESELECT') 出现多次，
//   第一次出现在**腿代码之前** —— 按第一次截断会把腿整段切掉。
//   实测症状：渲染图里完全没有腿，我一开始误判为"相机裁切"，其实是**几何从未生成**。
//   教训：用关键词截断代码时，先确认该关键词在文件里出现几次、哪一次才是真正的边界。
const cut = bodyPy.lastIndexOf("bpy.ops.object.select_all(action='DESELECT')");
if (cut > 0) bodyPy = bodyPy.slice(0, cut);
bodyPy += String.raw`
# ── 追加：报告尺寸 + 渲染单个体型 ──
allmesh = [o for o in bpy.context.scene.collection.objects if o.type == 'MESH']
dg = bpy.context.evaluated_depsgraph_get()
mn = [1e9]*3; mx = [-1e9]*3; nv = 0
for o in allmesh:
    em = o.evaluated_get(dg).to_mesh()
    nv += len(em.vertices)
    for v in em.vertices:
        p = o.matrix_world @ v.co
        mn = [min(mn[k], p[k]) for k in range(3)]
        mx = [max(mx[k], p[k]) for k in range(3)]
    o.evaluated_get(dg).to_mesh_clear()
m2 = bpy.data.materials.new("MAT-Sheet"); m2.use_nodes = True
bs = m2.node_tree.nodes.get("Principled BSDF")
bs.inputs['Base Color'].default_value = (0.16, 0.17, 0.20, 1.0)
bs.inputs['Roughness'].default_value = 0.72
for o in allmesh: o.data.materials.clear(); o.data.materials.append(m2)
# 取景必须框住**全高**：ortho_scale 是水平跨度，垂直跨度 = ortho_scale * (h/w)。
# 我给 420x700 的竖图，垂直跨度 = 2.3 * 700/420 = 3.83m —— 但相机中心取在 (max+min)/2，
# 只有 1.9m 的人只占中间一半，结果**下半身被裁**（实测第一版）。改成按身高定 scale 并居中对齐。
cam_d = bpy.data.cameras.new("CAM"); cam_d.type = 'ORTHO'
_h = max(0.1, mx[2] - mn[2])
cam_d.ortho_scale = _h * 1.18 * (420.0 / 700.0)
# 相机对准**包围盒中心**（之前取 (max+min)/2 时 z 中心被算错，下半身出画）。
cx = (mx[0]+mn[0])/2; cz = (mx[1+1]+mn[1+1])/2
cam = bpy.data.objects.new("CAM", cam_d); bpy.context.scene.collection.objects.link(cam)
cam.location = (cx, -8.0, (mx[2]+mn[2])/2.0); cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam
for nm, e, loc, rot in (("K", 900, (-2.0, -4.2, 3.4), (math.radians(46), 0, math.radians(-28))),
                        ("F", 300, ( 2.6, -3.8, 1.5), (math.radians(78), 0, math.radians(38))),
                        ("R", 520, ( 0.4,  3.8, 3.0), (math.radians(122), 0, math.radians(178)))):
    d = bpy.data.lights.new(nm, type='AREA'); d.energy = e; d.size = 3.0
    ob = bpy.data.objects.new(nm, d); bpy.context.scene.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = rot
w = bpy.data.worlds.new("W"); bpy.context.scene.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.020, 0.021, 0.025, 1.0); bg.inputs[1].default_value = 0.45
sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y = 420, 700
sc.render.engine = 'BLENDER_EEVEE'
sc.render.filepath = ARGS['out'] + '/body-' + ARGS['bodyName'] + '.png'
bpy.ops.render.render(write_still=True)
print('SHEET_ONE ' + ARGS['bodyName'] + ' X=%.3f Y=%.3f Z=%.3f verts=%d' % (mx[0]-mn[0], mx[1]-mn[1], mx[2]-mn[2], nv))
`;

const args = process.argv.slice(2);
const opt = (n, d) => { const i = args.indexOf('--' + n); return i >= 0 ? args[i + 1] : d; };
const quality = opt('quality', 'low');
const height = parseFloat(opt('height', '1.95'));
const names = opt('bodies', 'human,tall,stocky,child,lanky,gaunt,hulk').split(',').filter((n) => BODIES[n]);
const outDir = opt('out', path.join(ROOT, '_evidence/model-sheet'));
fs.mkdirSync(outDir, { recursive: true });

const tmp = path.join(ROOT, 'tools', '.model-sheet2-run.py');
fs.writeFileSync(tmp, bodyPy, 'utf8');
const rows = [];
for (const n of names) {
  const res = execFileSync(BLENDER, ['--background', '--factory-startup', '--python', tmp, '--',
    JSON.stringify({ canon: CANON, body: BODIES[n], bodyName: n, out: outDir, tag: 'X', height, quality })],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const m = res.match(/SHEET_ONE (\S+) X=([\d.]+) Y=([\d.]+) Z=([\d.]+) verts=(\d+)/);
  if (m) { rows.push(m.slice(1)); console.log(`  ${m[1].padEnd(7)} 身高(Z)=${m[4]}m  肩宽(X)=${m[2]}m  厚(Y)=${m[3]}m  网格顶点 ${m[5]}`); }
  else console.log(`  ${n.padEnd(7)} 渲染失败`);
}
fs.writeFileSync(path.join(outDir, 'body-metrics.tsv'),
  'body\tX\tshoulder\tY\tthickness\tZ\theight\tverts\n' + rows.map((r) => r.join('\t')).join('\n') + '\n', 'utf8');
console.log(`  ✓ 图 → ${outDir}\\body-*.png · 尺寸表 → body-metrics.tsv`);
