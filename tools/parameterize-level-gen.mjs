#!/usr/bin/env node
/**
 * parameterize-level-gen.mjs — 让关卡生成器支持 `--layout/--out`，并为第二张图铺路
 *
 * ## 目标（用户要求「接入多地图选项」的施工第二步）
 * 现状：`tools/gen-asylum-v1.mjs` 只有一份布局表、输出路径硬编码（`:345`），**不吃命令行参数**。
 * 所以要加第二张图，必须让它可参数化。
 *
 * ## 关键约束：**不能改变 asylum_v1 的既有产物**（逐字节）
 * 该文件的产物 `unity/Assets/Levels/asylum_v1.json` 被 `validate-levels` / `data-mirror` /
 * `gate-model` 三门禁引用。改造只要让产物哪怕多一个空格，就等于动了下游真源。
 * 因此本脚本只做**最小、向后兼容**的一处改动：
 *   · 把 `const level = {...}` 标成 `export`（供新生成器复用同一套几何规则）；
 *   · 末尾的 `fs.writeFileSync(OUT, …)` 改为**带参数时按 `--out` 写、不带参数时行为与原来完全一致**；
 *   · 支持 `--layout`：带上时打印提示并走新生成器（避免两套口径各写一遍）。
 *
 * ## 新布局放哪
 * `tools/lib/map-layouts.mjs`（**ASCII 文件名**——本机对中文路径写文件会报 ReplaceFileW EIO）。
 * 新的通用生成器 `tools/gen-map.mjs` 读它 + 复用 `gen-asylum-v1.mjs` 导出的几何函数。
 *
 * 用法：node tools/parameterize-level-gen.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'tools/gen-asylum-v1.mjs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[param] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('parseArgs')) { console.log('[param] 已参数化，跳过'); process.exit(0); }
let src = raw;

// ── ① 导出 level 与关键函数（供新生成器复用同一套几何规则）──────────────
{
  const anchor = 'const level = {';
  const n = src.split(anchor).length - 1;
  if (n !== 1) fail(`level 锚点命中 ${n} 次（应为 1）`);
  src = src.replace(anchor, 'export const level = {');
  log.push('  ✓ ① 导出 level（新生成器复用同一套几何）');
}

// ── ② 产物写出：默认行为不变，带 --out 时写到指定路径 ─────────────────
{
  const anchor = "const OUT = path.join(ROOT, 'unity/Assets/Levels/asylum_v1.json');\nfs.writeFileSync(OUT, JSON.stringify(level, null, 2) + '\\n', 'utf8');";
  const n = src.split(anchor).length - 1;
  if (n !== 1) fail(`产出锚点命中 ${n} 次（应为 1）`);
  const to = [
    '// ── 产出路径：**默认与历史行为逐字节一致**；带 --out 时写到指定文件 ──',
    '// 为什么要参数化：多地图（用户要求）需要在同一套几何规则下产出多张关卡；',
    '// 但 asylum_v1.json 已被 validate-levels / data-mirror / gate-model 三门禁引用，',
    '// 任何"顺手改一下产物"都可能动到下游真源，故默认路径与写法保持原样。',
    'const argv = process.argv.slice(2);',
    'const argOf = (name, def) => {',
    '  const i = argv.indexOf(name);',
    '  return i >= 0 && i + 1 < argv.length ? argv[i + 1] : def;',
    '};',
    "const OUT = argOf('--out', path.join(ROOT, 'unity/Assets/Levels/asylum_v1.json'));",
    "fs.writeFileSync(OUT, JSON.stringify(level, null, 2) + '\\n', 'utf8');",
    "console.log('[gen-asylum] 已写出 ' + path.relative(ROOT, OUT) + ' · 房间 ' + level.rooms.length);",
  ].join('\n');
  src = src.replace(anchor, to);
  log.push('  ✓ ② 产出支持 --out（默认路径与写法不变）');
}

// ── ③ --layout 提示走新生成器（避免两套口径各写一遍）──────────────────
{
  const anchor = 'export const level = {';
  const to = [
    '// ── 多地图入口：--layout <id> 交给 tools/gen-map.mjs（同一套几何，不同布局表）──',
    '// 为什么不让本文件直接吃全部布局：本文件的编码/门对齐/道具寻位是**经过门禁验证**的一套，',
    '// 新布局应复用同一套规则，而不是复制一份出来慢慢漂移。',
    "if (process.argv.includes('--layout')) {",
    "  console.error('[gen-asylum] 带 --layout 请改用：node tools/gen-map.mjs --layout <id> --out <file>');",
    '  process.exit(2);',
    '}',
    '',
    anchor,
  ].join('\n');
  const n = src.split(anchor).length - 1;
  if (n !== 1) fail('level 导出锚点在生成器里不唯一');
  src = src.replace(anchor, to);
  log.push('  ✓ ③ --layout 明确指向 gen-map.mjs（单一实现，防漂移）');
}

if (!checkOnly) {
  const bak = FILE + '.bak-param';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
}
console.log('[param] 关卡生成器参数化' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
console.log('  下一步：node tools/gen-map.mjs --layout tanglewood_v1 --out unity/Assets/Levels/tanglewood_v1.json');
