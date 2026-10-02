#!/usr/bin/env node
/**
 * tokens-map-check.mjs — V9 §11 色彩 Token ↔ 产品色板 的对账双射校验
 *
 * 为什么必须有它（独立验证轨指出）：
 *   DesignTokens.cs 是由 data/design-tokens.json **生成**的，所以"
 *   生成物 == 真源取值"这类断言是同义反复 —— 结构上永远够不着"产品漂离 V9"。
 *   要防的是漂移，就必须有一张**独立于生成链**的对账表，并强制双向全覆盖。
 *
 * 校验规则：
 *   R1 V9 §11 的 7 个 token 必须逐条出现在对账表且在四类之一（matched/renamed/unresolved/... ）；
 *   R2 产品色板的每个 color 键必须被分类（matched/renamed 指向它，或在 productOnly 里列出）；
 *   R3 matched 必须同名同值；renamed 必须同值异名；
 *   R4 任一侧新增 token 而未更新对账表 → 失败（这是本脚本存在的意义）。
 *
 * 用法：node tools/tokens-map-check.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const tokens = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/design-tokens.json'), 'utf8'));
const map = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/design-tokens.v9-mapping.json'), 'utf8'));

/** V9 §11 表11-1 原文（权威）：7 个 */
const V9_TABLE_11_1 = {
  'color-paper': '#F0E6D2',
  'color-ink': '#1A1A1A',
  'color-faded': '#8C7E66',
  'color-blood': '#8B1E1E',
  'color-warning': '#C46A1A',
  'color-ghost': '#5C8C6E',
  'color-dark': '#0D0D0D',
};

const fails = [];
const productColors = tokens.color ?? {};
const classified = new Set();

// R1 + R3
for (const [name, hex] of Object.entries(V9_TABLE_11_1)) {
  const row = (map.v9 ?? []).find((r) => r.token === name);
  if (!row) { fails.push(`R1 V9 §11 token 未在对账表中：${name}`); continue; }
  if (row.hex.toUpperCase() !== hex.toUpperCase()) fails.push(`R1 ${name} 的 hex 与 V9 表11-1 不一致：表 ${hex} vs 对账 ${row.hex}`);
  if (!['matched', 'renamed', 'unresolved'].includes(row.class)) { fails.push(`R1 ${name} 的 class 非法：${row.class}`); continue; }
  if (row.class === 'matched') {
    if (!row.product || productColors[row.product] === undefined) fails.push(`R3 ${name} 标为 matched 但产品侧无该键：${row.product}`);
    else if (String(productColors[row.product]).toUpperCase() !== hex.toUpperCase()) fails.push(`R3 ${name} matched 但值不同：${productColors[row.product]} vs ${hex}`);
    if (row.product) classified.add(row.product);
  } else if (row.class === 'renamed') {
    if (!row.product || productColors[row.product] === undefined) fails.push(`R3 ${name} 标为 renamed 但产品侧无该键：${row.product}`);
    else if (String(productColors[row.product]).toUpperCase() !== hex.toUpperCase()) fails.push(`R3 ${name} renamed 但值不同：${productColors[row.product]} vs ${hex}`);
    if (row.product) classified.add(row.product);
  }
  // unresolved 不指向产品键
}
// 表里多出的 V9 token 也要报（防手写错名）
for (const row of map.v9 ?? []) {
  if (!V9_TABLE_11_1[row.token]) fails.push(`R1 对账表含 V9 §11 之外的 token：${row.token}`);
}

// R2 产品侧全分类
for (const name of Object.keys(productColors)) {
  const inProductOnly = (map.productOnly ?? []).some((r) => r.token === name);
  if (!classified.has(name) && !inProductOnly) fails.push(`R2 产品色板 token 未分类：${name}（改色板后必须更新对账表）`);
}
for (const row of map.productOnly ?? []) {
  if (productColors[row.token] === undefined) fails.push(`R2 productOnly 含产品侧不存在的 token：${row.token}`);
}

const unresolved = (map.v9 ?? []).filter((r) => r.class === 'unresolved');
console.log('[tokens-map] V9 §11 表11-1（7 个）↔ 产品色板（' + Object.keys(productColors).length + ' 个）');
for (const r of map.v9 ?? []) {
  const tag = r.class === 'matched' ? '同名同值' : r.class === 'renamed' ? `同值异名→${r.product}` : '★未裁决';
  console.log(`  ${r.class === 'unresolved' ? '⚠' : '✓'} ${r.token.padEnd(14)} ${r.hex}  ${tag}`);
}
console.log(`[tokens-map] 产品独有 ${(map.productOnly ?? []).length} 个（V9 §11 未定义，已登记）`);
if (unresolved.length) {
  console.log(`[tokens-map] ⚠ 待人类裁决 ${unresolved.length} 条：${unresolved.map((r) => r.token).join(', ')}`);
  for (const r of unresolved) console.log(`    · ${r.token}：${r.note.slice(0, 110)}…`);
}
if (fails.length) {
  console.log(`结果：${fails.length} 个问题 ✗`);
  for (const f of fails) console.log('  ✗ ' + f);
  process.exit(1);
}
console.log(`结果：双射校验通过 ✓（未裁决 ${unresolved.length} 条已显式登记，不阻塞）`);
