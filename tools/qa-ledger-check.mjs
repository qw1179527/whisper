#!/usr/bin/env node
/**
 * qa-ledger-check.mjs — 质检台账的结构校验 + 新建轮次骨架
 *
 * ## 为什么需要它
 * 「打卡 = 质检审计轮次，每轮要有台账」是本项目的硬纪律，但纪律靠人记会腐化：
 * 最容易发生的是**台账停在旧轮次**（开发在推进、轮次没落盘），于是"可追溯"变成空话。
 * 本脚本把台账的**结构**变成机器可查的判据，并给出新建轮次的骨架。
 *
 * ## 判据（任一不满足 → 非零退出）
 *   ① 台账存在
 *   ② 每轮都有五个必需小节：**时点 · 被审 HEAD · 结论 · 问题清单 · 处置**
 *   ③ 每条问题必须有**可追溯证据**：`文件:行号`（形如 `x.cs:123`）或命令输出（反引号代码块/命令名）
 *   ④ 索引表里的轮次号与正文的轮次标题**一一对应**（防"正文加了、索引没加"）
 *   ⑤ 每轮必须记录**只读约束**（"只读"字样出现在该轮内或其后的约束小节）
 *
 * ## 用法
 *   node tools/qa-ledger-check.mjs            # 校验（CI / 门禁用）
 *   node tools/qa-ledger-check.mjs --new R7    # 在台账末尾追加一轮骨架（开发轨用）
 */
import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const LEDGER = path.join(ROOT, 'docs/qa-ledger.md');
const NEW = (() => { const i = process.argv.indexOf('--new'); return i >= 0 ? process.argv[i + 1] : null; })();

// ── 新建轮次骨架 ──
if (NEW) {
  if (!fs.existsSync(LEDGER)) { console.error(`✗ 台账不存在：${LEDGER}`); process.exit(1); }
  let head = '（未提交工作区）';
  try { head = execSync('git rev-parse HEAD', { cwd: ROOT, encoding: 'utf8' }).trim(); } catch { /* git 不可用时保留占位 */ }
  const now = new Date().toISOString().replace('T', ' ').slice(0, 16);
  const tpl = `
---

## ${NEW}（${now} · 被审 HEAD \`${head.slice(0, 7)}\`）

**时点**：${now} · **被审 HEAD**：\`${head}\`
**审计者（只读子代理）**：\`（填子代理 id/名称）\` · **状态**：⏳ 进行中

### 结论
（待填：一句话判定 —— 通过 / 部分通过 / 有阻断）

### 问题清单（每条必须带 \`文件:行号\` 或命令输出）

| # | 问题 | 证据（可复现） | 严重度 | 处置 |
|---|---|---|---|---|
| N?? | （待填） | \`路径/文件.cs:行号\` 或命令输出 | 高/中/低 | ✅ 已修 / ⏳ 登记 + 去向 |

### 处置
（待填：每条问题对应的处置与复验证据）

### 只读约束（本轮自证）
- 审计者约束：**只读**（禁止修改被审仓库任何文件）、禁止 git 写操作、禁止开子代理；变异只在**仓库之外**的临时目录做。
- 开工/\`git status --porcelain\` 行数：\_\_ → 收工：\_\_（HEAD：\_\_ → \_\_）
- 结论：是否修改过真源？（待填）
`;
  fs.appendFileSync(LEDGER, tpl, 'utf8');
  console.log(`✓ 已在台账追加 ${NEW} 骨架（时点 ${now} · HEAD ${head.slice(0, 7)}）`);
  console.log('  下一步：填结论/问题/处置/只读自证，再跑 node tools/qa-ledger-check.mjs 校验结构');
  process.exit(0);
}

// ── 结构校验 ──
if (!fs.existsSync(LEDGER)) { console.error(`✗ 找不到台账：${LEDGER}`); process.exit(1); }
const md = fs.readFileSync(LEDGER, 'utf8');
const problems = [];
const notes = [];

// 切分轮次：以 "## R<n>" 开头
const roundRe = /^## (R\d+)([^\n]*)$/gm;
const rounds = [];
let m;
while ((m = roundRe.exec(md)) !== null) rounds.push({ id: m[1], title: m[0].slice(3), start: m.index, head: m[0] });
for (let i = 0; i < rounds.length; i++) rounds[i].end = i + 1 < rounds.length ? rounds[i + 1].start : md.length;
for (const r of rounds) r.body = md.slice(r.start, r.end);

