// 给 `tools/whisper-model.mjs` 加 `verify` 子命令：一次生成全部体型并**渲染对比图**。
//
// 为什么需要：我只能靠**看渲染**判断"像不像人"（用户已两次说"烂"）。
// 逐个体型跑 Blender + 手动看太慢，所以做成一条命令：生成 → 摆到一排 → 正交正视图渲染 → 输出对比图。
// 关键：正交视图 + 同一地面线，**绝对高度可直接比对**（能立刻看出头身比、臂长、肩宽对不对）。
import fs from 'node:fs';

const P = 'tools/whisper-model.mjs';
let s = fs.readFileSync(P, 'utf8');

// ① Python 脚本里加一个"渲染对比"模式：ARGS.mode === 'sheet'
s = s.replace(
  "os.makedirs(OUT, exist_ok=True)\np = os.path.join(OUT, 'GEO-' + TAG + '_' + ARGS['bodyName'] + '.glb')",
  `os.makedirs(OUT, exist_ok=True)
p = os.path.join(OUT, 'GEO-' + TAG + '_' + ARGS['bodyName'] + '.glb')`);

// ② 导出之后：若是 sheet 模式，复制体并渲染对比图
s = s.replace(
  'print("WHISPER_MODEL_RESULT " + json.dumps({',
  `# ── 对比图模式：把当前体型复制一份摆到一排（由调用方传 sheetIndex/sheetCount），
#    最后一次调用时统一渲染。为简单起见：每次调用都把结果摆进一个临时 collection，
#    由 JS 侧串行调用后统一渲染。
print("WHISPER_MODEL_RESULT " + json.dumps({`);

// ③ 新增 renderSheet 子命令（单独一次 Blender 调用，读多个 glb 摆一排）
const sheetPy = String.raw`
import bpy, math, os, sys, json
A = json.loads(sys.argv[sys.argv.index('--') + 1])
files, out = A['files'], A['out']
# 清场
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
xs = []
for i, f in enumerate(files):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=f)
    new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    for o in new:
        o.location.x += i * 1.30          # 横向排开 1.3m
        xs.append(o)
# 材质：给全部部件统一近黑（对比图看**比例**，不看材质）
m = bpy.data.materials.new("MAT-Sheet"); m.use_nodes = True
b = m.node_tree.nodes.get("Principled BSDF")
b.inputs['Base Color'].default_value = (0.14, 0.15, 0.18, 1.0)
b.inputs['Roughness'].default_value = 0.75
for o in xs:
    o.data.materials.clear(); o.data.materials.append(m)
# 相机：正交正视图，框住整排
cam_d = bpy.data.cameras.new("CAM-Sheet"); cam_d.type = 'ORTHO'
cam_d.ortho_scale = max(2.2, 1.30 * len(files) + 0.6)
cam = bpy.data.objects.new("CAM-Sheet", cam_d)
bpy.context.scene.collection.objects.link(cam)
cam.location = ((len(files) - 1) * 0.65, -6.0, 1.05)
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam
# 光：三点（正面偏上，避免平光看不出形体）
for nm, e, loc, rot in (("L1", 700, (-2.0, -4.0, 3.4), (math.radians(48), 0, math.radians(-26))),
                        ("L2", 260, ( 2.6, -3.6, 1.7), (math.radians(76), 0, math.radians(36))),
                        ("L3", 420, ( 0.4,  3.6, 3.0), (math.radians(120), 0, math.radians(178)))):
    d = bpy.data.lights.new(nm, type='AREA'); d.energy = e; d.size = 3.0
    o = bpy.data.objects.new(nm, d); bpy.context.scene.collection.objects.link(o)
    o.location = loc; o.rotation_euler = rot
w = bpy.data.worlds.new("W"); bpy.context.scene.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.02, 0.021, 0.025, 1.0); bg.inputs[1].default_value = 0.5
sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y = A['w'], A['h']
sc.render.engine = 'BLENDER_EEVEE'
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("WHISPER_SHEET_RESULT " + json.dumps({"out": out, "count": len(files)}))
`;

fs.writeFileSync(P, s, 'utf8');

// ④ CLI：加 verify 子命令
const cliAnchor = "} else {\n  console.log('用法：node tools/whisper-model.mjs list');";
const cliNew = `} else if (cmd === 'verify') {
  const quality = opt('quality', 'low');
  const out = opt('out', path.join(ROOT, '_evidence/model-sheet'));
  fs.mkdirSync(out, { recursive: true });
  const bodies = (opt('bodies', 'human,tall,stocky,child,lanky,gaunt,hulk')).split(',');
  const files = [];
  for (const b of bodies) {
    if (!BODIES[b]) { console.log('  跳过未知体型 ' + b); continue; }
    const r = runBlender(PY, { canon: CANON, body: BODIES[b], bodyName: b, out, tag: 'GhostBody',
                               height: opt('height', '1.95'), quality });
    files.push(r.path);
    console.log(\`  \${b.padEnd(7)} \${r.glb_vertices} 顶点 · \${(r.bytes / 1024).toFixed(0)} KB\`);
  }
  if (!files.length) { console.error('  没有生成任何体型'); process.exit(2); }
  const sheet = path.join(out, 'body-types.png');
  const tmp = path.join(ROOT, 'tools', '.whisper-sheet-run.py');
  fs.writeFileSync(tmp, sheetPy, 'utf8');
  const res = execFileSync(BLENDER, ['--background', '--factory-startup', '--python', tmp, '--',
    JSON.stringify({ files, out: sheet, w: 300 * files.length, h: 900 })], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const m = res.match(/WHISPER_SHEET_RESULT (\\{.*\\})/s);
  console.log(m ? '  ✓ 对比图 ' + sheet : '  ! 对比图渲染失败');
} else {
  console.log('用法：node tools/whisper-model.mjs list');`;
if (s.includes(cliAnchor)) { s = s.replace(cliAnchor, cliNew); console.log('  ✓ verify 子命令已加'); }
else console.log('  ! CLI 锚点未找到');

s = fs.readFileSync(P, 'utf8').replace("  console.log('用法：node tools/whisper-model.mjs list');",
  "  console.log('用法：node tools/whisper-model.mjs list');");
fs.writeFileSync(P, s, 'utf8');
console.log('  ✓ 完成');
