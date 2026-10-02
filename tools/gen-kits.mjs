#!/usr/bin/env node
/**
 * gen-kits.mjs — 资产套件生成器（V9 §19.1 C3「资产零导入」/ §19.3 CI 资产管线）
 *
 * ## 为什么自产而不是下载
 * 清单原本声明了 5 个 CC0 资产的路径，但**文件一个都不存在**（连 sourceRoot 目录都没有）——
 * 即 C3 当时只是个声明。三条路：
 *   ① 从网上下载 CC0 资产 → 引入版权/来源核验负担，且本机网络对素材站不稳定；
 *   ② 继续留着声明 → 就是假绿；
 *   ③ **用 Blender 脚本化生成**（本机有 Blender 5.0.1）→ 无版权风险、确定性可复现、无网络依赖。
 * 选 ③。生成物是"灰盒套件"（简洁体块），与项目当前灰盒阶段一致；将来替换真美术资产时，
 * 只需换掉本文件的配方并重跑，清单与校验逻辑不变。
 *
 * 产物：`unity/Assets/ThirdParty/CC0/<file>`（GLB）+ 更新清单里的 sha256/size/三角面数。
 * 用法：node tools/gen-kits.mjs [--check]   （--check 只校验清单与产物是否一致，不生成）
 */
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');
const manifest = JSON.parse(fs.readFileSync(MANIFEST, 'utf8'));
const SRC_ROOT = path.join(ROOT, 'unity', manifest.sourceRoot);
const checkOnly = process.argv.includes('--check');

/** 配方：每个套件由若干盒体组成（位置 / 尺寸 / 用途色），单位米，Y 轴向上 */
const RECIPES = {
  // ── 房间套件：地面板 + 天花横梁 + 墙角柱（不含四面墙——墙由 LevelBuilder 按门洞切段生成）──
  hall_main: {
    note: '主走廊套件：地面 + 顶梁 + 柱',
    parts: [
      { name: 'floor', at: [0, -0.05, 0], size: [16, 0.1, 3], role: 'floor' },
      { name: 'beam_n', at: [0, 2.95, 1.3], size: [16, 0.1, 0.4], role: 'trim' },
      { name: 'beam_s', at: [0, 2.95, -1.3], size: [16, 0.1, 0.4], role: 'trim' },
      ...[-7.5, -2.5, 2.5, 7.5].map((x) => ({ name: `pillar_${x}`, at: [x, 1.5, 0], size: [0.3, 3, 0.3], role: 'structure' })),
    ],
  },
  hospital_ward: {
    note: '病房套件：地面 + 顶灯槽 + 窗框',
    parts: [
      { name: 'floor', at: [0, -0.05, 0], size: [3, 0.1, 4], role: 'floor' },
      { name: 'light_slot', at: [0, 3.4, 0], size: [1.6, 0.08, 0.3], role: 'light' },
      { name: 'window', at: [0, 1.8, 1.95], size: [1.4, 1.0, 0.08], role: 'trim' },
    ],
  },
  morgue: {
    note: '太平间套件：地面 + 冷柜架 + 排水沟',
    parts: [
      { name: 'floor', at: [0, -0.05, 0], size: [3, 0.1, 3], role: 'floor' },
      { name: 'drain', at: [0, 0.01, 0], size: [0.3, 0.02, 3], role: 'detail' },
      { name: 'rack_a', at: [-1.1, 0.9, 0], size: [0.5, 1.8, 2.2], role: 'structure' },
      { name: 'rack_b', at: [1.1, 0.9, 0], size: [0.5, 1.8, 2.2], role: 'structure' },
    ],
  },
  // ── 道具套件 ──
  bed_b: {
    note: '病床：床架 + 床垫 + 床头板',
    parts: [
      { name: 'frame', at: [0, 0.25, 0], size: [0.9, 0.1, 2.0], role: 'structure' },
      { name: 'mattress', at: [0, 0.38, 0], size: [0.85, 0.16, 1.9], role: 'soft' },
      { name: 'headboard', at: [0, 0.55, -0.95], size: [0.9, 0.6, 0.08], role: 'trim' },
      { name: 'leg_a', at: [-0.4, 0.1, -0.9], size: [0.08, 0.2, 0.08], role: 'structure' },
      { name: 'leg_b', at: [0.4, 0.1, -0.9], size: [0.08, 0.2, 0.08], role: 'structure' },
      { name: 'leg_c', at: [-0.4, 0.1, 0.9], size: [0.08, 0.2, 0.08], role: 'structure' },
      { name: 'leg_d', at: [0.4, 0.1, 0.9], size: [0.08, 0.2, 0.08], role: 'structure' },
    ],
  },
  cabinet_a: {
    note: '档案柜：柜体 + 三抽屉 + 标签牌',
    parts: [
      { name: 'body', at: [0, 0.6, 0], size: [0.8, 1.2, 0.5], role: 'structure' },
      ...[0.25, 0.6, 0.95].map((y, i) => ({ name: `drawer_${i}`, at: [0, y, 0.26], size: [0.7, 0.28, 0.04], role: 'trim' })),
      { name: 'label', at: [0, 1.15, 0.28], size: [0.3, 0.1, 0.02], role: 'detail' },
    ],
  },
};

/** 角色 → 材质基色（与 DesignTokens 的灰盒色一致，避免"生成的资产与配色体系脱节"） */
const ROLE_COLOR = {
  floor: [0.42, 0.40, 0.37, 1],
  structure: [0.35, 0.33, 0.31, 1],
  trim: [0.52, 0.49, 0.44, 1],
  soft: [0.60, 0.57, 0.52, 1],
  detail: [0.28, 0.27, 0.26, 1],
  light: [0.85, 0.80, 0.62, 1],
};