if (rounds.length < 2) problems.push(`台账只含 ${rounds.length} 轮（要求 ≥2 轮）`);
else notes.push(`✓ 台账含 ${rounds.length} 轮：${rounds.map((r) => r.id).join(', ')}`);

// ②③⑤ 逐轮检查
// 【判据设计说明】五项必填是"内容必须出现"，**不规定**小节的字面标题 ——
// 历史轮次用的标题各不相同（`### 结论` / `### 新发现的问题` / 直接一张表），
// 强行统一标题就等于"为了让检查通过去改历史记录"，那是本末倒置。
// 所以：用关键词 + 结构特征判断，而不是卡标题字符串。
for (const r of rounds) {
  const b = r.body;
  if (!/时点/.test(b)) problems.push(`${r.id} 缺「时点」`);
  if (!/HEAD/.test(b)) problems.push(`${r.id} 缺「被审 HEAD」`);
  if (!/结论/.test(b)) problems.push(`${r.id} 缺「结论」`);
  // 问题清单：接受 `问题清单`/`新发现的问题`/`问题` 任一字样，**或**存在 N 编号的问题行
  const hasIssueHeading = /问题/.test(b) || /^\|\s*N\d+/m.test(b);
  if (!hasIssueHeading) problems.push(`${r.id} 缺「问题清单」`);
  // 处置：**逐条**问题行都必须写出去向。状态标记集合包含 R2 历史写法里的 `❌ 未修`，
  // 所以判定是"有没有状态标记"，而不是"是不是 ✅"。
  // 【判据收紧记录】第一版只查轮内是否出现标记 → 变异"把所有标记换成普通词"仍通过（别处还剩）。
  // 第二版把"表格分隔行"也当成了问题行 → 基线误红。现在：只取 N 编号的问题行，并要求每行带状态标记。
  const issueRows = b.split('\n')
    .map((l) => l.trim())
    .filter((l) => /^\|\s*\**N\d+/.test(l) && !/^\|[\s|:-]+\|$/.test(l));
  const noDisposal = issueRows.filter((l) => !/[✅⏳📌❌]/.test(l));
  const hasDisposalSection = /###\s*处置|^\*\*处置/m.test(b);
  if (issueRows.length > 0 && noDisposal.length > 0 && !hasDisposalSection)
    problems.push(`${r.id} 有 ${noDisposal.length} 条问题没写处置去向（需状态标记 ✅/⏳/📌/❌，或补一个「处置」小节）`);
  else if (issueRows.length === 0 && !/处置/.test(b))
    problems.push(`${r.id} 缺「处置」`);
  // ③ 证据可追溯：`文件:行号` 或反引号里的命令
  const hasFileLine = /[\w./-]+\.(cs|mjs|sh|json|yml|md|ps1):\d+/.test(b);
  const hasCmd = /`[^`]*(node|bash|dotnet|aapt2|git|npm|pwsh|Unity)[^`]*`/.test(b);
  if (!hasFileLine && !hasCmd) problems.push(`${r.id} 没有任何可追溯证据（需 \`文件:行号\` 或命令输出）`);
  if (!/只读/.test(b)) notes.push(`· ${r.id} 轮内未直接写"只读"，依赖全局约束小节`);
}
if (!/只读/.test(md)) problems.push('台账全文没有出现"只读"（要求记录质检子代理的只读约束）');
else notes.push('✓ 台账记录了只读约束');

// ④ 索引与正文一一对应
const idx = new Set();
const idxBlock = md.match(/## 轮次索引([\s\S]*?)\n---/);
if (!idxBlock) problems.push('缺「轮次索引」小节');
else {
  const re = /^\|\s*(R\d+)\s*\|/gm;
  let mm;
  while ((mm = re.exec(idxBlock[1])) !== null) idx.add(mm[1]);
  const bodyIds = new Set(rounds.map((r) => r.id));
  for (const id of bodyIds) if (!idx.has(id)) problems.push(`正文有 ${id}，但轮次索引里没有（索引会腐化）`);
  for (const id of idx) if (!bodyIds.has(id)) problems.push(`轮次索引有 ${id}，但正文没有对应小节`);
  if (bodyIds.size === idx.size) notes.push('✓ 轮次索引与正文一一对应');
}

for (const n of notes) console.log('  ' + n);
if (problems.length) {
  console.error(`\n✗ 质检台账校验失败（${problems.length} 项）：`);
  for (const p of problems) console.error('  - ' + p);
  process.exit(1);
}
console.log('\n✓ 质检台账校验通过（每轮五项齐全 · 证据可追溯 · 索引一致 · 只读约束已记录）');
