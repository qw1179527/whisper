#!/usr/bin/env node
/**
 * fix-r8-compile-errors.mjs — 修真编译抓到的 3 个错（语法预检放过了它们）
 *
 * ## 缺陷（真 Unity 编译报的，`unity-syntax-check.sh` 判绿但没用）
 * ```
 * MenuScene.cs(667,13): error CS0103: The name 'title' does not exist in the current context
 * MenuScene.cs(1145,41): error CS0103: The name 'RoomReachJudge' does not exist in the current context
 * MenuScene.cs(1145,65): error CS0103: The name 'LanSession' does not exist in the current context
 * ```
 * 成因：
 * 1. 我删掉了 `Text title = SceneMaterials.Label(..., "MenuTitle", ...)` 这条**声明**，
 *    但紧接着的 `title.color = ...` 还是**用它的语句** → 名字不存在（经典"只删声明不删用途"）；
 * 2. `MenuScene` 里用了 `RoomReachJudge`（`Net.Direct`）与 `LanSession`（`Net.Direct`），
 *    但文件里没有对应的 `using Whisper.Net.Direct;`。
 *
 * ## 教训（写进注释，避免再犯）
 * **删声明时必须同时处理它的使用点**；**跨模块用类型时先确认 using**。
 * 这两类错 `unity-syntax-check.sh` 都抓不到（它不做跨文件/名字解析），只有真 Unity 编译会报 ——
 * 这正是交接 §0.4 那条纪律存在的理由。
 *
 * 用法：node tools/fix-r8-compile-errors.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const checkOnly = process.argv.includes('--check');

let src = fs.readFileSync(FILE, 'utf8');
const log = [];
const fail = (m) => { console.error('[r8-fix] ✗ ' + m); process.exit(1); };

// ── ① 删掉遗留的 title.color 使用点 ───────────────────────────────────
{
  const anchor = '            title.color = new Color(0.86f, 0.88f, 0.94f);';
  const n = src.split(anchor).length - 1;
  if (n === 0) log.push('  · title.color 已无残留，跳过');
  else if (n > 1) fail(`title.color 命中 ${n} 次`);
  else {
    src = src.replace(anchor, '            // （原 `title.color = …` 已随 MenuTitle 一起去掉：声明删了，使用点也必须删。）');
    log.push('  ✓ 删除遗留的 title.color 使用点');
  }
}

// ── ② 补 using Whisper.Net.Direct ─────────────────────────────────────
{
  if (src.includes('using Whisper.Net.Direct;')) log.push('  · using Whisper.Net.Direct 已存在，跳过');
  else {
    // 锚在既有 using 块的最后一条（Whisper.Backend 是该项目运行时的最后一条）
    const anchor = 'using Whisper.Backend;';
    const n = src.split(anchor).length - 1;
    if (n !== 1) fail(`using 锚点命中 ${n} 次（应为 1）`);
    src = src.replace(anchor,
      anchor + '\nusing Whisper.Net.Direct;   // RoomReachJudge / LanSession（零信令直连层的可达范围与会话编排）');
    log.push('  ✓ 补 using Whisper.Net.Direct');
  }
}

if (!checkOnly) fs.writeFileSync(FILE, src, 'utf8');
console.log('[r8-fix] 修真编译错误' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) console.log(`  已写回：${src.length} 字节`);
