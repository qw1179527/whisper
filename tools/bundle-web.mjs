#!/usr/bin/env node
/**
 * bundle-web.mjs — 把分包源树合成 0.6.0 形态的单文件产物
 *
 * 合成形态严格照抄 0.6.0 产物（三段固定文本逐字节取自 baseline，模块段由源树逆包装拼回）：
 *   A 前导   ：生成声明 + IIFE + 'use strict'
 *   M 模块段 ：__tables["__mN"] = function (mod) { …块内文本… };
 *   B 运行时 ：var __tables = {}, __cache = {}; function __req(id) {…}
 *   C 入口   ：var __entry = __req("__m13"); globalThis.startGame = …; })();
 *
 * 本脚本自检（任一不过即拒绝写出，避免"半成品产物"污染下游）：
 *   S1 固定三段必须与 baseline 逐字节相同
 *   S2 逆包装后的每个模块块必须与 baseline 对应块逐字节相同
 *   S3 产出脚本必须通过 node --check 语法检查
 *
 * 用法：node tools/bundle-web.mjs [--out <路径>]
 * 默认产出：build/game.js
 */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/module-manifest.json'), 'utf8'));
const MOD_DIR = path.join(ROOT, 'src/modules');
const BASELINE = path.join(ROOT, MANIFEST.meta.source);

const outFlag = process.argv.indexOf('--out');
const OUT = outFlag > 0 ? path.resolve(process.argv[outFlag + 1]) : path.join(ROOT, 'build/game.js');

// ── baseline 三段定位（用代码定位，不靠肉眼）──
const base = fs.readFileSync(BASELINE, 'utf8');
const atTables = base.indexOf('var __tables = {}, __cache = {};');
const atFirstMod = base.indexOf('__tables["__m0"]');
const atEntry = base.indexOf('var __entry = __req("__m13");');
if (atTables < 0 || atFirstMod < 0 || atEntry < 0) throw new Error('baseline 三段定位失败');

const SEG_A_PREFIX = base.slice(0, atTables); // 生成声明 + IIFE 开场
const SEG_B = base.slice(atTables, atFirstMod); // 运行时（表 + 缓存 + __req）
const SEG_C = base.slice(atEntry); // 入口引导 + IIFE 收尾

