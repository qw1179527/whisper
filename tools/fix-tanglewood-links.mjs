#!/usr/bin/env node
/**
 * fix-tanglewood-links.mjs — 把 tanglewood_v1 里故意留的坏引用换成真实共享墙上的门对
 *
 * ## 上一轮的验证（保留记录）
 * `tools/lib/map-layouts.mjs` 里我**故意**写了 `kitchen/d_south2_placeholder` 这个不存在的门引用，
 * 实测 `node tools/gen-map.mjs --layout tanglewood_v1 …` → `G2 连接表引用了不存在的门` 且 **exit=1、未产出文件**。
 * 这证明自检真的会拦，不是摆设。
 *
 * ## 本次修正
 * 厨房（x4..7, z3..6）与卫生间（x7..10, z3..6）**共享东/西墙 x=7**，z 范围 3..6（中心 z=4.5）。
 * 故：
 *   · kitchen 加 `{ id:'d_east', wall:'east', at:4.5 }`
 *   · bath 已有 `{ id:'d_west', wall:'west', at:4.5 }` → 两者共享墙 x=7、开口中心 z=4.5 → 对齐 ✓
 *   · links 第 3 条改为 `['kitchen/d_east', 'bath/d_west', 1.2]`
 *
 * 用法：node tools/fix-tanglewood-links.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'tools/lib/map-layouts.mjs');
const checkOnly = process.argv.includes('--check');

let s = fs.readFileSync(FILE, 'utf8');
const log = [];
const fail = (m) => { console.error('[fix-links] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ① kitchen 加东门（与 bath 的西门共享 x=7 墙、中心 z=4.5）
sub(`        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 5.5 }] },`,
  `        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [
          { id: 'd_south', wall: 'south', at: 5.5 },
          // 东门：与卫生间西门共享 x=7 的墙（两房 z 范围都是 3..6，中心 z=4.5）
          { id: 'd_east', wall: 'east', at: 4.5 },
        ] },`,
  '① kitchen 增加东门（与 bath 共享 x=7 墙）');

// ② 连接表换成真实门对
sub("      ['kitchen/d_south2_placeholder', 'bath/d_west', 1.2],   // 占位：下一轮改为真实共享墙（见文件末注）",
  "      ['kitchen/d_east', 'bath/d_west', 1.2],",
  '② 连接表改为 kitchen/d_east ↔ bath/d_west');

// ③ 文件末那条"已知待修"注释已过期，替换为完成说明
{
  const i = s.indexOf('/*\n * ⚠ 已知待修（下一轮）：');
  if (i >= 0) {
    s = s.slice(0, i) + `/*
 * 自检验证记录（2026-10-05）：
 * 本表的 links 最初**故意**含一条坏引用（kitchen/d_south2_placeholder），用于验证
 * \`tools/gen-map.mjs\` 的 G2 自检真的会拦 —— 实测 exit=1 且未产出文件。
 * 随后改为真实共享墙上的门对：kitchen/d_east ↔ bath/d_west（共享 x=7 墙，中心 z=4.5，开口对齐）。
 */
`;
    log.push('  ✓ ③ 文件末注释更新为"自检验证记录"');
  } else {
    log.push('  · 文件末注释已是新形式，跳过');
  }
}

if (!checkOnly) fs.writeFileSync(FILE, s, 'utf8');
console.log('[fix-links] tanglewood_v1 连接表修正' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
