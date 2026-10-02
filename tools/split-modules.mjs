#!/usr/bin/env node
/**
 * split-modules.mjs — 按模块清单的精确字节边界把单文件产物切成分包源树
 *
 * 纪律（本小类边界，越界即违规）：
 *   · 只切分与包装，**不改写任何数值与玩法逻辑**：每个模块块的原文逐字节搬入源文件
 *   · 导出符号与依赖关系不得调整（对齐 data/module-manifest.json）
 *   · 唯一新增的包装是显式的 require 注入 + exports 导出（把原 bundle 隐式的
 *     `__req` / `mod.exports` 变成可读的 CommonJS 形态），注入行由模块清单机械推导
 *
 * 用法：node tools/split-modules.mjs
 * 产出：src/modules/__mN.js（14 个）· src/entry.js
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/module-manifest.json'), 'utf8'));
const SRC = path.join(ROOT, MANIFEST.meta.source);
const code = fs.readFileSync(SRC, 'utf8');

const MOD_REF = /__req\(\s*["'](__m\d+)(?!\d)["']\s*\)/g;

/** 与抽取器同源的边界算法：模块块 = 表项头 → 下一表项头（末模块切在入口引导前） */
function sliceModules(src) {
  const head = /__tables\["(__m\d+)"\]\s*=\s*function\s*\(mod\)\s*\{/g;
  const marks = [];
  let m;
  while ((m = head.exec(src)) !== null) marks.push({ id: m[1], start: m.index, bodyAt: m.index + m[0].length });
  const entryAtRaw = src.search(/var\s+__entry\s*=\s*__req\(/);
  const entryAt = entryAtRaw >= 0 ? entryAtRaw : src.length;
  const entryEnd = src.indexOf('\n', entryAtRaw);
  return {
    blocks: marks.map((mk, i) => {
      const next = i + 1 < marks.length ? marks[i + 1].start : entryAt;
      const end = Math.max(mk.start, next);
      return { id: mk.id, start: mk.start, end, text: src.slice(mk.start, end) };
    }),
    entryAt,
    entryText: src.slice(entryAt, entryEnd >= 0 ? entryEnd : src.length),
  };
}

/** 从模块块里取出「注入行 + 模块体」两段，并把 bundle 形态的 `mod.exports` 机械改写为
 *  CommonJS 形态的 `module.exports`（Node 包装器给的是 module/exports/require，没有 mod）。
 *  改写是等价的：原 bundle 也是把工厂产出的 exports 对象挂到缓存上。 */
function dissect(text) {
  const bodyAt = text.indexOf('{') + 1; // 表项头 `function (mod) {` 之后的模块体起点
  const expIdx = text.lastIndexOf('mod.exports');
  if (bodyAt <= 0 || expIdx < 0) throw new Error('模块块结构异常：找不到模块体或 mod.exports');
  const expEnd = text.indexOf(';', expIdx);
  if (expEnd < 0) throw new Error('模块块结构异常：mod.exports 未以 ; 结束');
  const inject = text.slice(bodyAt, expIdx); // 头部注入 + 其它前置语句（逐字节保留，仅做 __req→require 机械替换）
  const exportsLine = 'module.exports' + text.slice(expIdx + 'mod.exports'.length, expEnd + 1);
  return { inject: inject.replace(MOD_REF, 'require("$1")'), exportsLine };
}

const lineAt = (idx) => code.slice(0, idx).split('\n').length;
const { blocks, entryAt, entryText } = sliceModules(code);
const manifestById = new Map(MANIFEST.modules.map((m) => [m.id, m]));

if (blocks.length !== MANIFEST.modules.length) {
  console.error(`[split] 模块数不一致：产物 ${blocks.length} vs 清单 ${MANIFEST.modules.length}，拒绝切分`);
  process.exit(2);
}

const outDir = path.join(ROOT, 'src/modules');
fs.mkdirSync(outDir, { recursive: true });

const written = [];
for (const b of blocks) {
  const man = manifestById.get(b.id);
  if (!man) throw new Error(`清单缺少 ${b.id}`);
  const { inject, exportsLine } = dissect(b.text);
  const header = [
    `/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）`,
    ` * 模块：${b.id} → ${man.file}`,
    ` * 职责：${man.summary || '（清单未记录）'}`,
    ` * 来源：${MANIFEST.meta.source} 第 ${lineAt(b.start)}~${lineAt(b.end - 1)} 行（${b.end - b.start} 字节，逐字节搬移）`,
    ` * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")`,
    ` * 依赖：${man.deps.length ? man.deps.join(', ') : '无'}｜导出：${man.exports.join(', ') || '无'}`,
    ` */`,
  ].join('\n');
  const fileText = `${header}\n'use strict';\n${inject}${exportsLine}\n`;
  // 护栏：包装残留检查 —— 只看代码体（头部注释本就在描述改写规则，含这些字样属正常）
  const codeBody = inject + exportsLine;
  for (const residue of ['__req(', '__tables[', '__cache[', /\bmod\.exports\b/]) {
    const hit = typeof residue === 'string' ? codeBody.includes(residue) : residue.test(codeBody);
    if (hit) {
      throw new Error(`${b.id} 包装残留 \`${residue}\`：切分未完成，拒绝写出半包装文件`);
    }
  }
  const file = path.join(outDir, `${b.id}.js`);
  fs.writeFileSync(file, fileText, 'utf8');
  written.push({
    id: b.id,
    file: path.relative(ROOT, file),
    bytes: Buffer.byteLength(fileText),
    verbatimBytes: b.end - b.start,
    sha256: crypto.createHash('sha256').update(fileText).digest('hex').slice(0, 16),
    deps: man.deps,
    exports: man.exports,
  });
}

const entryFile = path.join(ROOT, 'src/entry.js');
const entryOut = [
  `/* 低语计划 · 灰盒源树入口（tools/split-modules.mjs 生成，勿手改）`,
  ` * 来源：${MANIFEST.meta.source} 第 ${lineAt(entryAt)} 行起`,
  ` */`,
  `'use strict';`,
  entryText.replace(/__req\(/g, 'require('),
  `globalThis.startGame = function () { return __entry.startGame(); };`,
  '',
].join('\n');
fs.writeFileSync(entryFile, entryOut, 'utf8');

const indexPath = path.join(ROOT, 'src/index.json');
fs.writeFileSync(
  indexPath,
  JSON.stringify(
    {
      meta: {
        generatedBy: 'tools/split-modules.mjs',
        generatedAt: new Date().toISOString(),
        source: MANIFEST.meta.source,
        moduleCount: written.length,
      },
      files: written,
      entry: { file: 'src/entry.js', bytes: Buffer.byteLength(entryOut) },
    },
    null,
    2,
  ) + '\n',
  'utf8',
);

console.log(`[split] ${written.length} 个模块 → src/modules/ ，入口 → src/entry.js ，索引 → src/index.json`);
console.log(`[split] 原文搬移总字节 ${written.reduce((a, w) => a + w.verbatimBytes, 0)} / 产物 ${Buffer.byteLength(code)}`);
for (const w of written) console.log(`  ${w.id}.js  ${String(w.bytes).padStart(6)}B  sha ${w.sha256}  依赖[${w.deps.join(',')}]`);
