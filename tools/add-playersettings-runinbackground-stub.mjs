#!/usr/bin/env node
/**
 * add-playersettings-runinbackground-stub.mjs — 补 `PlayerSettings.runInBackground` 桩（v2）
 *
 * ## v1 的缺陷（我自己踩的）
 * 幂等判断写成 `raw.includes('        public static bool runInBackground { get; set; }')`，
 * 而 **`Application` 里那条声明也是 8 空格缩进** → 误判"已存在" → 什么都没做，
 * 于是 `CS0117 PlayerSettings 未包含 runInBackground` 一直红。这是"用文本片段判断结构"的典型错。
 *
 * ## v2 的判据
 * **以宿主类为锚点**：先定位 `public static class PlayerSettings`，再只在它的块内判断/插入。
 *
 * 用法：node tools/add-playersettings-runinbackground-stub.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const lines = raw.split(/\r?\n/);

// ① 定位 PlayerSettings 类块
const start = lines.findIndex((l) => /^\s*public static class PlayerSettings\b/.test(l));
if (start < 0) { console.error('[ps-rib] ✗ 未找到 PlayerSettings 类'); process.exit(1); }
const indent = lines[start].match(/^\s*/)[0];
let end = -1;
for (let j = start + 1; j < lines.length; j++) if (lines[j] === indent + '}') { end = j; break; }
if (end < 0) { console.error('[ps-rib] ✗ 未找到 PlayerSettings 类闭合括号'); process.exit(1); }

// ② 只在该块【内部】判断是否已存在（v1 的错就在这里）
const body = lines.slice(start, end + 1);
if (body.some((l) => /public static bool runInBackground\b/.test(l))) {
  console.log('[ps-rib] PlayerSettings 块内已有 runInBackground，跳过');
  process.exit(0);
}

// ③ 插在块尾
const ins = [
  indent + '    /// <summary>失焦时是否继续渲染（出处 ScriptReference/PlayerSettings-runInBackground）。',
  indent + '    /// 移动端默认 false → 失焦即停渲染并释放 Surface；本工程相机/UI 全是代码建的，不会自建回来。</summary>',
  indent + '    public static bool runInBackground { get; set; }',
];
lines.splice(end, 0, ...ins);
if (!checkOnly) {
  fs.writeFileSync(FILE, lines.join('\n'), 'utf8');
  console.log(`[ps-rib] 已在 PlayerSettings 块内补 runInBackground（插在第 ${end + 1} 行前）`);
}
