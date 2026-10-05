#!/usr/bin/env node
/**
 * add-menu-using-direct.mjs — 给 MenuScene 补 `using Whisper.Net.Direct;`
 *
 * ## 缺陷（真 Unity 编译报的）
 *     MenuScene.cs(1145): error CS0103: The name 'RoomReachJudge' does not exist in the current context
 *     MenuScene.cs(1145): error CS0103: The name 'LanSession' does not exist in the current context
 * 两者都在 `Whisper.Net.Direct`（零信令直连层），而 `MenuScene.cs` 的 using 块只有四行
 * （System.Collections.Generic / UnityEngine / UnityEngine.UI / Whisper.Core / Whisper.Core.Contracts）。
 *
 * ## 上一次为什么没成
 * 我把锚点写成 `using Whisper.Backend;` —— 那是我**凭印象**以为 MenuScene 引过它。
 * 实际文件里没有这条 → 锚点命中 0 次 → 脚本正确停下（没写坏文件）。
 * **教训：锚点必须来自"读到的真实内容"，不是"记得的内容"。**
 *
 * 用法：node tools/add-menu-using-direct.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('using Whisper.Net.Direct;')) { console.log('[using] 已存在，跳过'); process.exit(0); }

// 锚点：真实存在的最后一条 using（读文件得到，不是记忆）
const anchor = 'using Whisper.Core.Contracts;       // MatchPhase';
const n = raw.split(anchor).length - 1;
if (n !== 1) { console.error(`[using] ✗ 锚点命中 ${n} 次（应为 1）`); process.exit(1); }

const add = anchor + '\n' +
  'using Whisper.Net.Direct;           // LanSession / RoomReachJudge（零信令直连：可达范围与会话编排）';
const out = raw.replace(anchor, add);
if (!checkOnly) fs.writeFileSync(FILE, out, 'utf8');
console.log(`[using] 已补 using Whisper.Net.Direct（${raw.length} → ${out.length} 字节）`);
