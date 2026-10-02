#!/usr/bin/env node
/**
 * spec-ledger.mjs — 从**五版方案**抽取要求台账（把"完成所有要求"变成可逐条核对的东西）
 *
 * ## 为什么五版都要看
 * V9 是终稿冻结基线（内含「冲突裁决终表」，裁决 V5~V8 的分歧），但 V9 明确写了
 * "§17~§29 详解见各版原文；四版原文归档不再维护"。也就是说：
 *   · **裁决口径以 V9 为准**
 *   · **规格细节与理由仍在 V5~V8**（V9 只做整合与收敛）
 * 所以台账逐版抽取并标注版本，便于回答"这条要求出处是哪一版、V9 是否改过口径"。
 *
 * ## 做法（不追求 NLP，只做可靠的结构化）
 *   ① 解析每版的章节结构（第 X 章 / X.Y 节）
 *   ② 抽出**承重行**：含量化目标（≥/≤/数字+单位）、强制措辞（必须/禁止/不得/一律/不得…）、
 *      门禁词（门禁/gate/校验/清单/准入）、规则词（规则/纪律/约定/上限/阈值/预算/配额）
 *   ③ 输出 data/spec-ledger.json + docs/spec/LEDGER.md
 *
 * 用法：node tools/spec-ledger.mjs [--emit]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const OUT_JSON = path.join(ROOT, 'data/spec-ledger.json');
const OUT_MD = path.join(ROOT, 'docs/spec/LEDGER.md');

const SPECS = [
  { id: 'V9', file: 'V9-fulltext.txt', role: '终稿冻结基线（冲突裁决以此为准）' },
  { id: 'V8', file: 'V8-fulltext.txt', role: '综合裁决（含反建议）' },
  { id: 'V7', file: 'V7-fulltext.txt', role: '风险清偿 + 后端规格书全文' },
  { id: 'V6', file: 'V6-fulltext.txt', role: '设计补全（十项改进）' },
  { id: 'V5', file: 'V5-fulltext.txt', role: '立项基线（八域框架 + 数值基线）' },
];

const SECTION_RE = /^(\d{1,2})(?:\.(\d{1,2}))?\s*[\u3000\s]\s*(\S[^。]{0,40})$/;
const QUANT = /[≥≤]\s*\d|\d+\s*(ms|毫秒|秒|s\b|分钟|小时|天|次|项|个|人|台|MB|KB|GB|%|％|fps|Hz|路|条|层|档|页|语|集|份|名)/;
const MANDATE = /(必须|禁止|不得|一律|不允许|严禁|强制|应满足|需满足|硬门禁|铁律|红线|冻结)/;
const GATE = /(门禁|gate|校验|清单|验收|准入|基线)/i;
const RULE = /(规则|纪律|约定|标准|上限|下限|阈值|预算|配额)/;

/** 解析一版文本 → 章节 + 承重行 */
function parseSpec(version, role, file) {
  const src = path.join(ROOT, 'docs/spec', file);
  if (!fs.existsSync(src)) { console.error(`[ledger] ✗ 缺 ${file}（先跑 tools/extract-spec.mjs）`); process.exit(1); }
  const raw = fs.readFileSync(src, 'utf8').split('\n');
  const lines = raw.map((l, i) => ({ n: i + 1, t: l.replace(/\s+/g, ' ').trim() }));

  // 章节
  const chapters = [];
  const seen = new Set();
  for (const { n, t } of lines) {
    const m = t.match(SECTION_RE);
    if (!m) continue;
    if (!/[\u4e00-\u9fff]/.test(m[3])) continue;
    if (m[3].length < 2) continue;
    const num = m[2] ? `${m[1]}.${m[2]}` : m[1];
    if (seen.has(num)) continue;
    seen.add(num);
    chapters.push({ num, title: m[3].trim(), line: n });
  }

  // 承重行
  const requirements = [];
  for (const { n, t } of lines) {
    if (t.length < 8) continue;
    if (/Project Whisper · V9\.0/.test(t)) continue;      // 页眉
    if (/^\d+ \/ \d+$/.test(t)) continue;                  // 页码
    const isQuant = QUANT.test(t), isMandate = MANDATE.test(t);
    const isGate = GATE.test(t), isRule = RULE.test(t);
    if (!(isMandate || (isQuant && (isGate || isRule)))) continue;
    const chap = [...chapters].reverse().find((c) => c.line <= n);
    requirements.push({
      specVersion: version,
      spec: chap ? `§${chap.num}` : '§(未定位)',
      specTitle: chap ? chap.title : '',
      line: n,
      kind: isMandate ? 'mandate' : 'quantified',
      tags: [isQuant && 'quant', isGate && 'gate', isRule && 'rule'].filter(Boolean),
      text: t.slice(0, 300),
    });
  }
  return { version, role, source: `docs/spec/${file}`, lines: raw.length, chapters, requirements };
}

