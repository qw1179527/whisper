#!/usr/bin/env node
/**
 * add-keyboard-stubs.mjs — 补移动端系统键盘桩（加入房间要玩家输入 12/31 位房间码）
 *
 * ## 为什么必须补
 * 「加入房间」需要玩家输入房间码，移动端要唤起**系统键盘**（`TouchScreenKeyboard`）。
 * 实测：离线桩 `native/unity-stubs/UnityStubs.cs` 里**没有这个类型**（grep 无命中），
 * 直接写 UI 会让 `unity-syntax-check.sh` 先红 —— 而语法门禁是"改动必须过"的第一道闸。
 *
 * ## 补什么（每条都带官方出处）
 * | 入口 | 用处 | 官方出处 |
 * |---|---|---|
 * | `TouchScreenKeyboard` | 唤起系统键盘读玩家输入 | ScriptReference/TouchScreenKeyboard |
 * | `TouchScreenKeyboardType` | 键盘类型（房间码要 ASCII 大写，不用数字键盘） | ScriptReference/TouchScreenKeyboardType |
 * | `TouchScreenKeyboard.Open(...)` | 打开键盘 | ScriptReference/TouchScreenKeyboard.Open |
 * | `Application.isMobilePlatform` | 真机才弹键盘（编辑器/CI 不弹，避免卡住自动化） | ScriptReference/Application-isMobilePlatform |
 *
 * ## 纪律
 * · 幂等：已存在即跳过（可反复跑）；
 * · 宿主类可能是**单行类**（`public static class Screen { ... }`），故复用"单行类先展开"的写法；
 * · 锚点找不到就停下不写文件。
 *
 * 用法：node tools/add-keyboard-stubs.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const eol = raw.includes('\r\n') ? '\r\n' : '\n';
const lines = raw.split(/\r?\n/);
const log = [];
const fail = (m) => { console.error('[kbd] ✗ ' + m); process.exit(1); };
const has = (rx) => lines.some((l) => rx.test(l));

if (has(/^\s*public class TouchScreenKeyboard\b/) && has(/^\s*public enum TouchScreenKeyboardType\b/)) {
  console.log('[kbd] 已存在，跳过（幂等）');
  process.exit(0);
}

/** 定位类块 [start,end]；单行类返回 [i,i,true] */
function classRange(rx) {
  const i = lines.findIndex((l) => rx.test(l));
  if (i < 0) return null;
  if (lines[i].includes('{') && lines[i].trimEnd().endsWith('}')) return [i, i, true];
  const indent = lines[i].match(/^\s*/)[0];
  for (let j = i + 1; j < lines.length; j++) if (lines[j] === indent + '}') return [i, j, false];
  return null;
}

// ── ① Application.isMobilePlatform（宿主是单行/多行都可能）──────────────
if (!has(/isMobilePlatform/)) {
  const r = classRange(/^\s*public static class Application\b/);
  if (!r) fail('未找到 Application 类');
  const [i, , oneLine] = r;
  const indent = lines[i].match(/^\s*/)[0];
  const add = [
    indent + '    /// <summary>是否移动端平台（出处 ScriptReference/Application-isMobilePlatform）。',
    indent + '    /// 加入房间要弹系统键盘，编辑器/CI 不弹，否则自动化会被键盘卡住。</summary>',
    indent + '    public static bool isMobilePlatform => false;',
  ];
  if (oneLine) {
    const t = lines[i].trim();
    const open = t.indexOf('{');
    const body = t.slice(open + 1, t.lastIndexOf('}')).trim();
    lines.splice(i, 1, indent + t.slice(0, open).trimEnd(), indent + '{', indent + '    ' + body, ...add, indent + '}');
    log.push('  ✓ Application.isMobilePlatform（单行类已展开 + 插入）');
  } else {
    lines.splice(r[1], 0, ...add);
    log.push('  ✓ Application.isMobilePlatform（块尾插入）');
  }
}

// ── ② TouchScreenKeyboardType 枚举 ─────────────────────────────────────
if (!has(/^\s*public enum TouchScreenKeyboardType\b/)) {
  const r = classRange(/^\s*public static class Screen\b/);
  const at = r ? r[1] + 1 : lines.length - 1;
  lines.splice(at, 0, '',
    '    /// <summary>系统键盘类型（出处 ScriptReference/TouchScreenKeyboardType）。',
    '    /// 房间码是 base32 大写字母+数字，故用 ASCIICapable 而不是数字键盘。</summary>',
    '    public enum TouchScreenKeyboardType { Default = 0, ASCIICapable = 1, NumberPad = 4, EmailAddress = 5 }');
  log.push('  ✓ TouchScreenKeyboardType 枚举');
}

// ── ③ TouchScreenKeyboard ──────────────────────────────────────────────
if (!has(/^\s*public class TouchScreenKeyboard\b/)) {
  const r = classRange(/^\s*public enum TouchScreenKeyboardType\b/);
  if (!r) fail('未找到刚插入的 TouchScreenKeyboardType（用于定位插入点）');
  lines.splice(r[1] + 1, 0, '',
    '    /// <summary>移动端系统键盘（出处 ScriptReference/TouchScreenKeyboard）。',
    '    /// 加入房间时用它读玩家粘贴/输入的 12/31 位房间码 —— 自绘 32 键小键盘既丑又难点。</summary>',
    '    public class TouchScreenKeyboard',
    '    {',
    '        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType type,',
    '                                               bool autocorrect, bool multiline, bool secure,',
    '                                               bool alert, string placeholder) => new TouchScreenKeyboard();',
    '        /// <summary>键盘当前内容（玩家逐字输入/粘贴的结果）。</summary>',
    '        public string text { get; set; }',
    '        /// <summary>是否仍在输入中（关闭后置 false，UI 据此收起）。</summary>',
    '        public bool active => false;',
    '        /// <summary>是否被玩家取消/隐藏。</summary>',
    '        public bool wasCanceled => false;',
    '        /// <summary>是否已被系统关闭（贴到屏幕外等）。</summary>',
    '        public static bool visible => false;',
    '        public void SetText(string t) { text = t; }',
    '    }');
  log.push('  ✓ TouchScreenKeyboard（含 Open/text/active/wasCanceled/visible）');
}

// ── ④ 自检 ─────────────────────────────────────────────────────────────
for (const rx of [/^\s*public class TouchScreenKeyboard\b/, /^\s*public enum TouchScreenKeyboardType\b/,
                  /public static bool isMobilePlatform/]) {
  const n = lines.filter((l) => rx.test(l)).length;
  if (n !== 1) fail(`自检：${rx} 命中 ${n} 次（应为 1）`);
}
log.push('  ✓ 自检通过');

console.log('[kbd] 补移动端系统键盘桩' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const out = lines.join(eol);
  fs.writeFileSync(FILE, out, 'utf8');
  console.log(`  已写回：${raw.length} → ${out.length} 字节`);
}
