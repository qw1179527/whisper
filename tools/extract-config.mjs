#!/usr/bin/env node
/**
 * extract-config.mjs — 把 __m0 的配置字面量分离为唯一数值真相源 data/config.json
 *
 * 为什么必须"真实执行"而不是文本比对：配置表是对象字面量（含表达式与注释），
 * 文本相似不等于值相同。本脚本用 vm 在隔离语境里跑出真实对象，再与"原产物里
 * __m0 工厂跑出的 __CFG"逐键深比对 —— 键集、值、类型三者任一不同即失败。
 *
 * 用法：node tools/extract-config.mjs
 * 产出：data/config.json · 控制台深比对报告
 */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/module-manifest.json'), 'utf8'));
const SRC = path.join(ROOT, MANIFEST.meta.source);
const code = fs.readFileSync(SRC, 'utf8');

/** 取出 `var __CFG = <字面量>;` 的完整字面量文本（花括号配平，跳过字符串与注释） */
function grabLiteral(src, varName) {
  const at = src.indexOf(`var ${varName} = `);
  if (at < 0) throw new Error(`找不到 var ${varName} = …`);
  const start = src.indexOf('{', at);
  let depth = 0;
  let i = start;
  let mode = null; // null | "'" | '"' | '`' | '//' | '/*'
  for (; i < src.length; i++) {
    const c = src[i];
    const n = src[i + 1];
    if (mode === '//') {
      if (c === '\n') mode = null;
      continue;
    }
    if (mode === '/*') {
      if (c === '*' && n === '/') {
        mode = null;
        i++;
      }
      continue;
    }
    if (mode) {
      if (c === '\\') {
        i++;
        continue;
      }
      if (c === mode) mode = null;
      continue;
    }
    if (c === '/' && n === '/') {
      mode = '//';
      i++;
      continue;
    }
    if (c === '/' && n === '*') {
      mode = '/*';
      i++;
      continue;
    }
    if (c === "'" || c === '"' || c === '`') {
      mode = c;
      continue;
    }
    if (c === '{') depth++;
    else if (c === '}') {
      depth--;
      if (depth === 0) break;
    }
  }
  if (depth !== 0) throw new Error('花括号未配平，配置字面量解析失败');
  return src.slice(start, i + 1);
}

/** 最小 CommonJS 运行时：只提供模块解析，不注入任何宿主能力 */
function loadModuleTree(rootDir) {
  const cache = new Map();
  const req = (id, fromDir = rootDir) => {
    if (cache.has(id)) return cache.get(id);
    const file = path.isAbsolute(id) ? id : path.join(fromDir, `${id}.js`);
    const src = fs.readFileSync(file, 'utf8');
    const mod = { exports: {} };
    cache.set(id, mod.exports);
    const fn = vm.runInThisContext(
      `(function (exports, module, require) {\n${src}\n})`,
      { filename: file },
    );
    fn(mod.exports, mod, (dep) => req(dep, path.dirname(file)));
    cache.set(id, mod.exports);
    return mod.exports;
  };
  return req;
}

// ── 1. 源树形态：真实执行重建的 __m0 工厂，经其公开访问器拿到活配置 ──
//    护栏：__m0 导出的 `cfg` 是查询函数 cfg(path, fallback)，**不是配置对象**；
//    真配置由模块私有的 __CFG 持有，公开访问器是 loadConfig()。
const reqFromSrc = loadModuleTree(path.join(ROOT, 'src/modules'));
const m0 = reqFromSrc('__m0');
if (typeof m0.loadConfig !== 'function') throw new Error('源树 __m0 未导出 loadConfig —— 导出链损坏');
const cfgFromSrcTree = m0.loadConfig();
if (typeof cfgFromSrcTree !== 'object' || cfgFromSrcTree === null) {
  throw new Error(`源树 loadConfig() 未返回对象（实际 ${typeof cfgFromSrcTree}）`);
}

