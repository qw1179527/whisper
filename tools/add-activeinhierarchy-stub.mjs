#!/usr/bin/env node
/**
 * add-activeinhierarchy-stub.mjs — 补 `GameObject.activeInHierarchy` 桩
 *
 * ## 为什么
 * `retire-legacy-ui.mjs` 新增的 `TryHitWorldLabel` 用 `label.gameObject.activeInHierarchy`
 * 做"这个牌匾当前是否显示"的前置判断（官方出处 ScriptReference/GameObject-activeInHierarchy），
 * 而离线桩里**只有 `activeSelf`** → 语法门禁 CS1061。
 *
 * ## 上次为什么没成
 * 我用 `node -e` + 正则 `/public class GameObject[^\n]*\n\{/` 去匹配，实际声明是
 * `public class GameObject : Object`（**基类在同行**）而左大括号在**下一行** → 正则不中。
 * 又一次"锚点凭猜" —— 这次先 `grep` 拿到真实行号与内容再写。
 *
 * 用法：node tools/add-activeinhierarchy-stub.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const lines = raw.split(/\r?\n/);

const start = lines.findIndex((l) => /^\s*public class GameObject\b/.test(l));
if (start < 0) { console.error('[aih] ✗ 未找到 GameObject 类'); process.exit(1); }
const indent = lines[start].match(/^\s*/)[0];
let end = -1;
for (let j = start + 1; j < lines.length; j++) if (lines[j] === indent + '}') { end = j; break; }
if (end < 0) { console.error('[aih] ✗ 未找到 GameObject 类闭合括号'); process.exit(1); }

if (lines.slice(start, end).some((l) => /activeInHierarchy/.test(l))) {
  console.log('[aih] 已存在，跳过');
  process.exit(0);
}

// 插在 activeSelf 之后（同族语义挨着放，便于以后一起读）
const anchor = lines.findIndex((l, i) => i > start && i < end && /public bool activeSelf\b/.test(l));
if (anchor < 0) { console.error('[aih] ✗ 未找到 activeSelf 锚点'); process.exit(1); }
lines.splice(anchor + 1, 0,
  '        /// <summary>本对象与父链是否都激活（出处 ScriptReference/GameObject-activeInHierarchy）。UI 命中判定先看它。</summary>',
  '        public bool activeInHierarchy => true;');

if (!checkOnly) fs.writeFileSync(FILE, lines.join('\n'), 'utf8');
console.log(`[aih] 已补 GameObject.activeInHierarchy（插在第 ${anchor + 2} 行）`);
