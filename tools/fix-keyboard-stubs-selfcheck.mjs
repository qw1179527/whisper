#!/usr/bin/env node
/**
 * fix-keyboard-stubs-selfcheck.mjs — 收紧键盘桩脚本的自检正则
 *
 * ## 缺陷（本轮自检自己抓到的）
 * `add-keyboard-stubs.mjs` 的自检用 `/isMobilePlatform/` 匹配，把**注释里那处**也算进去 →
 * 命中 2 次 → 判红、拒绝写文件。行为是对的（拒绝半改状态），但判据太宽：注释不该参与自检。
 * 修法：自检改成匹配**声明本身**（`public static bool isMobilePlatform`）。
 *
 * 用法：node tools/fix-keyboard-stubs-selfcheck.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const F = path.join(ROOT, 'tools/add-keyboard-stubs.mjs');
let s = fs.readFileSync(F, 'utf8');
const from = '                  /isMobilePlatform/]) {';
const to = '                  /public static bool isMobilePlatform/]) {';
if (!s.includes(from)) { console.log('[fix] 锚点未命中（可能已改）'); process.exit(0); }
s = s.replace(from, to);
fs.writeFileSync(F, s, 'utf8');
console.log('[fix] 自检正则已收紧为「匹配声明本身」');