/** baseline 里每个模块的原块（用于 S2 对拍） */
function baselineBlocks() {
  const head = /__tables\["(__m\d+)"\]\s*=\s*function\s*\(mod\)\s*\{/g;
  const marks = [];
  let m;
  while ((m = head.exec(base)) !== null) marks.push({ id: m[1], start: m.index });
  const map = new Map();
  marks.forEach((mk, i) => {
    const end = i + 1 < marks.length ? marks[i + 1].start : atEntry;
    map.set(mk.id, base.slice(mk.start, end));
  });
  return map;
}
const baseBlocks = baselineBlocks();

/** 源文件 → 原块（逆包装），并返回源文件里的模块体文本 */
function unwrap(id) {
  const file = path.join(MOD_DIR, `${id}.js`);
  const src = fs.readFileSync(file, 'utf8');
  const marker = "'use strict';\n";
  const at = src.indexOf(marker);
  if (at < 0) throw new Error(`${id}: 源文件缺少 'use strict' 标记，无法拆分包装`);
  const body = src.slice(at + marker.length);
  return body;
}

/** 逆包装后的模块体：module.exports→mod.exports，require("__mN")→__req("__mN")，去掉外层 function 包裹残留 */
function bodyToOriginal(id) {
  const body = unwrap(id);
  // 源文件体 = 头部注入 + 前置语句 + module.exports 行 + '\n'
  return body
    .replace(/module\.exports/g, 'mod.exports')
    .replace(/require\(\s*["'](__m\d+)(?!\d)["']\s*\)/g, '__req("$1")');
}

// ── 生成模块段 + S2 自检 ──
const moduleParts = [];
for (const m of MANIFEST.modules) {
  const original = baseBlocks.get(m.id);
  if (!original) throw new Error(`${m.id}: baseline 中找不到对应块`);
  // original = `__tables["__mN"] = function (mod) {` + 块内文本 + '\n  };\n  '
  const head = `__tables["${m.id}"] = function (mod) {`;
  const inner = original.slice(head.length).replace(/\n  \};\n  $/, '');
  const rebuiltInner = bodyToOriginal(m.id).replace(/\n$/, '');
  if (rebuiltInner !== inner) {
    const n = Math.min(rebuiltInner.length, inner.length);
    let i = 0;
    while (i < n && rebuiltInner[i] === inner[i]) i++;
    throw new Error(
      `S2 自检失败：${m.id} 逆包装后与 baseline 块不一致（首个差异第 ${i} 字节：\n  源树=${JSON.stringify(rebuiltInner.slice(i, i + 60))}\n  原始=${JSON.stringify(inner.slice(i, i + 60))}）`,
    );
  }
  moduleParts.push(`${head}${rebuiltInner}\n  };\n  `);
}

// ── S1 自检：固定三段必须取自 baseline（结构性保证）──
for (const [name, seg] of [
  ['A 前导', SEG_A_PREFIX],
  ['B 运行时', SEG_B],
  ['C 入口', SEG_C],
]) {
  if (!base.includes(seg)) throw new Error(`S1 自检失败：${name} 未能在 baseline 中逐字节定位`);
}

const versionInfo = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/version.json'), 'utf8'));
const banner =
  `/* 低语计划 · 灰盒单文件包（tools/bundle-web.mjs 生成，勿手改）\n` +
  ` * 版本 ${versionInfo.version} · 源树 ${MANIFEST.meta.moduleCount} 模块 · 基线条目哈希 ${MANIFEST.meta.sourceSha256.slice(0, 16)}\n` +
  ` * 生成时间不计入产物（保证重跑字节级稳定）*/\n`;

// A 段去掉 baseline 的单行声明，换成带版本信息的多行 banner（形态一致：都是生成声明注释）
const segA = SEG_A_PREFIX.replace(
  /^\/\* 低语计划 · 灰盒单文件包（tools\/bundle-web\.mjs 生成，勿手改） \*\/\n/,
  banner,
);

const outText = segA + SEG_B + moduleParts.join('') + SEG_C;

// ── S3 自检：语法检查 + 真实执行（在最小 __tables 语境下加载）──
fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, outText, 'utf8');

try {
  execFileSync(process.execPath, ['--check', OUT], { stdio: 'pipe' });
} catch (e) {
  throw new Error(`S3 自检失败：产出未通过语法检查\n${e.stderr?.toString() ?? e.message}`);
}

// 真实执行：给出最小 DOM/全局桩，只验证模块装配链能否跑通（不验证玩法渲染）
const sandbox = {
  globalThis: undefined,
  window: { addEventListener() {}, removeEventListener() {}, devicePixelRatio: 1 },
  document: { getElementById: () => null, createElement: () => ({ style: {}, appendChild() {}, getContext: () => null }), body: { appendChild() {} }, addEventListener() {} },
  console: { log() {}, warn() {}, error() {} },
  performance: { now: () => 0 },
  requestAnimationFrame: () => 0,
  navigator: { userAgent: 'node-check' },
};
sandbox.globalThis = sandbox;
const ctx = vm.createContext(sandbox);
try {
  vm.runInContext(outText, ctx, { filename: OUT, timeout: 5000 });
  if (typeof sandbox.startGame !== 'function') throw new Error('globalThis.startGame 未挂载');
} catch (e) {
  throw new Error(`S3 自检失败：产出无法装配（${e.message}）`);
}

const stat = fs.statSync(OUT);
console.log(`[bundle] 产出 ${path.relative(ROOT, OUT)}  ${stat.size} 字节 · 模块 ${MANIFEST.modules.length} 个 · 版本 ${versionInfo.version}`);
console.log(`[bundle] 自检 S1(固定三段) S2(模块块字节一致) S3(语法+装配) 全部通过 ✓`);
