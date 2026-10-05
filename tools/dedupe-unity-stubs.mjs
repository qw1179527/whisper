#!/usr/bin/env node
/**
 * dedupe-unity-stubs.mjs — 合并 UnityStubs.cs 里重复定义的两套 UnityEngine 桩
 *
 * ## 缺陷（草稿门禁抓出）
 * `bash tools/unity-syntax-check.sh` 报 **56 条真实错误**，其中 12 条是类型重复：
 *   `CS0101 Rect/Texture/Texture2D/RenderTexture/TextureFormat/FilterMode 已包含定义`
 *   `CS0111 成员已定义`（构造器/方法重复）
 * 成因：历史上 `tools/fix-stubs-*.mjs` 那批脚本是**往文件末尾追加**一整套更完整的桩，
 * 而没有删掉文件里 249–275 行的旧版贫桩 → 同名类型出现两次。
 *
 * ## 为什么是「合并」而不是「删一套」
 * 两套**各有独有成员**，删谁都会让别的代码编译不过：
 *   · 旧版独有：`TextureWrapMode`、`Texture2D.SetPixels32`、`RenderTexture.GetTemporary/ReleaseTemporary`、`TextureFormat.Alpha8/RGB24`
 *   · 新版独有：`Graphics.Blit`、`DepthTextureMode`、`RenderTextureFormat`、`Texture2D.ReadPixels(Rect,…)/Apply()/GetPixel`
 * 所以：**删旧版的 6 个冲突类型，把旧版独有成员并入新版**（并集，不丢任何入口）。
 *
 * ## 纪律
 * 每一步都断言「锚点唯一 + 目标不存在」，任何一条不中即停止不写文件（避免半改状态）。
 * CRLF 保持：读入后按 /\r?\n/ 切、写回用原文件的分隔符。
 *
 * 用法：node tools/dedupe-unity-stubs.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const eol = raw.includes('\r\n') ? '\r\n' : '\n';
if (eol !== '\r\n') console.log('[stubs] 注意：文件不是 CRLF，按 LF 处理');
let lines = raw.split(/\r?\n/);

const fail = (m) => { console.error('[stubs] ✗ ' + m); process.exit(1); };
const log = [];

/** 找到「以 startRx 命中的行」起到「同缩进的闭合大括号」为止的区间 [i, j]（含） */
function blockRange(startRx, from = 0) {
  const i = lines.findIndex((l, idx) => idx >= from && startRx.test(l));
  if (i < 0) return null;
  const indent = lines[i].match(/^\s*/)[0];
  for (let j = i + 1; j < lines.length; j++) {
    if (lines[j] === indent + '}') return [i, j];
  }
  return null;
}

// ── 0. 先确认现在是「重复」状态，避免重复执行 ──────────────────────────
const dupMark = (t) => lines.filter((l) => new RegExp('^\\s*(public|internal)\\s+(class|struct|enum)\\s+' + t + '\\b').test(l)).length;
for (const t of ['Rect', 'Texture', 'Texture2D', 'RenderTexture', 'TextureFormat', 'FilterMode']) {
  const n = dupMark(t);
  if (n !== 2) fail(`${t} 定义数=${n}（期望 2）。若为 1 说明已合并过，若 >2 需人工看`);
}
log.push('  前提核对：6 个类型各定义 2 次（确认处于待合并状态）');

// ── 1. 删掉旧版 6 个冲突定义（旧块：Texture / TextureFormat / FilterMode / Texture2D / RenderTexture；Rect 单独）──
// 旧块从 "public class Texture : Object { }" 到旧 RenderTexture 的闭合括号
const oldStart = lines.findIndex((l) => /^\s*public class Texture : Object \{ \}\s*$/.test(l));
if (oldStart < 0) fail('未找到旧版 Texture 定义行');
const oldRt = blockRange(/^\s*public class RenderTexture : Texture\s*$/, oldStart);
if (!oldRt) fail('未找到旧版 RenderTexture 块');
const oldEnd = oldRt[1];
// 校验这一段就是要删的东西
const seg = lines.slice(oldStart, oldEnd + 1).join('\n');
for (const t of ['Texture', 'TextureFormat', 'FilterMode', 'Texture2D', 'RenderTexture']) {
  if (!new RegExp('(class|enum)\\s+' + t + '\\b').test(seg)) fail(`待删段里没有 ${t}`);
}
if (/TextureWrapMode/.test(seg) === false) fail('待删段里竟没有 TextureWrapMode —— 它会一起被删，需改脚本');
// ⚠ TextureWrapMode 必须在删完后仍然存在 → 先把它从待删段里摘出来，稍后并入新版
const twmLine = lines.slice(oldStart, oldEnd + 1).find((l) => /^\s*public enum TextureWrapMode\b/.test(l));
if (!twmLine) fail('未找到 TextureWrapMode 定义行');
const removed = lines.splice(oldStart, oldEnd - oldStart + 1);
log.push(`  ① 删除旧版 6 类型（行 ${oldStart + 1}–${oldEnd + 1}，共 ${removed.length} 行）；摘出 TextureWrapMode 待并入`);

