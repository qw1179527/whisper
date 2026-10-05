#!/usr/bin/env node
/**
 * fix-join-hittest.mjs — 把「点提示条加入房间」的命中判定改成项目既有范式
 *
 * ## 缺陷（真语法门禁抓到，2 条 CS1061/CS0117）
 * 我第一版用 `_boardHint.gameObject.activeInHierarchy` + `RectTransformUtility.WorldToScreenPoint`，
 * 两者都**不在离线桩里**，而且更重要的是：**项目里已有更好的范式**（MenuScene:1246-1255）：
 *   · 把屏幕点转到**按钮所在层级的局部坐标**再与 `rect` 比较（直接比屏幕坐标会因 CanvasScaler 缩放错位）；
 *   · **两种 y 口径都试**（`sp` 与 `Screen.height - sp.y`）——
 *     注释里写着这是"按钮点不动"的直接原因（mousePosition 的 y 与 Screen.height 不同源）。
 * 我重写时没查既有实现，等于把人家踩过的坑又踩一遍。本脚本改成同范式。
 *
 * ## 纪律
 * 先读既有实现再写新代码（本条就是没做而付出的代价）。
 *
 * 用法：node tools/fix-join-hittest.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
let src = raw;
const log = [];
const fail = (m) => { console.error('[fix-hit] ✗ ' + m); process.exit(1); };

if (src.includes('ScreenPointToLocalPointInRectangle(_boardHint.rectTransform')) {
  console.log('[fix-hit] 已修，跳过');
  process.exit(0);
}

const from = [
  '            if (clicked && _boardHint != null && _boardHint.gameObject.activeInHierarchy',
  '                && _joinKeyboard == null)',
  '            {',
  '                var rt = _boardHint.rectTransform;',
  '                var c = RectTransformUtility.WorldToScreenPoint(null, rt.position);',
  '                var half = rt.sizeDelta * 0.5f;',
  '                var d = new Vector2(screenPoint.x - c.x, screenPoint.y - c.y);',
  '                if (Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y) { StartJoinSession(); return true; }',
  '            }',
].join('\n');

const to = [
  '            if (clicked && _boardHint != null && _joinKeyboard == null)',
  '            {',
  '                // 命中判定沿用本项目既有范式（MenuScene 里按钮命中那段的注释写明了两个坑）：',
  '                // ① 必须转到**该层级的局部坐标**再与 rect 比 —— 直接比屏幕坐标会因 CanvasScaler 缩放错位；',
  '                // ② **两种 y 口径都试** —— 实测 mousePosition 的 y 与 Screen.height 不同源，单口径会全不命中。',
  '                var rt = _boardHint.rectTransform;',
  '                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, null, out var local);',
  '                var flipped = new Vector2(screenPoint.x, Screen.height - screenPoint.y);',
  '                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, flipped, null, out var localFlipped);',
  '                if (rt.rect.Contains(local) || rt.rect.Contains(localFlipped)) { StartJoinSession(); return true; }',
  '            }',
].join('\n');

const n = src.split(from).length - 1;
if (n !== 1) fail(`待修块命中 ${n} 次（应为 1）`);
src = src.replace(from, to);
log.push('  ✓ 命中判定改为 ScreenPointToLocalPointInRectangle + rect.Contains（双 y 口径）');

console.log('[fix-hit] 修正加入房间的点击判定' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
}
