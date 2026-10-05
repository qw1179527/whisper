#!/usr/bin/env node
/**
 * remove-invented-ui.mjs — 删掉**我自己编的**两行 UI（用户第 4 条：你看官方文档了吗就瞎写界面）
 *
 * ## 我编了什么（对照官方原文逐条打脸）
 * 第 12 轮我加了两个元素，**官方大厅形态里根本没有**：
 * | 我加的 | 官方原文（《补充说明》第 5 行） |
 * |---|---|
 * | `_navHintText`：「◀ 地图　｜　菜单板　｜　商店 ▶」 | 官方是「在地点选择、主菜单和商店之间**快速跳转的按钮**」——是**按钮**（UI 控件，在菜单界面里），**不是场景里的一行文字** |
 * | `_taskBoardText`：「每日挑战 · 每周挑战」 | 官方是「在主菜单板上**能看到**每日挑战和每周挑战」——即**菜单板上的内容**（板面一部分），**不是场景里独立漂浮的一行字** |
 *
 * ## 处置
 * 两个都**不再创建**（连同它们在 `UpdateBoardUi` 里的跟随相机代码）。
 * 正确的做法在后续轮次：快速跳转做成**菜单界面里的按钮**；挑战做进**菜单板板面**里。
 *
 * ## 纪律（写进注释避免再犯）
 * **凡是官方没明写的 UI 元素，一律不加。** 本项目"禁止 Guessing"是用户从第一天就立下的约束
 * （《新指导》：不清楚的地方不盲目进行，不猜，不靠记忆），而我在第 12 轮恰恰违反了一次。
 *
 * 用法：node tools/remove-invented-ui.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[rm-ui] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('remove-invented-ui')) { console.log('[rm-ui] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 删掉两个 Label 的创建 ──────────────────────────────────────────
{
  const a = s.indexOf('            // ── 官方 §4：每日/每周挑战**显示在主菜单板上**（原先只在已废弃的旧左面板里）──');
  const b = s.indexOf('_navHintText.color = new Color(0.80f, 0.84f, 0.90f);');
  if (a < 0 || b < 0) fail('未找到待删的两个元素（可能已被改）');
  const end = s.indexOf('\n', b) + 1;
  s = s.slice(0, a) + [
    '            // 【用户 2026-10-05：「你看官方文档了吗就瞎写界面」——我确实编了两个官方没有的元素，此处不再创建】',
    '            // · 原 `_navHintText`「◀ 地图｜菜单板｜商店 ▶」：官方是**快速跳转按钮**（菜单界面里的控件），',
    '            //   不是场景里漂浮的一行文字；后续做成真正的 UI 按钮。',
    '            // · 原 `_taskBoardText`「每日挑战 · 每周挑战」：官方是"菜单板上**能看到**挑战"，',
    '            //   即板面内容的一部分；后续做进菜单板板面里，而不是独立一行字。',
    '            // 纪律：**官方没明写的 UI 元素一律不加**（用户《新指导》"不清楚的地方不盲目进行，不猜"）。',
    '            // remove-invented-ui：此注释为幂等标记，勿删。',
    '',
  ].join('\n') + s.slice(end);
  log.push('  ✓ ① 不再创建 _navHintText / _taskBoardText');
}

// ── ② 删掉字段声明 ───────────────────────────────────────────────────
sub('        /// <summary>主菜单板上的每日/每周挑战行（官方 §4 要求显示在板上）。</summary>\n        Text _taskBoardText;\n        /// <summary>快速跳转提示（地点 ↔ 主菜单 ↔ 商店）。</summary>\n        Text _navHintText;',
  '        // `_taskBoardText` / `_navHintText` 已删除：它们是我编的、官方形态里没有的元素（见 remove-invented-ui.mjs）。',
  '② 删除两个字段声明');

// ── ③ 删掉 UpdateBoardUi 里的跟随相机代码块 ─────────────────────────
{
  const a = s.indexOf('            // 挑战行与快捷提示跟随相机投影（与 BoardItem 同一口径，避免另造一套坐标换算）。');
  if (a < 0) fail('未找到跟随相机代码块');
  const b = s.indexOf('            }', s.indexOf('PlaceAtScreen(_navHintText', a));
  if (b < 0) fail('未找到跟随相机代码块结尾');
  const end = s.indexOf('\n', b) + 1;
  s = s.slice(0, a) + '            // （原"挑战行/快捷提示跟随相机"代码块已随两个元素一起删除）\n' + s.slice(end);
  log.push('  ✓ ③ 删除跟随相机代码块');
}

if (!checkOnly) {
  const bak = FILE + '.bak-rmui';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[rm-ui] 删除我编造的两行 UI' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