// 旧版 Rect（struct，另有异于新版的坐标字段名）——单独删
const oldRect = blockRange(/^\s*public struct Rect\s*$/, 0);
if (!oldRect) fail('未找到旧版 Rect 块');
// 确认它就是我们想删的那个（含 Contains，属旧版独有 → 迁移到新版）
const rectSeg = lines.slice(oldRect[0], oldRect[1] + 1).join('\n');
if (!/Contains\(Vector2/.test(rectSeg)) fail('旧版 Rect 里没有 Contains —— 与预期不符，停止');
lines.splice(oldRect[0], oldRect[1] - oldRect[0] + 1);
log.push(`  ② 删除旧版 Rect（行 ${oldRect[0] + 1} 起）；Contains 待并入新版`);

// ── 2. 把旧版独有成员并入新版（尾部那套）──────────────────────────────
// 2a. 新版 RenderTexture：加 GetTemporary / ReleaseTemporary
{
  const r = blockRange(/^\s*public class RenderTexture : Texture\s*$/);
  if (!r) fail('未找到新版 RenderTexture 块');
  const anchor = lines.findIndex((l, i) => i > r[0] && i < r[1] && /public static RenderTexture active \{ get; set; \}/.test(l));
  if (anchor < 0) fail('新版 RenderTexture 里未找到 active 行');
  if (lines.slice(r[0], r[1]).some((l) => /GetTemporary/.test(l))) fail('GetTemporary 已存在，勿重复并入');
  lines.splice(anchor + 1, 0,
    '        /// <summary>临时 RT（出处 ScriptReference/RenderTexture.GetTemporary）。多级后处理缓冲用它。</summary>',
    '        public static RenderTexture GetTemporary(int w, int h, int depth) => null;',
    '        public static void ReleaseTemporary(RenderTexture rt) { }');
  log.push('  ③ 并入 RenderTexture.GetTemporary / ReleaseTemporary');
}
// 2b. 新版 Texture2D：加 SetPixels32 / wrapMode / TextureWrapMode（枚举）
{
  const r = blockRange(/^\s*public class Texture2D : Texture\s*$/);
  if (!r) fail('未找到新版 Texture2D 块');
  if (lines.slice(r[0], r[1]).some((l) => /SetPixels32/.test(l))) fail('SetPixels32 已存在，勿重复并入');
  const anchor = lines.findIndex((l, i) => i > r[0] && i < r[1] && /public void Apply\(\)/.test(l));
  if (anchor < 0) fail('新版 Texture2D 里未找到 Apply() 行');
  lines.splice(anchor + 1, 0,
    '        public void SetPixels32(Color32[] colors) { }',
    '        public TextureWrapMode wrapMode { get; set; }');
  // 枚举：插在 Texture2D 块之后
  const after = blockRange(/^\s*public class Texture2D : Texture\s*$/);
  const insertAt = (after ? after[1] : r[1]) + 1;
  if (lines.some((l) => /^\s*public enum TextureWrapMode\b/.test(l))) fail('TextureWrapMode 已存在，勿重复并入');
  lines.splice(insertAt, 0, '', '    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2 }');
  log.push('  ④ 并入 Texture2D.SetPixels32 / wrapMode / TextureWrapMode');
}
// 2c. 新版 TextureFormat：补 Alpha8 / RGB24（旧版有、新版缺）
{
  const i = lines.findIndex((l) => /^\s*public enum TextureFormat\b/.test(l));
  if (i < 0) fail('未找到新版 TextureFormat 枚举');
  if (/Alpha8/.test(lines[i])) fail('Alpha8 已存在，勿重复并入');
  lines[i] = '    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, RFloat = 6 }';
  log.push('  ⑤ 并入 TextureFormat.Alpha8 / RGB24');
}
// 2d. 新版 Rect：补 Contains（旧版有、新版缺）
{
  const r = blockRange(/^\s*public struct Rect\s*$/);
  if (!r) fail('未找到新版 Rect 块');
  if (lines.slice(r[0], r[1]).some((l) => /Contains\(Vector2/.test(l))) fail('Rect.Contains 已存在，勿重复并入');
  const ctor = lines.findIndex((l, i) => i > r[0] && i < r[1] && /public Rect\(/.test(l));
  if (ctor < 0) fail('新版 Rect 里未找到构造器');
  lines.splice(ctor + 1, 0,
    '        /// <summary>点是否在矩形内（出处 ScriptReference/Rect.Contains）。</summary>',
    '        public bool Contains(Vector2 p) => p.x >= x && p.x <= x + width && p.y >= y && p.y <= y + height;');
  log.push('  ⑥ 并入 Rect.Contains');
}

// ── 3. 收尾自检：每个类型必须恰好 1 个定义 ─────────────────────────────
for (const t of ['Rect', 'Texture', 'Texture2D', 'RenderTexture', 'TextureFormat', 'FilterMode', 'TextureWrapMode']) {
  const n = lines.filter((l) => new RegExp('^\\s*(public|internal)\\s+(class|struct|enum)\\s+' + t + '\\b').test(l)).length;
  if (n !== 1) fail(`合并后 ${t} 定义数=${n}（应为 1）`);
}
log.push('  ⑦ 自检通过：7 个类型各恰好 1 个定义');

console.log('[stubs] UnityStubs.cs 去重合并' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const out = lines.join(eol);
  const bak = FILE + '.bak-dedupe';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, out, 'utf8');
  console.log(`  已写回：${raw.length} → ${out.length} 字节（备份 ${path.basename(bak)}）`);
}