function blenderScript(kits) {
  const lines = [];
  lines.push('import bpy, json, os, struct');
  lines.push('KITS = json.loads(' + JSON.stringify(JSON.stringify(kits)) + ')');
  lines.push(String.raw`
def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgba):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        if "Roughness" in bsdf.inputs: bsdf.inputs["Roughness"].default_value = 0.85
        if "Metallic" in bsdf.inputs: bsdf.inputs["Metallic"].default_value = 0.0
    return m

report = {}
for kit in KITS:
    clear()
    for part in kit["parts"]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=part["at"])
        ob = bpy.context.active_object
        ob.name = part["name"]
        ob.scale = (part["size"][0], part["size"][1], part["size"][2])
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        ob.data.materials.append(mat(part["name"] + "_mat", part["rgba"]))
    out = kit["out"]
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=False)
    # 统计三角面数（导出后读 GLB JSON chunk 里的 accessor 计数太绕，直接看网格）
    tris = 0
    for ob in bpy.data.objects:
        if ob.type == 'MESH':
            ob.data.calc_loop_triangles()
            tris += len(ob.data.loop_triangles)
    report[kit["id"]] = {"tris": tris, "objects": len([o for o in bpy.data.objects if o.type == 'MESH']), "bytes": os.path.getsize(out)}
print("KIT_REPORT=" + json.dumps(report))
`);
  return lines.join('\n');
}

function buildKitList() {
  const kits = [];
  for (const k of manifest.kits) {
    const recipe = RECIPES[k.id];
    if (!recipe) { console.error(`[kits] 套件 ${k.id} 无配方（清单与配方表不同步）`); process.exit(1); }
    kits.push({
      id: k.id,
      out: path.join(SRC_ROOT, k.file),
      parts: recipe.parts.map((p) => ({
        name: p.name, at: p.at, size: p.size, rgba: ROLE_COLOR[p.role] ?? ROLE_COLOR.structure,
      })),
    });
  }
  return kits;
}

const kits = buildKitList();

if (checkOnly) {
  // 只校验：产物存在 + 清单记录的 sha256/size 与产物一致
  const problems = [];
  for (const k of manifest.kits) {
    const f = path.join(SRC_ROOT, k.file);
    if (!fs.existsSync(f)) { problems.push(`缺产物：${k.file}`); continue; }
    const buf = fs.readFileSync(f);
    const sha = createHash('sha256').update(buf).digest('hex');
    if (!k.sha256) problems.push(`${k.id} 清单缺 sha256`);
    else if (k.sha256 !== sha) problems.push(`${k.id} sha256 不一致（清单 ${k.sha256.slice(0, 12)} vs 实际 ${sha.slice(0, 12)}）`);
    if (k.bytes && k.bytes !== buf.length) problems.push(`${k.id} 字节数不一致（清单 ${k.bytes} vs 实际 ${buf.length}）`);
  }
  console.log(`[kits] 校验 ${manifest.kits.length} 个套件产物`);
  if (problems.length) { for (const p of problems) console.log('  ✗ ' + p); console.log(`  结果：${problems.length} 个问题 ✗`); process.exit(1); }
  console.log('  结果：产物与清单记录逐项一致 ✓');
  process.exit(0);
}

// 生成
const scriptPath = path.join(ROOT, 'tmp/dbg/gen-kits.py');
fs.mkdirSync(path.dirname(scriptPath), { recursive: true });
fs.writeFileSync(scriptPath, blenderScript(kits), 'utf8');

console.log(`[kits] 用 Blender 生成 ${kits.length} 个套件（确定性体块，自产 CC0）`);
let report = {};
try {
  const out = execFileSync('blender', ['-b', '--factory-startup', '--python', scriptPath], {
    encoding: 'utf8', cwd: ROOT, stdio: 'pipe', timeout: 600000,
  });
  const m = out.match(/KIT_REPORT=(\{.*\})/);
  if (m) report = JSON.parse(m[1]);
} catch (e) {
  console.error('[kits] Blender 执行失败：');
  console.error(String(e.stdout ?? '').slice(-1500));
  console.error(String(e.stderr ?? '').slice(-600));
  process.exit(1);
}

// 回写清单（sha256 / bytes / triangles）——清单是资产的唯一入口，记录必须与产物同步
let changed = 0;
for (const k of manifest.kits) {
  const f = path.join(SRC_ROOT, k.file);
  if (!fs.existsSync(f)) { console.error(`[kits] ✗ 未生成：${k.file}`); process.exit(1); }
  const buf = fs.readFileSync(f);
  const sha = createHash('sha256').update(buf).digest('hex');
  const r = report[k.id] ?? {};
  const before = JSON.stringify([k.sha256, k.bytes, k.triangles]);
  k.sha256 = sha;
  k.bytes = buf.length;
  k.triangles = r.tris ?? 0;
  k.generator = 'tools/gen-kits.mjs（Blender 脚本化生成，确定性体块）';
  if (JSON.stringify([k.sha256, k.bytes, k.triangles]) !== before) changed++;
  console.log(`  ✓ ${k.id.padEnd(15)} ${(buf.length / 1024).toFixed(1)} KB · ${r.tris ?? '?'} 三角面 · sha ${sha.slice(0, 12)}`);
}
manifest.kitsUpdatedBy = 'tools/gen-kits.mjs';
fs.writeFileSync(MANIFEST, JSON.stringify(manifest, null, 2) + '\n', 'utf8');
// 同步到 Resources（运行时经 Resources.Load 读清单）与 APK 内嵌副本
fs.copyFileSync(MANIFEST, path.join(ROOT, 'unity/Assets/Resources/Data/asset-manifest.json'));
console.log(`[kits] 清单已回写（${changed} 个条目更新）并同步到 Resources/Data`);
