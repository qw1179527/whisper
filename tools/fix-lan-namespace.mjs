#!/usr/bin/env node
/**
 * fix-lan-namespace.mjs — 把本轮两个新文件归入 Whisper.Net.Direct
 *
 * ## 缺陷（真 Unity 编译抓到，语法预检抓不到）
 *     Assets\Scripts\Net\LanSession.cs(51,28): error CS0103: The name 'RoomCode' does not exist in the current context
 * 根因：`RoomCode` 与 `UdpV6NetService` 都在 **`Whisper.Net.Direct`** 子命名空间里
 * （`RoomCode.cs:5` `namespace Whisper.Net.Direct`），而我新建的 `LanAddress` / `LanSession`
 * 写成了 `Whisper.Net`，于是 `RoomCode` 不在作用域内。
 *
 * ## 修法
 * 两个新文件都归入 `Whisper.Net.Direct` —— 这既修编译错，也在架构上更正确：
 * 它们（取本机地址 / 装配直连会话）本来就只服务"零信令 IPv6/UDP 直连"这一层。
 * 同时修正测试文件的 using。
 *
 * 用法：node tools/fix-lan-namespace.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const edits = [
  ['unity/Assets/Scripts/Net/LanAddress.cs', 'namespace Whisper.Net\n', 'namespace Whisper.Net.Direct\n'],
  ['unity/Assets/Scripts/Net/LanSession.cs', 'namespace Whisper.Net\n', 'namespace Whisper.Net.Direct\n'],
  ['unity/Assets/Scripts/Tests/EditMode/LanSessionTests.cs',
    'using Whisper.Net;\n', 'using Whisper.Net;\nusing Whisper.Net.Direct;\n'],
];

let n = 0;
for (const [rel, from, to] of edits) {
  const p = path.join(ROOT, rel);
  const s = fs.readFileSync(p, 'utf8');
  if (s.includes(to.trim()) && to.includes('\n')) {
    // 幂等：已经改过就跳过
    if (rel.endsWith('LanAddress.cs') || rel.endsWith('LanSession.cs')) {
      if (s.includes('namespace Whisper.Net.Direct')) { console.log(`  · 已修正，跳过 ${rel}`); continue; }
    }
    if (rel.endsWith('LanSessionTests.cs') && s.includes('using Whisper.Net.Direct;')) {
      console.log(`  · 已修正，跳过 ${rel}`); continue;
    }
  }
  const c = s.split(from).length - 1;
  if (c !== 1) { console.error(`[fix-ns] ✗ ${rel}：锚点命中 ${c} 次（应为 1）`); process.exit(1); }
  if (!checkOnly) fs.writeFileSync(p, s.replace(from, to), 'utf8');
  console.log(`  ✓ ${rel}`);
  n++;
}
console.log(`[fix-ns] 修正 ${n} 个文件（Whisper.Net → Whisper.Net.Direct）`);