const versions = SPECS.map((s) => parseSpec(s.id, s.role, s.file));
const totals = {
  lines: versions.reduce((a, v) => a + v.lines, 0),
  chapters: versions.reduce((a, v) => a + v.chapters.length, 0),
  requirements: versions.reduce((a, v) => a + v.requirements.length, 0),
};
const ledger = {
  note: '五版方案要求台账（自动抽取）。V9 为冲突裁决基线；V5~V8 承载规格细节与理由。抽取器 tools/spec-ledger.mjs',
  versions: versions.map((v) => ({ ...v, chapterCount: v.chapters.length, requirementCount: v.requirements.length })),
  totals,
};

if (process.argv.includes('--emit')) {
  fs.mkdirSync(path.dirname(OUT_JSON), { recursive: true });
  fs.writeFileSync(OUT_JSON, JSON.stringify(ledger, null, 2) + '\n', 'utf8');

  const md = ['# 方案要求台账（五版自动抽取）\n',
    '> 抽取器 `tools/spec-ledger.mjs` · 原文 `docs/spec/*-fulltext.txt`\n',
    '> **口径纪律**：冲突以 V9 §3「冲突裁决终表」为准；V5~V8 保留规格细节与理由。\n',
    `- 合计：**${totals.chapters}** 个章节 · **${totals.requirements}** 条承重行 · 原文 ${totals.lines} 行\n`,
    '| 版本 | 性质 | 章节 | 承重行 | 原文行 |', '|---|---|---|---|---|'];
  for (const v of versions) md.push(`| ${v.version} | ${v.role} | ${v.chapters.length} | ${v.requirements.length} | ${v.lines} |`);
  for (const v of versions) {
    md.push(`\n---\n\n## ${v.version}　${v.role}\n`);
    md.push('### 章节结构\n');
    md.push('| 节号 | 标题 | 原文行 |', '|---|---|---|');
    for (const c of v.chapters) md.push(`| §${c.num} | ${c.title} | ${c.line} |`);
    md.push('\n### 承重行\n');
    const bySec = new Map();
    for (const r of v.requirements) {
      if (!bySec.has(r.spec)) bySec.set(r.spec, []);
      bySec.get(r.spec).push(r);
    }
    for (const [sec, list] of [...bySec.entries()].sort((a, b) => a[0].localeCompare(b[0], 'zh', { numeric: true }))) {
      md.push(`#### ${sec}　${list[0].specTitle}\n`);
      for (const r of list) md.push(`- [ ] \`L${r.line}\` **${r.kind}**${r.tags.length ? ` (${r.tags.join('/')})` : ''}：${r.text}`);
      md.push('');
    }
  }
  fs.writeFileSync(OUT_MD, md.join('\n') + '\n', 'utf8');
  console.log('[ledger] 已写出 data/spec-ledger.json 与 docs/spec/LEDGER.md');
}

console.log('[ledger] 五版合计：章节 %d · 承重行 %d', totals.chapters, totals.requirements);
for (const v of versions) {
  const byKind = v.requirements.reduce((a, r) => (a[r.kind] = (a[r.kind] ?? 0) + 1, a), {});
  console.log(`  ${v.version}  ${String(v.chapters.length).padStart(3)} 章 · ${String(v.requirements.length).padStart(3)} 条 ${JSON.stringify(byKind)}`);
}
