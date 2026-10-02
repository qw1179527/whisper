#!/usr/bin/env node
/**
 * extract-modules.mjs — 从 0.6.0 单文件产物抽取模块清单与依赖图
 *
 * 目的：把「只有产物」的灰盒变成有事实基准的工程 —— 后续分包源树重建、
 * 打包链重建、逐模块等价对拍三件事都以本脚本产出的 module-manifest.json 为准。
 *
 * 本脚本只读，不改产物；自身不依赖任何包（V9 §19 零依赖纪律）。
 *
 * 用法：
 *   node tools/extract-modules.mjs [产物路径] [输出路径]
 * 默认：baseline/game-0.6.0.js → data/module-manifest.json
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = process.argv[2] ?? path.join(ROOT, 'baseline/game-0.6.0.js');
const OUT = process.argv[3] ?? path.join(ROOT, 'data/module-manifest.json');

/** 抽取每个模块的表项块（从 `__tables["__mN"]` 到下一个表项/入口引导之前）
 *
 * 边界陷阱：产物末尾的入口引导 `var __entry = __req("__m13")` 与 `__m13` 同属一个
 * 模块切片，会被误判成 `__m13 → __m13` 自环。因此最后一个模块的终点必须切在入口引导处。
 */
function sliceModules(code) {
  const head = /__tables\["(__m\d+)"\]\s*=\s*function\s*\(mod\)\s*\{/g;
  const marks = [];
  let m;
  while ((m = head.exec(code)) !== null) marks.push({ id: m[1], start: m.index, bodyAt: m.index + m[0].length });
  const entryAtRaw = code.search(/var\s+__entry\s*=\s*__req\(/);
  const entryAt = entryAtRaw >= 0 ? entryAtRaw : code.length;
  const blocks = marks.map((mk, i) => {
    const next = i + 1 < marks.length ? marks[i + 1].start : entryAt;
    const end = Math.max(mk.start, next);
    return { id: mk.id, start: mk.start, end, text: code.slice(mk.start, end) };
  });
  return { blocks, entryAt, entryLine: lineAt(code, entryAt), entrySource: entryAtRaw >= 0 ? code.slice(entryAtRaw, entryAtRaw + 80).split('\n')[0].trim() : null };
}

/** 位置 → 1-based 行号（证据用，可回原产物核对） */
const lineAt = (code, idx) => code.slice(0, idx).split('\n').length;

/** 责任首注：块内首个块注释的正文（去掉 * 前缀），压缩为单行 */
function summaryOf(text) {
  const c = text.match(/\/\*\*([\s\S]*?)\*\//);
  if (!c) return '';
  return c[1]
    .split('\n')
    .map((l) => l.replace(/^\s*\*?\s?/, '').trim())
    .filter(Boolean)
    .join(' ')
    .replace(/\s+/g, ' ')
    .trim();
}

/** 无文档注释时的机械兜底职责：由块内首个顶层声明推断（如 __m0 的 `var __CFG = {...}`）
 *  规则式抽取，不做语义猜测；summarySource 标注来源以便复核 */
function fallbackSummary(text) {
  const v = text.match(/\b(?:var|let|const)\s+([A-Za-z_$][\w$]*)\s*=\s*(\{|\[|function)/);
  const f = text.match(/\bfunction\s+([A-Za-z_$][\w$]*)\s*\(/);
  if (v) return `无文档注释：由顶层声明 \`${v[1]}\` 推断为数据/配置模块（首字符 ${v[2]}）`;
  if (f) return `无文档注释：由顶层函数 \`${f[1]}\` 推断`;
  return '';
}

/** 模块 id 词法：必须带边界，否则 __m1 会把 __req("__m12") 截断成假依赖（自环 bug 的根因） */
const MOD_REF = /__req\(\s*["'](__m\d+)(?!\d)["']\s*\)/g;

/** 依赖边：块内所有 __req("__mN") */
function depsOf(text) {
  const set = new Set();
  for (const d of text.matchAll(MOD_REF)) set.add(d[1]);
  return [...set].sort((a, b) => Number(a.slice(3)) - Number(b.slice(3)));
}

/** 导出符号：**只认 `mod.exports = { ... }` 的字面键**（运行时权威），
 *  绝不把依赖导入的解构（`var __nsN = __req(...); var A = __nsN.A;`）当成导出——
 *  首版把别名列表并进导出，导致 __m12 多报 10 个符号（真导出只有 startGame）。 */
function exportsOf(code, text) {
  const exp = text.match(/mod\.exports\s*=\s*\{([\s\S]*?)\}\s*;?[\s\S]*$/);
  let exportsKeys = [];
  if (exp) {
    const body = exp[1].replace(/\/\/[^\n]*/g, '');
    // 键形态覆盖三种真实写法：`{ a: 1, b: 2 }`、`{ parseGLB }`、`{ startGame: startGame }`
    // 末尾补哨兵 ',' ：捕获组已吃掉 '}'，否则「空格 + 结尾」会让 :[,}] 永远无字符可看
    const keys = body + ',';
    const re = /([A-Za-z_$][\w$]*)\s*(?=[:,}])/g;
    let k;
    while ((k = re.exec(keys)) !== null) exportsKeys.push(k[1]);
  }
  const fnNames = [...text.matchAll(/function\s+([A-Za-z_$][\w$]*)\s*\(/g)].map((x) => x[1]);
  const exports = [...new Set(exportsKeys)].sort();
  return {
    exports,
    exportsObjectKeys: exports,
    locals: [...new Set(fnNames)].filter((f) => !exports.includes(f)).sort(),
  };
}

/** 环检测（DFS 三色法）：依赖图必须无环 */
function findCycles(ids, edges) {
  const color = new Map(ids.map((i) => [i, 0]));
  const cycles = [];
  const stack = [];
  const visit = (n) => {
    color.set(n, 1);
    stack.push(n);
    for (const d of edges.get(n) ?? []) {
      if (!color.has(d)) continue;
      if (color.get(d) === 1) cycles.push([...stack.slice(stack.indexOf(d)), d].join(' → '));
      else if (color.get(d) === 0) visit(d);
    }
    stack.pop();
    color.set(n, 2);
  };
  for (const i of ids) if (color.get(i) === 0) visit(i);
  return cycles;
}

const code = fs.readFileSync(SRC, 'utf8');
const { blocks, entryLine, entrySource } = sliceModules(code);
if (blocks.length === 0) {
  console.error(`[extract-modules] 未找到任何模块表项，检查输入产物：${SRC}`);
  process.exit(2);
}

const ids = blocks.map((b) => b.id);
const edges = new Map();
const modules = blocks.map((b) => {
  const deps = depsOf(b.text);
  edges.set(b.id, deps);
  const ex = exportsOf(code, b.text);
  const doc = summaryOf(b.text);
  const summary = doc || fallbackSummary(b.text);
  return {
    id: b.id,
    file: `${b.id.replace(/^__/, '')}.js`,
    summary,
    summarySource: doc ? 'doc-comment' : summary ? 'declaration-fallback' : 'none',
    deps,
    exports: ex.exports,
    locals: ex.locals,
    evidence: {
      startLine: lineAt(code, b.start),
      endLine: lineAt(code, Math.max(b.start, b.end - 1)),
      bytes: b.end - b.start,
      depsRaw: [...b.text.matchAll(MOD_REF)].length,
      depSites: [...b.text.matchAll(MOD_REF)].map((d) => ({
        dep: d[1],
        line: lineAt(code, b.start + d.index),
      })),
    },
  };
});

const cycles = findCycles(ids, edges);
const reverse = Object.fromEntries(ids.map((i) => [i, []]));
for (const [from, tos] of edges) for (const t of tos) reverse[t]?.push(from);

const manifest = {
  meta: {
    source: path.relative(ROOT, SRC),
    sourceBytes: Buffer.byteLength(code),
    sourceSha256: crypto.createHash('sha256').update(code).digest('hex'),
    moduleCount: modules.length,
    generatedBy: 'tools/extract-modules.mjs',
    generatedAt: new Date().toISOString(),
    note: '本文件是灰盒源码反拆的事实基准：后续分包源树重建、打包链重建、逐模块等价对拍均以此为准。',
  },
  graph: {
    acyclic: cycles.length === 0,
    cycles,
    roots: ids.filter((i) => (reverse[i] ?? []).length === 0),
    leaves: ids.filter((i) => (edges.get(i) ?? []).length === 0),
    reverse,
  },
  entry: { line: entryLine, source: entrySource, exportedGlobal: 'startGame' },
  modules,
};

fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, JSON.stringify(manifest, null, 2) + '\n', 'utf8');

console.log(`[extract-modules] ${modules.length} 模块 → ${path.relative(ROOT, OUT)}`);
console.log(`[extract-modules] 依赖图无环: ${manifest.graph.acyclic}${cycles.length ? ' 环: ' + cycles.join(' | ') : ''}`);
console.log(`[extract-modules] 根(无上游): ${manifest.graph.roots.join(', ')} · 叶(无依赖): ${manifest.graph.leaves.join(', ')}`);
for (const m of modules) console.log(`  ${m.id} → [${m.deps.join(',')}]  导出 ${m.exports.length} 项`);
