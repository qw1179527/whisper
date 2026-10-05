#!/usr/bin/env node
/**
 * fix-bleasdale-layout.mjs — 按 G1/G2/G3/G4 自检结论修正第三张图的二层布局
 *
 * ## 自检报的 9 条问题与逐条修法
 * | 报错 | 根因 | 修法 |
 * |---|---|---|
 * | `G1 同层重叠：corridor_f1 与 attic_f1（2.00×4.00m）` | 走廊 x4..10 与阁楼 x8..12 在 x8..10 重叠 | 阁楼移到 **x10..14**（贴走廊东墙 x=10） |
 * | `G4 corridor_f0/d_west 门洞中心 2.5 越界（墙 1..3，宽 2）` | 墙长只有 2m（z1..3），放 2.0m 门时中心必须在 2.0，写 2.5 必越界 | 走廊改成 **z1..4**（墙长 3），门宽 2.0 @ 中心 2.5 → 合法 |
 * | `G2 entrance/d_east 与 corridor_f0/d_west 开口未对齐（2.50 vs 2.00）` | 同上（夹取后中心被推到 2.0） | 由上行一并解决：两门都取 2.5 |
 * | `G2 kitchen_f0/d_south_02(south) 与 back_hall_f0/d_west(west) 不相对` | south 与 west **不是相对墙** —— 我配错了 | 后厅改从**南侧**进入：后厅门放 `north` 墙（z=6 与厨房南墙共享），与厨房 `south` 相对 |
 * | `G2 corridor_f1/d_east 与 attic_f1/d_west 不在共享墙（10 vs 8）` | 阁楼 x8..12，与走廊东墙 x=10 不贴 | 阁楼移到 x10..14 后，其西墙就是 x=10 = 走廊东墙 ✓ |
 * | `G3 不可达：corridor_f1 / master_f1 / second_f1 / attic_f1` | 二层**只能靠竖井到达**，而我只在 links 里写了同层走廊连接、**没有把竖井算进可达图**（G3 目前只按 links 建图） | 二层房间通过走廊互连（已有）+ **竖井**跨层；G3 的图里要加"竖井边"——本轮同时修 G3 |
 *
 * ## 关于 G3 的一个重要澄清
 * 让 G3 把竖井算成跨层边**不是放松判据**：`gate-model` 的 M8 就是这么算的
 * （竖井矩形落在哪几层的哪些房间里 → 连成边）。G3 作为"产出前自检"，应与 M8 同口径，
 * 否则会出现"自检说不可达、门禁说可达"的两套口径 —— 那正是本项目反复踩的坑。
 *
 * 用法：node tools/fix-bleasdale-layout.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[bleasc] ✗ ' + m); process.exit(1); };

// ════ 一、布局表修正（map-layouts.mjs）════════════════════════════════
{
  const p = path.join(ROOT, 'tools/lib/map-layouts.mjs');
  let s = fs.readFileSync(p, 'utf8');
  const sub = (from, to, what) => {
    const n = s.split(from).length - 1;
    if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
    s = s.replace(from, to);
    log.push('  ✓ ' + what);
  };

  // ① 一层走廊 z1..3 → z1..4（墙长 3，容得下 2.0m 门）
  sub("{ id: 'corridor_f0', x0: 4, x1: 10, z0: 1, z1: 3, h: 3.0, floor: 0,",
    "{ id: 'corridor_f0', x0: 4, x1: 10, z0: 1, z1: 4, h: 3.0, floor: 0,   // z1..4：墙长 3，容得下 2.0m 门（原先 z1..3 只 2m，门中心必越界）",
    '① 一层走廊 z1..4（让 2.0m 门合法）');

  // ② 厨房南墙 z3..6 → z4..6（避开走廊变深后的重叠：走廊现在到 z=4）
  sub("{ id: 'kitchen_f0', x0: 4, x1: 8, z0: 3, z1: 6, h: 3.0, floor: 0,",
    "{ id: 'kitchen_f0', x0: 4, x1: 8, z0: 4, z1: 7, h: 3.0, floor: 0,   // z4..7：避开加深后的走廊，仍与后厅共享 z=7 墙",
    '② 厨房 z4..7（避开加深后的走廊）');
  // 厨房两扇南门：与走廊(d_n_kitchen at x=5.5) 和 后厅，门位随之更新
  sub("          { id: 'd_south', wall: 'south', at: 5.5 },\n          { id: 'd_south_02', wall: 'south', at: 6.0 },",
    "          { id: 'd_south', wall: 'south', at: 5.5 },      // ↔ 走廊北门（共享 z=7 墙）\n          { id: 'd_south_02', wall: 'south', at: 6.5 },   // ↔ 后厅北门",
    '② 厨房两扇南门门位更新');

  // ③ 一层走廊北门：与厨房共享 z=7 墙？走廊 z1..4 → 北墙是 z=4，而厨房从 z=4 开始 → 共享 z=4
  sub("          { id: 'd_west', wall: 'west', at: 2.5 },\n          { id: 'd_n_kitchen', wall: 'north', at: 5.5 },",
    "          { id: 'd_west', wall: 'west', at: 2.5 },\n          { id: 'd_n_kitchen', wall: 'north', at: 5.5 },   // 共享 z=4 墙（走廊北墙 = 厨房南墙起点）",
    '③ 一层走廊北门注释（共享 z=4）');

  // ④ 后厅：z6..9 → z7..9？与厨房共享 z=7 墙 → 后厅 z7..10，门放 north 墙 at x=6.5
  sub("{ id: 'back_hall_f0', x0: 4, x1: 10, z0: 6, z1: 9, h: 3.0, floor: 0,\n        kit: 'hall_main_corridor_link', zone: 'pressure', evidence: false, props: [],\n        doors: [{ id: 'd_west', wall: 'west', at: 7.5 }] },",
    "{ id: 'back_hall_f0', x0: 4, x1: 10, z0: 7, z1: 10, h: 3.0, floor: 0,\n        kit: 'hall_main_corridor_link', zone: 'pressure', evidence: false, props: [],\n        // 从**北墙**进（与厨房南墙共享 z=7）；原先配 west 与厨房的 south 不相对 —— 自检 G2 抓到的\n        doors: [{ id: 'd_north', wall: 'north', at: 6.5 }] },",
    '④ 后厅改为从北墙进入（与厨房南墙相对）');

  // ⑤ 竖井随房间改动重新落位：一层后厅 z7..10、二层走廊 z4..8 → 取交集 x6..9, z7..8
  sub("      { id: 'stair_well', kind: 'stair', minX: 6, minZ: 6, maxX: 9, maxZ: 9, fromFloor: 0, toFloor: 1 },",
    "      // 竖井矩形必须同时落在一层后厅(x4..10,z7..10) 与二层走廊(x4..10,z4..8) 内 → 取交集 z7..8\n      { id: 'stair_well', kind: 'stair', minX: 6, minZ: 7, maxX: 9, maxZ: 8, fromFloor: 0, toFloor: 1 },",
    '⑤ 竖井重新落位（两层交集）');

  // ⑥ 厨房→后厅 的连接改引后厅北门
  sub("      ['kitchen_f0/d_south_02', 'back_hall_f0/d_west', 1.6],",
    "      ['kitchen_f0/d_south_02', 'back_hall_f0/d_north', 1.6],",
    '⑥ 厨房→后厅 连接改为北门');

  // ⑦ 二层：阁楼移到 x10..14（贴走廊东墙），走廊门改 z=6、北门改 x5.0/x7.0（与新房间对齐）
  sub("{ id: 'attic_f1', x0: 8, x1: 12, z0: 4, z1: 8, h: 3.0, floor: 1,",
    "{ id: 'attic_f1', x0: 10, x1: 14, z0: 4, z1: 8, h: 3.0, floor: 1,   // 贴走廊东墙 x=10（原先 x8..12 与走廊重叠 —— G1 抓到）",
    '⑦ 阁楼移到 x10..14');

  if (!checkOnly) fs.writeFileSync(p, s, 'utf8');
  log.push('  ✓ 布局表已更新');
}

// ════ 二、G3 连通性自检：把竖井算成跨层边（与 gate-model M8 同口径）════
{
  const p = path.join(ROOT, 'tools/gen-map.mjs');
  let s = fs.readFileSync(p, 'utf8');
  const from = `  const startId = BOXES[0].id;
  const seen = new Set([startId]);
  const q = [startId];
  while (q.length) { for (const n of adj.get(q.shift()) ?? []) if (!seen.has(n)) { seen.add(n); q.push(n); } }`;
  if (s.split(from).length - 1 !== 1) fail('G3 锚点未命中');
  const to = `  // ⚠ 竖井必须算成**跨层边**：二层房间只能靠竖井到达。
  // 这不是放松判据 —— gate-model 的 M8 就是这么算的（竖井矩形落在哪几层的哪些房间里 → 连边）。
  // G3 作为"产出前自检"必须与 M8 同口径，否则会出现"自检说不可达、门禁说可达"的两套口径。
  for (const sh of (L.shafts ?? [])) {
    const hosted = [];
    for (const b of BOXES) {
      if ((b.floor ?? 0) < sh.fromFloor || (b.floor ?? 0) > sh.toFloor) continue;
      const ox = Math.min(b.x1, sh.maxX) - Math.max(b.x0, sh.minX);
      const oz = Math.min(b.z1, sh.maxZ) - Math.max(b.z0, sh.minZ);
      if (ox > 0.01 && oz > 0.01) hosted.push(b.id);
    }
    for (let i = 1; i < hosted.length; i++) {
      adj.get(hosted[0])?.push(hosted[i]);
      adj.get(hosted[i])?.push(hosted[0]);
    }
  }
  const startId = BOXES[0].id;
  const seen = new Set([startId]);
  const q = [startId];
  while (q.length) { for (const n of adj.get(q.shift()) ?? []) if (!seen.has(n)) { seen.add(n); q.push(n); } }`;
  s = s.replace(from, to);
  if (!checkOnly) fs.writeFileSync(p, s, 'utf8');
  log.push('  ✓ G3 把竖井算成跨层边（与 M8 同口径）');
}

console.log('[bleasc] 第三张图布局修正' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
