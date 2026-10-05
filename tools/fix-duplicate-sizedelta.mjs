#!/usr/bin/env node
/**
 * fix-duplicate-sizedelta.mjs — 删掉我重复添加的 `RectTransform.sizeDelta` 桩
 *
 * ## 缺陷
 * `add-recttransform-rect-stub.mjs` 里我同时加了 `rect` 与 `sizeDelta`，
 * 但**桩里本来就有 `sizeDelta`** → `CS0102 已包含定义` + 6 处 `CS0229 二义性`。
 * 这是本轮第二次犯"没先查是否已存在"（第一次是重复类型、这次是重复成员）。
 * 修法：只保留 `rect`，删掉我新加的那条 `sizeDelta`（连注释）。
 *
 * 用法：node tools/fix-duplicate-sizedelta.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const lines = raw.split(/\r?\n/);
const decl = 'public Vector2 sizeDelta { get; set; }';
const idx = lines.map((l, i) => (l.includes(decl) ? i : -1)).filter((i) => i >= 0);
console.log(`sizeDelta 声明数：${idx.length}`);
if (idx.length <= 1) { console.log('[fix-sd] 无需处理（已是单条）'); process.exit(0); }

// 删掉最后一条（我新加的），并连同它上面的中文注释一起删
const last = idx[idx.length - 1];
let start = last;
if (last - 1 >= 0 && lines[last - 1].includes('sizeDelta')) start = last - 1;
const removed = lines.splice(start, last - start + 1);
console.log(`[fix-sd] 删除 ${removed.length} 行：`);
for (const l of removed) console.log('   ' + l.trim());
if (!checkOnly) {
  fs.writeFileSync(FILE, lines.join('\n'), 'utf8');
  console.log('[fix-sd] 已写回');
}
