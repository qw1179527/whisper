#!/usr/bin/env node
/**
 * add-recttransform-rect-stub.mjs — 补 `RectTransform.rect` 桩
 *
 * ## 为什么
 * 加入房间的点击命中判定沿用项目既有范式（`rect.Contains(local)`），
 * 而离线桩里 `RectTransform` **没有 `rect` 属性** → 语法门禁 CS1061。
 * 官方的 `RectTransform.rect` 是"该矩形在本地坐标下的矩形"（出处 ScriptReference/RectTransform-rect），
 * 正是命中判定要用的那个。
 *
 * ## 纪律
 * 幂等；宿主类可能是单行类（脚本已处理）。
 *
 * 用法：node tools/add-recttransform-rect-stub.mjs [--check]
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
const fail = (m) => { console.error('[rt-rect] ✗ ' + m); process.exit(1); };

if (lines.some((l) => /public Rect rect\b/.test(l))) { console.log('[rt-rect] 已存在，跳过'); process.exit(0); }

const i = lines.findIndex((l) => /^\s*public class RectTransform\b/.test(l));
if (i < 0) fail('未找到 RectTransform 类');
const indent = lines[i].match(/^\s*/)[0];
let end = -1;
for (let j = i + 1; j < lines.length; j++) if (lines[j] === indent + '}') { end = j; break; }
if (end < 0) fail('未找到 RectTransform 类闭合括号');

lines.splice(end, 0,
  indent + '    /// <summary>本地坐标下的矩形（出处 ScriptReference/RectTransform-rect）。UI 命中判定用它。</summary>',
  indent + '    public Rect rect => new Rect(0f, 0f, 100f, 40f);',
  indent + '    /// <summary>矩形尺寸（出处 ScriptReference/RectTransform-sizeDelta）。</summary>',
  indent + '    public Vector2 sizeDelta { get; set; }');

const out = lines.join(eol);
console.log('[rt-rect] 补 RectTransform.rect / sizeDelta' + (checkOnly ? '（--check：不写文件）' : ''));
if (!checkOnly) {
  fs.writeFileSync(FILE, out, 'utf8');
  console.log(`  已写回：${raw.length} → ${out.length} 字节`);
}
