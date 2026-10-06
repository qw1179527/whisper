#!/usr/bin/env node
/**
 * prune-caches.mjs — 清理 GitHub Actions 的陈旧缓存，**保持前缀下只留最近 N 个**。
 *
 * ══════════════════════════════════════════════════════════════════════════════
 * 为什么需要它（2026-10-06 实测）
 * ══════════════════════════════════════════════════════════════════════════════
 * `Library-Android-v2-*` 缓存的 key 含**全部 Assets 的哈希** ⇒ **每次改动都产生一个新条目**，
 * 而每个 **1.76 GB**。实测积累到 6 个 = **10.55 GB**，而 GitHub 每仓库缓存上限就是 **10 GB**。
 *
 * 后果不是"浪费空间"这么轻，而是：**任何新缓存一加进来就会触发 LRU 淘汰**，
 * 把构建用的 Library 缓存挤掉 —— 构建时长直接变差，且**不可预测**。
 * （我第一版想给 Unity 编辑器加缓存，就是被这个上限顶掉的：加 5~8 GB 必须先把这里腾干净。）
 *
 * 策略：同前缀下**只保留最近访问的 N 个**（默认 1）。
 * 为什么留 1 就够：`restore-keys: Library-Android-v2-` 是前缀匹配，命中的是"最近那个"；
 * 更早的状态本来就命中不到精确 key，留着只是占额度。
 *
 * ══════════════════════════════════════════════════════════════════════════════
 * 为什么是 Node 脚本而不是写在 workflow 里
 * ══════════════════════════════════════════════════════════════════════════════
 * 第一版我把清理逻辑直接写进 `run: |` 块，里面嵌了一段**多行的 Python** ——
 * 结果 YAML 解析直接失败（块内换行让 `import json,sys` 被当成新键）。
 * 抽成仓库内脚本后：workflow 只有一行调用、逻辑**可本地测试**、也不必依赖 runner 上有 python3。
 *
 * 用法：
 *   GH_TOKEN=... node tools/prune-caches.mjs --prefix Library-Android-v2- --keep 1
 *   GH_TOKEN=... node tools/prune-caches.mjs --prefix Library-Android-v2- --keep 1 --dry-run
 * 仓库由 GH_REPO 或 GITHUB_REPOSITORY 给出（本机跑时需显式设）。
 */
import fs from 'node:fs';

const argv = process.argv.slice(2);
const argOf = (k, d = null) => { const i = argv.indexOf(k); return i >= 0 && argv[i + 1] ? argv[i + 1] : d; };
const hasFlag = (k) => argv.includes(k);

const PREFIX = argOf('--prefix', 'Library-Android-v2-');
const KEEP = Number(argOf('--keep', '1'));
const DRY = hasFlag('--dry-run');
const REPO = process.env.GH_REPO || process.env.GITHUB_REPOSITORY;
const TOKEN = process.env.GH_TOKEN || process.env.GITHUB_TOKEN;
const API = process.env.GITHUB_API_URL || 'https://api.github.com';

if (!REPO) { console.error('缺 GH_REPO / GITHUB_REPOSITORY'); process.exit(2); }
if (!TOKEN) { console.error('缺 GH_TOKEN / GITHUB_TOKEN'); process.exit(2); }
if (!Number.isFinite(KEEP) || KEEP < 0) { console.error('--keep 必须是非负整数'); process.exit(2); }

const H = {
  Authorization: 'Bearer ' + TOKEN,
  Accept: 'application/vnd.github+json',
  'X-GitHub-Api-Version': '2022-11-28',
};

/** 取全部缓存（分页；`per_page=100` 通常一次够）。 */
async function listCaches() {
  const out = [];
  for (let page = 1; page <= 5; page++) {
    const r = await fetch(`${API}/repos/${REPO}/actions/caches?per_page=100&page=${page}`, { headers: H });
    if (!r.ok) throw new Error(`列举缓存失败 HTTP ${r.status}：${(await r.text()).slice(0, 200)}`);
    const j = await r.json();
    out.push(...(j.actions_caches ?? []));
    if ((j.actions_caches ?? []).length < 100) break;
  }
  return out;
}

const all = await listCaches();
const mine = all.filter((c) => c.key.startsWith(PREFIX))
  // 最近访问的排前面（GitHub 的 LRU 也是按这个维度淘汰）
  .sort((a, b) => String(b.last_accessed_at).localeCompare(String(a.last_accessed_at)));
const keep = mine.slice(0, KEEP);
const drop = mine.slice(KEEP);

const totalMB = (arr) => (arr.reduce((n, c) => n + c.size_in_bytes, 0) / 1e6).toFixed(0);
console.log(`[prune] 仓库 ${REPO} · 前缀 "${PREFIX}" · 命中 ${mine.length} 个（${totalMB(mine)} MB）`);
console.log(`[prune] 保留 ${keep.length} 个（${totalMB(keep)} MB）· 待删 ${drop.length} 个（${totalMB(drop)} MB）`);
for (const c of keep) console.log(`  · 保留 ${c.key.slice(0, 60)}（最近访问 ${c.last_accessed_at}）`);

if (drop.length === 0) { console.log('[prune] 无需清理'); process.exit(0); }
if (DRY) { for (const c of drop) console.log(`  · [dry-run] 将删 ${c.key.slice(0, 60)}`); process.exit(0); }

let n = 0, fail = 0;
for (const c of drop) {
  const r = await fetch(`${API}/repos/${REPO}/actions/caches/${c.id}`, { method: 'DELETE', headers: H });
  if (r.status === 204) { n++; console.log(`  ✓ 已删 ${c.key.slice(0, 60)}`); }
  else { fail++; console.log(`  ✗ 删除 ${c.key.slice(0, 60)} HTTP ${r.status}`); }
}
console.log(`[prune] 结果：删除 ${n} 个 · 失败 ${fail} 个 · 释放约 ${totalMB(drop)} MB`);
// 清理失败**不算任务失败**：它是额度优化，不是构建判据（调用方另有判断）。
process.exit(0);
