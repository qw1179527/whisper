#!/usr/bin/env node
/**
 * verify-sourcetree.mjs — 分包源树的门控验证（本小类"护栏"）
 *
 * 三关，任一不过即失败（不合格不进入汇总结论）：
 *   G1 文件对应：src/modules 下模块文件集合 == 模块清单 id 集合（数量与名字双向零差异）
 *   G2 导出对齐：真实执行源树，每个模块 module.exports 的键集合 == 清单 exports（双向零差异 + 无空导出）
 *   G3 依赖对齐：真实执行时模块实际 require 的依赖集合 == 清单 deps（双向零差异），
 *               且 require 的符号在依赖模块里真实存在（拒绝"导入了不存在的名字"）
 *
 * 用法：node tools/verify-sourcetree.mjs
 */
import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/module-manifest.json'), 'utf8'));
const MOD_DIR = path.join(ROOT, 'src/modules');

const fails = [];
const ok = (m) => console.log('  ✓ ' + m);
const bad = (m) => {
  fails.push(m);
  console.log('  ✗ ' + m);
};

// ── G1 文件对应 ──
console.log('G1 文件对应');
const files = fs
  .readdirSync(MOD_DIR)
  .filter((f) => f.endsWith('.js'))
  .map((f) => f.replace(/\.js$/, ''))
  .sort();
const ids = MANIFEST.modules.map((m) => m.id).sort();
const g1 = JSON.stringify(files) === JSON.stringify(ids);
(g1 ? ok : bad)(`模块文件 ${files.length} 个 vs 清单 ${ids.length} 个：${g1 ? '双向一致' : `不一致 缺[${ids.filter((i) => !files.includes(i))}] 多[${files.filter((i) => !ids.includes(i))}]`}`);