// ── 2. 原产物形态：切出 __m0 块，跑出 __CFG ──
const blockAt = code.indexOf('__tables["__m0"]');
const blockEnd = code.indexOf('__tables["__m1"]');
const m0Block = code.slice(blockAt, blockEnd);
const cfgLiteral = grabLiteral(m0Block, '__CFG');
const cfgFromBaseline = vm.runInNewContext(`(${cfgLiteral})`, Object.create(null), { timeout: 5000 });

// ── 2b. 设计 Token 同样分离：__TOK 与 __CFG 并列为 __m0 的两个字面量真源 ──
const tokLiteral = grabLiteral(m0Block, '__TOK');
const tokFromBaseline = vm.runInNewContext(`(${tokLiteral})`, Object.create(null), { timeout: 5000 });
const tokFromSrcTree = m0.designTokens();

// ── 3. 逐键深比对 ──
function deepDiff(a, b, trail = '', out = []) {
  const ta = a === null ? 'null' : Array.isArray(a) ? 'array' : typeof a;
  const tb = b === null ? 'null' : Array.isArray(b) ? 'array' : typeof b;
  if (ta !== tb) {
    out.push(`${trail || "(根)"}: 类型不同 ${ta} vs ${tb}`);
    return out;
  }
  if (ta === 'object' || ta === 'array') {
    const ka = Object.keys(a);
    const kb = Object.keys(b);
    for (const k of ka) if (!kb.includes(k)) out.push(`${trail}.${k}: 仅存在于源树`);
    for (const k of kb) if (!ka.includes(k)) out.push(`${trail}.${k}: 仅存在于原产物`);
    for (const k of ka) if (kb.includes(k)) deepDiff(a[k], b[k], `${trail}.${k}`, out);
    return out;
  }
    if (!Object.is(a, b)) out.push(`${trail || '(根)'}: 值不同 ${JSON.stringify(a)} vs ${JSON.stringify(b)}`);
  return out;
}

const diffs = deepDiff(cfgFromSrcTree, cfgFromBaseline);
const tokDiffs = deepDiff(tokFromSrcTree, tokFromBaseline);
const countLeaves = (o) =>
  o && typeof o === 'object' ? Object.values(o).reduce((a, v) => a + countLeaves(v), 0) : 1;

fs.mkdirSync(path.join(ROOT, 'data'), { recursive: true });
fs.writeFileSync(path.join(ROOT, 'data/config.json'), JSON.stringify(cfgFromBaseline, null, 2) + '\n', 'utf8');
fs.writeFileSync(
  path.join(ROOT, 'data/config.provenance.json'),
  JSON.stringify(
    {
      generatedBy: 'tools/extract-config.mjs',
      generatedAt: new Date().toISOString(),
      source: MANIFEST.meta.source,
      module: '__m0',
      extractMethod: 'vm 真实执行 __CFG 字面量（非文本比对）',
      comparison: {
        keysTopLevel: Object.keys(cfgFromBaseline).length,
        leafValues: countLeaves(cfgFromBaseline),
        diffs,
        equal: diffs.length === 0,
      },
    },
    null,
    2,
  ) + '\n',
  'utf8',
);

console.log(`[config] 顶层键 ${Object.keys(cfgFromBaseline).length} 个，叶值 ${countLeaves(cfgFromBaseline)} 个`);
console.log(`[config] 源树 cfg 与原产物 __CFG 深度比对：${diffs.length === 0 ? '完全一致 ✓' : `差异 ${diffs.length} 处 ✗`}`);
for (const d of diffs.slice(0, 20)) console.log('   ' + d);
console.log(`[config] 关键分组：${Object.keys(cfgFromBaseline).join(', ')}`);
console.log(
  `[config] 设计 Token（__TOK）分离：${Object.keys(tokFromBaseline).length} 个顶层分组，` +
    `源树 designTokens() 深度比对 ${tokDiffs.length === 0 ? '完全一致 ✓' : `差异 ${tokDiffs.length} 处 ✗`}`,
);
for (const d of tokDiffs.slice(0, 10)) console.log('   ' + d);
process.exitCode = diffs.length === 0 && tokDiffs.length === 0 ? 0 : 1;
