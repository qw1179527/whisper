#!/usr/bin/env node
/**
 * fix-genmap-schema.mjs — 让 gen-map.mjs 产出与真源一致的 schema（门禁教我的）
 *
 * ## 门禁报了什么（真源 `validate-levels.mjs` 的精确修正）
 * 我第一版产出被逐条判红，原因不是我"写错了字段名"，而是**我没照抄真源形状**：
 * | 我写的 | 真源要求（读 asylum_v1.json 得到） |
 * |---|---|
 * | `corridors[] = { doorA, doorB, widthM }` | `{ from, to, doorA, doorB, width }` —— **还必须有 from/to**，且 `width` 不是 `widthM` |
 * | `events[] = { id, kind, atRoom }` | `{ type, minute, durationSec, params, sanityEffect, counterplay }` —— 内置 6 型 + 必须有反制手段 |
 * | `bounds: {...}` | 真源**没有** bounds 键（多余字段） |
 *
 * ## 教训（写进注释，避免再犯）
 * **新增产物的 schema 一律先读真源同族文件照抄**，不要凭"听起来合理"的字段名去写。
 * 本会话我因"锚点/字段凭印象"已经反复吃亏（CS0103 缺 using、KeyCode 缺成员、这次是 schema）。
 *
 * ## 修法
 * gen-map.mjs：
 * 1. corridors 输出 `{ from, to, doorA, doorB, width }`（from/to 从门引用里拆出房间 id）；
 * 2. events 由布局表给出**完整**字段（type/minute/durationSec/params/sanityEffect/counterplay），
 *    生成器只做透传 + 校验必备字段，缺了就报错不产出；
 * 3. 去掉 bounds（真源没有）。
 * map-layouts.mjs：把 tanglewood_v1 的事件改成完整形状（内置类型：blackout 等 6 型之一）。
 *
 * 用法：node tools/fix-genmap-schema.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[schema] ✗ ' + m); process.exit(1); };

function edit(rel, pairs, tag) {
  const p = path.join(ROOT, rel);
  let s = fs.readFileSync(p, 'utf8');
  for (const [from, to] of pairs) {
    const n = s.split(from).length - 1;
    if (n !== 1) fail(`${tag}：锚点命中 ${n} 次（应为 1）→ ${from.slice(0, 60)}`);
    s = s.replace(from, to);
  }
  if (!checkOnly) fs.writeFileSync(p, s, 'utf8');
  log.push('  ✓ ' + tag);
}

// ── ① gen-map.mjs：corridors 补 from/to（并用 width）─────────────────
edit('tools/gen-map.mjs', [[
  "  corridors: LINKS.map(([a, b, w]) => ({ doorA: a, doorB: b, widthM: w })),",
  "  // 「真源形状」照抄自 unity/Assets/Levels/asylum_v1.json：\n" +
  "  //   { from, to, doorA, doorB, width } —— **from/to 是房间 id**，不是门引用；宽度键名是 width。\n" +
  "  // 我第一版只写 doorA/doorB/widthM，被 validate-levels 逐条判红（\"走廊 from 必须是字符串\"）。\n" +
  "  corridors: LINKS.map(([a, b, w]) => ({\n" +
  "    from: a.split('/')[0], to: b.split('/')[0], doorA: a, doorB: b, width: w,\n" +
  "  })),"
]], '① corridors 补 from/to、宽度改 width');

// ── ② gen-map.mjs：events 透传 + 必备字段校验；去掉 bounds ────────────
edit('tools/gen-map.mjs', [
  [
    "  events: L.events ?? [],\n  extraction: L.extraction,\n  bounds: L.bounds,\n};",
    "  events: (L.events ?? []).map((e) => {\n" +
    "    // 事件schema 照抄真源：必须带 type/minute/durationSec/counterplay（V9 §30.2 要求每个事件有明确反制手段）。\n" +
    "    // 缺字段 → 报错不产出（宁可生成失败，也不要产出一个门禁必红的关卡）。\n" +
    "    for (const k of ['type', 'minute', 'durationSec', 'counterplay']) {\n" +
    "      if (e[k] === undefined || e[k] === null) problems.push(`事件 ${e.type ?? '?'} 缺字段 ${k}`);\n" +
    "    }\n" +
    "    return { type: e.type, minute: e.minute, durationSec: e.durationSec,\n" +
    "             params: e.params ?? {}, sanityEffect: e.sanityEffect ?? 0, counterplay: e.counterplay };\n" +
    "  }),\n" +
    "  extraction: L.extraction,\n" +
    "};   // 真源没有 bounds 键 —— 多余字段会被门禁当作可疑输入",
  ],
  [
    "if (problems.length) {\n  console.error(`[gen-map] ✗ 几何自检 ${problems.length} 个问题（未产出）：`);",
    "// events 的字段校验写在上面的 map 里，故这里统一在产出前汇总判红\nif (problems.length) {\n  console.error(`[gen-map] ✗ 自检 ${problems.length} 个问题（未产出）：`);",
  ],
], '② events 完整形状 + 去掉 bounds');

// ── ③ map-layouts.mjs：事件改成完整形状 ───────────────────────────────
edit('tools/lib/map-layouts.mjs', [[
  "    events: [\n" +
  "      { id: 'e_blackout', kind: 'blackout', atRoom: 'hall_main' },\n" +
  "      { id: 'e_knock', kind: 'door_lock_shift', atRoom: 'bedroom' },\n" +
  "    ],",
  "    // 事件形状照抄真源 asylum_v1.json 的 events[]（内置 6 型之一 + 反制手段）：\n" +
  "    //   { type, minute, durationSec, params, sanityEffect, counterplay }\n" +
  "    // ⚠ counterplay 不是可选装饰：V9 §30.2 要求每个事件都有**明确反制手段**，门禁会查。\n" +
  "    events: [\n" +
  "      { type: 'blackout', minute: 6, durationSec: 10, params: { scope: 'house' },\n" +
  "        sanityEffect: -3, counterplay: '手电筒照地面确认出口；黑暗持续掉理智，回玄关（安全区）可恢复' },\n" +
  "      { type: 'door_lock_shift', minute: 12, durationSec: 8, params: { scope: 'bedroom' },\n" +
  "        sanityEffect: -2, counterplay: '卧室门短暂锁死：先做别的房间取证，或从卫生间绕行' },\n" +
  "    ],",
]], '③ tanglewood 事件改完整形状');
// 顺带：布局表里不再需要 bounds（生成器已去掉），留着无害但不一致
edit('tools/lib/map-layouts.mjs', [[
  "    bounds: { minX: 0, minZ: 0, maxX: 15, maxZ: 10 },\n",
  "",
]], '④ 布局表去掉 bounds（与真源一致）');

console.log('[schema] gen-map 产出对齐真源 schema' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