// ── G1.5 字节级可逆性：去掉生成的头部注释，把 14 个源文件拼回原模块块 ──
console.log('G1.5 字节级可逆性');
{
  const code = fs.readFileSync(path.join(ROOT, MANIFEST.meta.source), 'utf8');
  const MOD_REF = /__req\(\s*["'](__m\d+)(?!\d)["']\s*\)/g;
  const head = /__tables\["(__m\d+)"\]\s*=\s*function\s*\(mod\)\s*\{/g;
  const marks = [];
  let mm;
  while ((mm = head.exec(code)) !== null) marks.push({ id: mm[1], start: mm.index });
  const entryAt = code.search(/var\s+__entry\s*=\s*__req\(/);
  const originalBlocks = new Map(
    marks.map((mk, i) => {
      const end = i + 1 < marks.length ? marks[i + 1].start : entryAt;
      return [mk.id, code.slice(mk.start, end)];
    }),
  );

  let byteExact = 0;
  const problems = [];
  const patchedOk = [];
  // 已登记补丁：patches/MANIFEST.json 记录的模块**允许**与 baseline 不同（差异由补丁本身产生）。
  // 未登记却不同 → 仍然失败。这样"有人偷偷改了源树"不会被放行，而"有意修复"也不会被误拦。
  let patched = new Map();
  try {
    const ledger = JSON.parse(fs.readFileSync(path.join(ROOT, 'patches/MANIFEST.json'), 'utf8'));
    for (const e of ledger.patchedModules ?? []) patched.set(e.module.replace(/\.js$/, ''), e);
  } catch { /* 无台账 → 不允许任何差异 */ }
  for (const m of MANIFEST.modules) {
    const src = fs.readFileSync(path.join(MOD_DIR, `${m.id}.js`), 'utf8');
    const bodyAt = src.indexOf("'use strict';\n");
    const body = src.slice(bodyAt + "'use strict';\n".length);
    // 逆包装：module.exports → mod.exports；require("__mN") → __req("__mN")
    // 拼回公式与切分器严格对称：`${header}${块内文本（含尾部换行）}  };\n  `
    // （切分时 block = header + 块内文本 + '\n' + '  };\n  '，故此处不得删换行）
    let rebuilt = body
      .replace(/module\.exports/, 'mod.exports')
      .replace(/require\(\s*["'](__m\d+)(?!\d)["']\s*\)/g, '__req("$1")');
    const header = `__tables["${m.id}"] = function (mod) {`;
    const candidate = `${header}${rebuilt}  };\n  `;
    const original = originalBlocks.get(m.id);
    if (candidate === original) byteExact++;
    else {
      const a = original ?? '';
      let i = 0;
      while (i < Math.min(a.length, candidate.length) && a[i] === candidate[i]) i++;
      const led = patched.get(m.id);
      if (led) {
        const cur = crypto.createHash('sha256').update(fs.readFileSync(path.join(MOD_DIR, `${m.id}.js`))).digest('hex');
        if (cur !== led.sha256) {
          problems.push(`${m.id} 已登记补丁的内容与台账哈希不符（台账 ${led.sha256.slice(0, 12)} vs 现 ${cur.slice(0, 12)}）—— 有人绕过补丁脚本改了它`);
        } else {
          patchedOk.push(`${m.id} ← 补丁 ${led.patches.map((p) => p.id + ':' + p.name).join(', ')}（已登记，允许与 baseline 不同）`);
        }
      } else {
        problems.push(`${m.id} 拼回后与原块不同（首个差异在第 ${i} 字节：原=${JSON.stringify(a.slice(i, i + 40))} 拼=${JSON.stringify(candidate.slice(i, i + 40))}）`);
      }
    }
  }
  for (const x of patchedOk) console.log('  · ' + x);
  if (problems.length === 0) ok(`${byteExact} 个模块逐字节可拼回原产物块${patchedOk.length ? ` · ${patchedOk.length} 个为已登记补丁（差异可审计）` : ''}`);
  else problems.forEach(bad);
  void MOD_REF;
}

// ── 真实执行源树（记录每个模块实际 require 到什么） ──
const loaded = new Map();
const actualDeps = new Map();
const req = (id, from) => {
  if (loaded.has(id)) return loaded.get(id);
  const file = path.join(from, `${id}.js`);
  if (!fs.existsSync(file)) throw new Error(`require 目标不存在：${id}（来自 ${path.basename(from)}）`);
  const src = fs.readFileSync(file, 'utf8');
  const mod = { exports: {} };
  const seen = new Set();
  actualDeps.set(id, seen);
  loaded.set(id, mod.exports);
  const fn = vm.runInThisContext('(function (exports, module, require) {\n' + src + '\n})', { filename: file });
  fn(mod.exports, mod, (dep) => {
    seen.add(dep);
    return req(dep, path.dirname(file));
  });
  loaded.set(id, mod.exports);
  return mod.exports;
};

let runtimeError = null;
try {
  req('__m13', MOD_DIR); // 入口 → 向下加载整棵树
} catch (e) {
  runtimeError = e;
}
if (runtimeError) bad(`真实执行源树失败：${runtimeError.message}`);
else ok(`真实执行源树成功：加载 ${loaded.size} 个模块（入口 __m13）`);

// ── G2 导出对齐 ──
console.log('G2 导出符号对齐');
if (!runtimeError) {
  for (const m of MANIFEST.modules) {
    const actual = Object.keys(loaded.get(m.id) ?? {}).sort();
    const expect = [...m.exports].sort();
    const same = JSON.stringify(actual) === JSON.stringify(expect);
    if (!same) {
      bad(`${m.id} 导出不一致：清单[${expect.join(',')}] 实际[${actual.join(',')}]`);
    }
  }
  const mismatches = fails.filter((f) => f.includes('导出不一致')).length;
  if (mismatches === 0) ok(`14 个模块导出键集合与清单零差异（共 ${MANIFEST.modules.reduce((a, m) => a + m.exports.length, 0)} 个导出符号）`);
}

// ── G3 依赖对齐 + 符号存在性 ──
console.log('G3 依赖与导入符号');
if (!runtimeError) {
  for (const m of MANIFEST.modules) {
    const actual = [...(actualDeps.get(m.id) ?? new Set())].sort();
    const expect = [...m.deps].sort();
    if (JSON.stringify(actual) !== JSON.stringify(expect)) {
      bad(`${m.id} 依赖不一致：清单[${expect.join(',')}] 实际[${actual.join(',')}]`);
    }
  }
  const mism = fails.filter((f) => f.includes('依赖不一致')).length;
  if (mism === 0) ok('14 个模块实际 require 依赖集合与清单零差异');

  // 导入符号存在性：源码里 `var X = __nsN.name` 抽出的 name 必须在依赖导出里
  let checked = 0;
  for (const m of MANIFEST.modules) {
    const src = fs.readFileSync(path.join(MOD_DIR, `${m.id}.js`), 'utf8');
    for (const mm of src.matchAll(/var\s+__ns\d+\s*=\s*require\(["'](__m\d+)(?!\d)["']\)\s*;([\s\S]{0,600}?);/g)) {
      const dep = mm[1];
      const depExports = new Set(Object.keys(loaded.get(dep) ?? {}));
      for (const idm of mm[2].matchAll(/(?:^|,)\s*([A-Za-z_$][\w$]*)\s*(?==|,|$)/g)) {
        checked++;
        if (!depExports.has(idm[1])) bad(`${m.id} 从 ${dep} 导入的 \`${idm[1]}\` 在依赖导出中不存在`);
      }
    }
  }
  if (!fails.some((f) => f.includes('不存在'))) ok(`导入符号存在性：核验 ${checked} 个名字，全部在依赖模块导出中`);
}

console.log('');
if (fails.length) {
  console.log(`结果：失败 ${fails.length} 项 ✗`);
  process.exit(1);
}
console.log('结果：G1/G2/G3 全部通过 ✓');
