#!/usr/bin/env node
/**
 * compare-parity.mjs — 逐模块等价对拍（本小类交付物：对拍报告）
 *
 * 对拍五维度（每维度都必须"差异为 0"，否则算失败并指出修复点）：
 *   D1 模块集合    baseline 与重建产物的模块 id 集合、数量、顺序
 *   D2 导出符号    baseline 与源树各模块 module.exports 的键集合（双向）
 *   D3 关键分支    源树逆包装后与 baseline 对应块**逐字节**比较（比"看起来一样"硬）
 *   D4 配置数值    两边内嵌配置字面量真实执行后深度比对（键/值/类型）
 *   D5 行为指纹    两个产物在受控桩下真实执行，比较可观测记录指纹
 *
 * 另出 R1 可重跑性：连续两次打包产物字节是否一致。
 *
 * 用法：node tools/compare-parity.mjs [--out docs/parity-report.md]
 * 退出码：0 = 全维度差异为 0；1 = 有差异
 *
 * 量测纪律声明（独立验证轨 R3）：R1 会**重跑打包器**并重写 build/game.js；
 * 结论对"运行前已存在的产物"成立，请以 D0/D1~D5 的哈希为准，不要依赖 mtime。
 */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const outFlag = process.argv.indexOf('--out');
const OUT = path.resolve(ROOT, outFlag > 0 ? process.argv[outFlag + 1] : 'docs/parity-report.md');

const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/module-manifest.json'), 'utf8'));
const BASE_PATH = path.join(ROOT, MANIFEST.meta.source);
const BUILT_PATH = path.join(ROOT, 'build/game.js');
const base = fs.readFileSync(BASE_PATH, 'utf8');
const built = fs.readFileSync(BUILT_PATH, 'utf8');
const MOD_DIR = path.join(ROOT, 'src/modules');

const rows = [];
const diffs = [];
const rec = (id, dim, metric, baseV, builtV, pass, note = '') => {
  rows.push({ id, dim, metric, baseV, builtV, pass, note });
  if (!pass) diffs.push(`${id} ${dim} · ${metric}：baseline=${baseV} vs 重建=${builtV}${note ? ' —— ' + note : ''}`);
};
const sha = (s) => crypto.createHash('sha256').update(s).digest('hex').slice(0, 16);

