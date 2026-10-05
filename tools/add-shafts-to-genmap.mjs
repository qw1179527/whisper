#!/usr/bin/env node
/**
 * add-shafts-to-genmap.mjs — 生成器输出 `shafts`（两层图必需）+ 新增自检 G5
 *
 * ## 为什么
 * `bleasdale_v1` 要做**两层**，而跨层连通只能靠竖井（`level.shafts`）。
 * 我的 `gen-map.mjs` 第一版**没有输出 shafts**（只输出了 rooms/corridors/events/extraction），
 * 而 `gate-model` 的 M8 会用"竖井矩形落在哪些层的哪些房间里"把那些房间连成跨层边 ——
 * 没有竖井，二层房间**必然判不可达**。
 *
 * ## G5 自检（新增）
 * 真源形状：`{ id, kind, minX, minZ, maxX, maxZ, fromFloor, toFloor }`（读 asylum_v1.json 得到）。
 * 判据（与 M8 同原理）：竖井矩形必须在**它跨越的每一层**都与至少一个房间矩形相交 ——
 * 否则"画了竖井但那一层站不住人"，C# 侧 `LevelWorld` 会判不通。
 * 自检不过就不产出（宁可生成失败，也不要产出一个门禁必红的关卡）。
 *
 * ## 顺带
 * `kind` 限定为 lift / stair（与真源一致）；将来加别的交通方式再扩。
 *
 * 用法：node tools/add-shafts-to-genmap.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'tools/gen-map.mjs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[shafts] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('G5')) { console.log('[shafts] 已加，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① G5 自检：竖井必须落在它跨越的每一层的可走房间里 ─────────────────
sub('if (problems.length) {\n  console.error(`[gen-map] ✗ 自检 ${problems.length} 个问题（未产出）：`);',
  `// G5 竖井自检：与 gate-model 的 M8 同原理 —— 竖井矩形落在哪几层的哪些房间里，那些房间才连得上。
// 若某层矩形与竖井不相交，那一层就"站不住人"，M8 会判不通。
for (const sh of (L.shafts ?? [])) {
  if (!sh.id || !sh.kind) { problems.push('G5 竖井缺 id/kind'); continue; }
  if (!['lift', 'stair'].includes(sh.kind)) problems.push(\`G5 竖井 \${sh.id} 的 kind 非法：\${sh.kind}（合法：lift/stair）\`);
  if (!(sh.maxX > sh.minX) || !(sh.maxZ > sh.minZ)) { problems.push(\`G5 竖井 \${sh.id} 矩形非正\`); continue; }
  if (!(sh.toFloor > sh.fromFloor)) { problems.push(\`G5 竖井 \${sh.id} 层区间非正（\${sh.fromFloor}→\${sh.toFloor}）\`); continue; }
  for (let f = sh.fromFloor; f <= sh.toFloor; f++) {
    const hosted = BOXES.some((b) => {
      if ((b.floor ?? 0) !== f) return false;
      const ox = Math.min(b.x1, sh.maxX) - Math.max(b.x0, sh.minX);
      const oz = Math.min(b.z1, sh.maxZ) - Math.max(b.z0, sh.minZ);
      return ox > 0.01 && oz > 0.01;   // 与该层某房间有真实交集
    });
    if (!hosted) problems.push(\`G5 竖井 \${sh.id} 在 \${f} 层没有可走房间承载（玩家到不了）\`);
  }
}

if (problems.length) {
  console.error(\`[gen-map] ✗ 自检 \${problems.length} 个问题（未产出）：\`);`,
  '① 新增 G5 竖井自检');

// ── ② 产出里带上 shafts（真源形状）────────────────────────────────────
sub('  extraction: L.extraction,\n};   // 真源没有 bounds 键 —— 多余字段会被门禁当作可疑输入',
  `  extraction: L.extraction,
  // 竖井（跨层连通）：形状照抄真源 asylum_v1.json → { id, kind, minX, minZ, maxX, maxZ, fromFloor, toFloor }
  // 单层图给空数组（真源 asylum 有 2 条；空数组对单层图是正确表达，不是缺失）。
  shafts: (L.shafts ?? []).map((sh) => ({
    id: sh.id, kind: sh.kind, minX: sh.minX, minZ: sh.minZ, maxX: sh.maxX, maxZ: sh.maxZ,
    fromFloor: sh.fromFloor, toFloor: sh.toFloor,
  })),
};   // 真源没有 bounds 键 —— 多余字段会被门禁当作可疑输入`,
  '② 产出带 shafts');

if (!checkOnly) fs.writeFileSync(FILE, s, 'utf8');
console.log('[shafts] 生成器支持竖井' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
