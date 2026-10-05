// 把 **鬼怪模型池** 落进配置（`data/config.json` + 3 个镜像）。
//
// ## 用户规则（2026-10-05 明令，已钉长期记忆）
// 「鬼的建模是通用的，不与鬼的类型绑定，所有鬼的建模都正常化，不要说比如随到幻影就用没腿的建模，
//   这是错误的，所有的鬼都随机几个建模（分男女建模），只是按机制体现不同而已」
//
// ## 因此本脚本做三件**并只做这三件**事
//   ① 写一个 `ghosts.modelPool` = **扁平列表**（8 个模型：4 男 4 女）
//   ② 写 `ghosts.modelSelection` = "matchSeed"（开局按匹配种子随机取，确定性）
//   ③ **断言配置里不存在任何"类型→模型"的绑定**（含 `monsters.<类型>.model` 这类字段）
//      —— 这是把用户规则变成**机器判据**，防止以后有人又接上。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// ⚠ 不要用 `new URL(import.meta.url).pathname` —— 中文路径会变成 %XX，fs 直接 ENOENT。
// 本项目目录含中文，这个坑我已经踩了 4 次（whisper-model.mjs / model-sheet.mjs / 本文件）。
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const TARGETS = [
  'data/config.json',
  'unity/Assets/Data/config.json',        // 运行时真源（data-mirror 声明）
  'unity/Assets/Resources/Data/config.json',
  'native/micprobe/res/raw/config.json',
];

// 模型池：从实际产物目录读（**不手写文件名**，避免与产物脱钩）
const MODEL_DIR = path.join(ROOT, 'unity/Assets/Resources/Models/ghostbody');
const files = fs.readdirSync(MODEL_DIR).filter((f) => /^GEO-GhostBody_(female|male)_.*\.glb\.bytes$/.test(f)).sort();
if (files.length === 0) { console.error('  ✗ 找不到模型产物，先跑 tools/gen-ghost-roster-models.mjs'); process.exit(2); }

const pool = files.map((f) => {
  const m = f.match(/^GEO-GhostBody_(female|male)_(\w+)\.glb\.bytes$/);
  return {
    id: f.replace('GEO-GhostBody_', '').replace('.glb.bytes', ''),
    sex: m[1],
    frame: m[2],
    // `Resources.Load` 的路径**省略最后一层扩展名**（本工程约定）→ 指向 .glb
    resPath: 'Models/ghostbody/' + f.replace('.bytes', ''),
  };
});

let patched = 0;
for (const rel of TARGETS) {
  const p = path.join(ROOT, rel);
  if (!fs.existsSync(p)) { console.log('  · 跳过（不存在）' + rel); continue; }
  const cfg = JSON.parse(fs.readFileSync(p, 'utf8'));
  cfg.ghosts = cfg.ghosts || {};
  cfg.ghosts.modelPool = pool;
  cfg.ghosts.modelSelection = {
    rule: 'matchSeed',
    note: '开局由匹配种子确定性随机取一个；**与鬼的类型无关**（用户规则 2026-10-05）。',
  };
  cfg.ghosts.modelRules = {
    allHumanoid: true,
    note: '全部为完整人形（有腿有臂有头）。禁止"无腿/雾状尾梢/漂浮"这类按类型残缺失真的做法。',
  };
  fs.writeFileSync(p, JSON.stringify(cfg, null, 2) + '\n', 'utf8');
  patched++;
}
console.log(`  ✓ 模型池 ${pool.length} 个（女 ${pool.filter((x) => x.sex === 'female').length} · 男 ${pool.filter((x) => x.sex === 'male').length}）→ ${patched} 个配置`);

// ── 判据：**配置里不得存在"类型→模型"绑定** ──
const problems = [];
for (const rel of TARGETS) {
  const p = path.join(ROOT, rel);
  if (!fs.existsSync(p)) continue;
  const cfg = JSON.parse(fs.readFileSync(p, 'utf8'));
  // ① monsters.* 下不得有 model / modelId / bodyType 之类
  for (const [mid, mv] of Object.entries(cfg.monsters || {})) {
    if (!mv || typeof mv !== 'object') continue;
    for (const bad of ['model', 'modelId', 'bodyType', 'body', 'mesh']) {
      if (bad in mv) problems.push(`${rel}: monsters.${mid}.${bad} 存在 —— 违反"模型不与类型绑定"`);
    }
  }
  // ② ghosts.* 下（若不是我们写的那两项）不得出现形如 {"类型名": {"model": ...}} 的结构
  for (const [gid, gv] of Object.entries(cfg.ghosts || {})) {
    if (['modelPool', 'modelSelection', 'modelRules'].includes(gid)) continue;
    if (gv && typeof gv === 'object') {
      for (const bad of ['model', 'modelId', 'bodyType', 'body', 'mesh']) {
        if (bad in gv) problems.push(`${rel}: ghosts.${gid}.${bad} 存在 —— 违反"模型不与类型绑定"`);
      }
    }
  }
}
if (problems.length) {
  console.error('  ✗ 判据不成立：配置里存在"类型→模型"绑定');
  for (const x of problems) console.error('     · ' + x);
  process.exit(1);
}
console.log('  ✓ 判据：配置里不存在任何"类型→模型"绑定（allHumanoid 亦已声明）');