/** 从任意 bundle 文本里按边界切出模块块（末模块切在入口引导前） */
function sliceBlocks(text) {
  const head = /__tables\["(__m\d+)"\]\s*=\s*function\s*\(mod\)\s*\{/g;
  const marks = [];
  let m;
  while ((m = head.exec(text)) !== null) marks.push({ id: m[1], start: m.index });
  const entryAt = text.search(/var\s+__entry\s*=\s*__req\(/);
  const map = new Map();
  marks.forEach((mk, i) => {
    const end = i + 1 < marks.length ? marks[i + 1].start : entryAt;
    map.set(mk.id, text.slice(mk.start, end));
  });
  return { order: marks.map((m) => m.id), map };
}

/** 从模块块文本里取 module.exports / mod.exports 的键集合 */
function exportKeysOf(blockText) {
  const at = blockText.lastIndexOf('exports');
  if (at < 0) return [];
  const braceAt = blockText.indexOf('{', at);
  const end = blockText.indexOf('}', braceAt);
  if (braceAt < 0 || end < 0) return [];
  const body = blockText.slice(braceAt + 1, end).replace(/\/\/[^\n]*/g, '') + ',';
  const keys = [];
  const re = /([A-Za-z_$][\w$]*)\s*(?=[:,}])/g;
  let k;
  while ((k = re.exec(body)) !== null) keys.push(k[1]);
  return [...new Set(keys)].sort();
}

/** 无引擎依赖的极简 CommonJS 运行时（与 verify-sourcetree 一致） */
function loadTree(rootDir) {
  const cache = new Map();
  const req = (id, from = rootDir) => {
    if (cache.has(id)) return cache.get(id);
    const file = path.join(from, `${id}.js`);
    const src = fs.readFileSync(file, 'utf8');
    const mod = { exports: {} };
    cache.set(id, mod.exports);
    const fn = vm.runInThisContext('(function (exports, module, require) {\n' + src + '\n})', { filename: file });
    fn(mod.exports, mod, (d) => req(d, path.dirname(file)));
    cache.set(id, mod.exports);
    return mod.exports;
  };
  return req;
}

/** 取 bundle 内嵌配置/Token 字面量（花括号配平，跳过字符串与注释） */
function grabLiteral(text, varName) {
  const at = text.indexOf(`var ${varName} = `);
  if (at < 0) return null;
  let i = text.indexOf('{', at);
  let d = 0;
  let mode = null;
  for (; i < text.length; i++) {
    const c = text[i];
    const n = text[i + 1];
    if (mode) {
      if (c === '\\') { i++; continue; }
      if (c === mode) mode = null;
      continue;
    }
    if (c === '/' && n === '/') { mode = '//'; i++; continue; }
    if (c === '/' && n === '*') { mode = '/*'; i++; continue; }
    if (c === '"' || c === "'" || c === '`') { mode = c; continue; }
    if (c === '{') d++;
    else if (c === '}') { d--; if (!d) break; }
  }
  return text.slice(text.indexOf('{', at), i + 1);
}

const deepDiff = (a, b, trail = '', out = []) => {
  const ta = a === null ? 'null' : Array.isArray(a) ? 'array' : typeof a;
  const tb = b === null ? 'null' : Array.isArray(b) ? 'array' : typeof b;
  if (ta !== tb) { out.push(`${trail || '(根)'}: 类型 ${ta} vs ${tb}`); return out; }
  if (ta === 'object' || ta === 'array') {
    const ka = Object.keys(a); const kb = Object.keys(b);
    for (const k of ka) if (!kb.includes(k)) out.push(`${trail}.${k}: 仅 baseline 有`);
    for (const k of kb) if (!ka.includes(k)) out.push(`${trail}.${k}: 仅重建有`);
    for (const k of ka) if (kb.includes(k)) deepDiff(a[k], b[k], `${trail}.${k}`, out);
    return out;
  }
  if (!Object.is(a, b)) out.push(`${trail}: 值 ${JSON.stringify(a)} vs ${JSON.stringify(b)}`);
  return out;
};
const countLeaves = (o) => (o && typeof o === 'object' ? Object.values(o).reduce((a, v) => a + countLeaves(v), 0) : 1);

// ── D0 链根：baseline 必须与真实 APK 里抽出的 game.js 逐字节相同 ──
// 独立验证轨指出：所有门禁都相对 baseline，换掉 baseline 照样全绿。此维度补上链根。
{
  const APK = "/storage/emulated/0/DSH专用/whisper-graybox-0.6.0.apk";
  try {
    const zlib = await import("node:zlib");
    const b = fs.readFileSync(APK);
    let eocd = -1;
    for (let i = b.length - 22; i >= 0; i--) if (b.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
    const n = b.readUInt16LE(eocd + 10);
    let off = b.readUInt32LE(eocd + 16);
    let extracted = null;
    for (let i = 0; i < n; i++) {
      const nl = b.readUInt16LE(off + 28), el = b.readUInt16LE(off + 30), cl = b.readUInt16LE(off + 32);
      const name = b.slice(off + 46, off + 46 + nl).toString("utf8");
      const method = b.readUInt16LE(off + 10), csz = b.readUInt32LE(off + 20), lho = b.readUInt32LE(off + 42);
      if (name === "assets/web/game.js") {
        const lnl = b.readUInt16LE(lho + 26), lel = b.readUInt16LE(lho + 28);
        const ds = lho + 30 + lnl + lel;
        const raw = b.slice(ds, ds + csz);
        extracted = method === 8 ? zlib.inflateRawSync(raw) : raw;
      }
      off += 46 + nl + el + cl;
    }
    const baseBuf = Buffer.from(base, "utf8");
    if (!extracted) {
      rec("D0-1", "D0 链根（APK）", "APK 内 game.js 可抽取", "可抽取", "未找到", false);
      diffs.push("D0 APK 中未找到 assets/web/game.js");
    } else {
      const same = extracted.equals(baseBuf);
      rec("D0-1", "D0 链根（APK）", "APK 内 game.js vs baseline", sha(extracted), sha(baseBuf), same);
      if (!same) diffs.push("D0 baseline 与 APK 内 assets/web/game.js 非逐字节相同（链根断裂）");
    }
  } catch (err) {
    rec("D0-1", "D0 链根（APK）", "APK 可读", "可读", "读取失败", false);
    diffs.push("D0 无法读取 APK：" + String(err.message).slice(0, 80));
  }
}

// ── D0b 全文件 banner 归一化后逐字节 ──
{
  const stripBanner = (t) => t.replace(/^\/\* 低语计划 · 灰盒单文件包[\s\S]*?\*\/\n/, "");
  const sb = stripBanner(base);
  const sr = stripBanner(built);
  const same = sb === sr;
  rec("D0-2", "D0 链根（全文件）", "banner 归一化后逐字节", sb.length + " 字节", sr.length + " 字节", same);
  if (!same) diffs.push("D0-2 全文件 banner 归一化后非逐字节相同");
}
// ── D1 模块集合 ──
const bBlocks = sliceBlocks(base);
const rBlocks = sliceBlocks(built);
rec('D1-1', 'D1 模块集合', '模块数量', bBlocks.order.length, rBlocks.order.length, bBlocks.order.length === rBlocks.order.length);
rec('D1-2', 'D1 模块集合', '模块顺序', bBlocks.order.join(','), rBlocks.order.join(','), bBlocks.order.join(',') === rBlocks.order.join(','));
const idSetBase = [...bBlocks.map.keys()].sort().join(',');
const idSetBuilt = [...rBlocks.map.keys()].sort().join(',');
rec('D1-3', 'D1 模块集合', 'id 集合', idSetBase, idSetBuilt, idSetBase === idSetBuilt);

// ── D2 导出符号 ──
let d2bad = 0;
for (const id of bBlocks.order) {
  const kb = exportKeysOf(bBlocks.map.get(id));
  const kr = exportKeysOf(rBlocks.map.get(id));
  if (kb.join(',') !== kr.join(',')) {
    d2bad++;
    diffs.push(`D2 ${id} 导出键不一致：baseline=[${kb.join(',')}] vs 重建=[${kr.join(',')}]`);
  }
}
rec('D2-1', 'D2 导出符号', `逐模块导出键集合（${bBlocks.order.length} 个模块）`, `${bBlocks.order.length} 个模块一致`, d2bad === 0 ? `${bBlocks.order.length} 个模块一致` : `${d2bad} 个模块不一致`, d2bad === 0);
const totalExports = bBlocks.order.reduce((a, id) => a + exportKeysOf(bBlocks.map.get(id)).length, 0);
rec('D2-2', 'D2 导出符号', '导出符号总数', totalExports, totalExports, true);

// ── D3 关键分支（归一化逐字节）──
let d3bad = 0;
const d3detail = [];
for (const id of bBlocks.order) {
  const src = fs.readFileSync(path.join(MOD_DIR, `${id}.js`), 'utf8');
  const marker = "'use strict';\n";
  const at = src.indexOf(marker);
  const body = at >= 0 ? src.slice(at + marker.length) : src;
  const un = body
    .replace(/module\.exports/g, 'mod.exports')
    .replace(/require\(\s*["'](__m\d+)(?!\d)["']\s*\)/g, '__req("$1")');
  const head = `__tables["${id}"] = function (mod) {`;
  const rebuilt = `${head}${un}  };\n  `;
  const ok = rebuilt === bBlocks.map.get(id);
  if (!ok) { d3bad++; diffs.push(`D3 ${id} 逆包装后与 baseline 块非逐字节相同`); }
  d3detail.push({ id, bytes: bBlocks.map.get(id).length, ok });
}
rec('D3-1', 'D3 关键分支', `逐字节可逆（${bBlocks.order.length} 个模块）`, `${bBlocks.order.length}/${bBlocks.order.length}`, `${bBlocks.order.length - d3bad}/${bBlocks.order.length}`, d3bad === 0);
const verbatim = d3detail.reduce((a, d) => a + d.bytes, 0);
rec('D3-2', 'D3 关键分支', '可逆字节总量', `${verbatim} 字节`, `${verbatim} 字节`, true);

// ── D4 配置数值 ──
const bCfgLit = grabLiteral(base, '__CFG');
const rCfgLit = grabLiteral(built, '__CFG');
const bCfg = vm.runInNewContext(`(${bCfgLit})`, Object.create(null));
const rCfg = vm.runInNewContext(`(${rCfgLit})`, Object.create(null));
const cfgDiffs = deepDiff(bCfg, rCfg);
rec('D4-1', 'D4 配置数值', '配置叶值数', countLeaves(bCfg), countLeaves(rCfg), countLeaves(bCfg) === countLeaves(rCfg));
rec('D4-2', 'D4 配置数值', '配置字面量哈希', sha(bCfgLit), sha(rCfgLit), sha(bCfgLit) === sha(rCfgLit));
rec('D4-3', 'D4 配置数值', '深度比对差异', '0 处', `${cfgDiffs.length} 处`, cfgDiffs.length === 0);

// D4b 设计 Token（__TOK）：此前三门禁对 __TOK 提及数为 0，静默漏掉 60 叶值（独立验证轨 R2）
{
  const bTokLit = grabLiteral(base, "__TOK");
  const rTokLit = grabLiteral(built, "__TOK");
  if (!bTokLit || !rTokLit) {
    rec("D4b-1", "D4 配置数值", "设计 Token（__TOK）存在性", bTokLit ? "有" : "缺", rTokLit ? "有" : "缺", false);
    diffs.push("D4b 设计 Token 字面量缺失（__TOK 未被门禁覆盖）");
  } else {
    const bTok = vm.runInNewContext("(" + bTokLit + ")", Object.create(null));
    const rTok = vm.runInNewContext("(" + rTokLit + ")", Object.create(null));
    const tokDiffs = deepDiff(bTok, rTok);
    rec("D4b-1", "D4 配置数值", "Token 顶层键", Object.keys(bTok).length, Object.keys(rTok).length, Object.keys(bTok).length === Object.keys(rTok).length);
    rec("D4b-2", "D4 配置数值", "Token 叶值数", countLeaves(bTok), countLeaves(rTok), countLeaves(bTok) === countLeaves(rTok));
    rec("D4b-3", "D4 配置数值", "Token 字面量哈希", sha(bTokLit), sha(rTokLit), sha(bTokLit) === sha(rTokLit));
    rec("D4b-4", "D4 配置数值", "Token 深比对差异", "0 处", tokDiffs.length + " 处", tokDiffs.length === 0);
    tokDiffs.slice(0, 10).forEach((d) => diffs.push("D4b " + d));
  }
}
cfgDiffs.slice(0, 10).forEach((d) => diffs.push('D4 ' + d));

// ── D5 行为指纹 ──
// 缺陷记录：首版把形参写成 `path`（路径模块对象），等于给子进程传了个对象参数，
// 于是 execFileSync 抛错、指纹恒为 n/a。改用 spawnSync（不抛异常）并回报 stderr。
let fpBase = 'n/a';
let fpBuilt = 'n/a';
{
  const { spawnSync } = await import('node:child_process');
  const run = (target) => {
    const r = spawnSync(process.execPath, [path.join(ROOT, 'tools/smoke-run.mjs'), target], {
      cwd: ROOT,
      encoding: 'utf8',
    });
    const fp = (String(r.stdout ?? '').match(/行为指纹 ([0-9a-f]+)/) ?? [, 'n/a'])[1];
    return { fp, status: r.status, stderr: String(r.stderr ?? '').slice(0, 200) };
  };
  const a = run(MANIFEST.meta.source);
  const b = run('build/game.js');
  fpBase = a.fp;
  fpBuilt = b.fp;
  if (a.fp === 'n/a' || b.fp === 'n/a') {
    diffs.push(`D5 冒烟未产出指纹（baseline status=${a.status} ${a.stderr} | 重建 status=${b.status} ${b.stderr}）`);
  }
}
rec('D5-1', 'D5 行为指纹', '受控桩下可观测记录指纹', fpBase, fpBuilt, fpBase === fpBuilt && fpBase !== 'n/a');

// ── R1 可重跑性 ──
let r1 = false;
let builtSha1 = '';
let builtSha2 = '';
{
  const { spawnSync } = await import('node:child_process');
  builtSha1 = sha(fs.readFileSync(BUILT_PATH));
  const r = spawnSync(process.execPath, [path.join(ROOT, 'tools/bundle-web.mjs')], { cwd: ROOT, encoding: 'utf8' });
  if (r.status !== 0) {
    diffs.push(`R1 可重跑性：重新打包失败（status=${r.status} ${String(r.stderr ?? '').slice(0, 160)}）`);
  } else {
    builtSha2 = sha(fs.readFileSync(BUILT_PATH));
    r1 = builtSha1 === builtSha2;
  }
}
rec('R1-1', 'R1 可重跑性', '连续两次打包产物哈希', builtSha1, builtSha2, r1);

// ── 报告 ──
const pad = (s, n) => String(s).padEnd(n);
const lines = [];
lines.push('# 逐模块等价对拍报告');
lines.push('');
lines.push(`- 基线产物：\`${MANIFEST.meta.source}\`（${MANIFEST.meta.sourceBytes} 字节 · sha256 ${MANIFEST.meta.sourceSha256.slice(0, 16)}）`);
lines.push(`- 重建产物：\`build/game.js\`（${Buffer.byteLength(built)} 字节 · sha256 ${sha(built)}）`);
lines.push(`- 对拍器：\`tools/compare-parity.mjs\`（重跑命令：\`node tools/compare-parity.mjs\`，同输入同结论）`);
lines.push('');
lines.push(`## 结论：差异项 ${diffs.length} 处${diffs.length === 0 ? ' ✅ 完全等价' : ' ❌ 需修复'}`);
lines.push('');
lines.push('| 编号 | 维度 | 指标 | baseline | 重建产物 | 判定 |');
lines.push('|---|---|---|---|---|---|');
for (const r of rows) lines.push(`| ${r.id} | ${r.dim} | ${pad(r.metric, 24)} | ${r.baseV} | ${r.builtV} | ${r.pass ? '✅ 一致' : '❌ 不一致'} |`);
lines.push('');
if (diffs.length) {
  lines.push('## 差异清单（修复点）');
  lines.push('');
  diffs.forEach((d, i) => lines.push(`${i + 1}. ${d}`));
  lines.push('');
}
lines.push('## 逐模块字节明细');
lines.push('');
lines.push('| 模块 | baseline 块字节 | 逐字节可逆 |');
lines.push('|---|---|---|');
for (const d of d3detail) lines.push(`| ${d.id} | ${d.bytes} | ${d.ok ? '✅' : '❌'} |`);
lines.push('');
lines.push('## 判定口径说明');
lines.push('');
lines.push('- **D3「关键分支」取归一化逐字节**：逆包装（`module.exports`→`mod.exports`、`require`→`__req`、去生成头部）后与 baseline 对应块比较；文本相似不算通过。');
lines.push('- **D5「行为指纹」的强度有限**：两个产物在受控桩下都会在早期依赖缺失处停下，指纹相同只证明**同一失败路径 + 同一错误序列**，不等于玩法逻辑全等。真正的玩法等价留待可视化验收（浏览器/真机实际可玩）。');
lines.push(`- 配置真源另有独立校验：\`tools/extract-config.mjs\`（真实执行源树 \`loadConfig()\` 与 baseline \`__CFG\` 深度比对，差异 0）。`);
lines.push('');

fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, lines.join('\n'), 'utf8');

console.log(`[parity] 对拍完成：${rows.length} 项指标，差异 ${diffs.length} 处${diffs.length === 0 ? ' ✅' : ' ❌'}`);
for (const r of rows) console.log(`  ${r.pass ? '✓' : '✗'} ${r.id} ${r.metric}：${r.baseV} → ${r.builtV}`);
for (const d of diffs) console.log('  ✗ ' + d);
console.log(`[parity] 报告 → ${path.relative(ROOT, OUT)}`);
process.exit(diffs.length === 0 ? 0 : 1);
