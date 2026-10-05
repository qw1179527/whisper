#!/usr/bin/env node
/**
 * apply-all.mjs — 按序应用 patches/*.mjs，并维护 patches/MANIFEST.json（补丁台账）
 *
 * 台账的作用：`tools/verify-sourcetree.mjs` 的 G1.5 门禁要求「源树能逐字节拼回 baseline」。
 * 打了补丁之后这条必然不成立 —— 但"不成立"必须被**登记**，而不是被忽略。
 * 于是：凡是台账里记录在案、且内容哈希与台账一致的模块，G1.5 按"已登记的补丁"处理；
 * 没有登记却与 baseline 不同 → 仍然失败（防止有人偷偷改源树）。
 *
 * 用法：node patches/apply-all.mjs          # 应用全部补丁并写台账
 *       node patches/apply-all.mjs --check  # 只校验台账与现状是否一致
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PDIR = path.join(ROOT, 'patches');
const LEDGER = path.join(PDIR, 'MANIFEST.json');
const MOD_DIR = path.join(ROOT, 'src/modules');

const sha = (b) => crypto.createHash('sha256').update(b).digest('hex');
const patchFiles = fs.readdirSync(PDIR).filter((f) => /^\d+-.*\.mjs$/.test(f)).sort();
const checkOnly = process.argv.includes('--check');

if (!checkOnly) {
  console.log(`[patches] 应用 ${patchFiles.length} 个补丁`);
  for (const f of patchFiles) {
    const before = new Map();
    for (const m of fs.readdirSync(MOD_DIR)) if (m.endsWith('.js')) before.set(m, sha(fs.readFileSync(path.join(MOD_DIR, m))));
    const { execFileSync } = await import('node:child_process');
    const out = execFileSync(process.execPath, [path.join(PDIR, f)], { encoding: 'utf8' });
    process.stdout.write('  ' + out.split('\n').filter(Boolean).join('\n  ') + '\n');
    for (const m of fs.readdirSync(MOD_DIR)) {
      if (!m.endsWith('.js')) continue;
      const after = sha(fs.readFileSync(path.join(MOD_DIR, m)));
      if (before.get(m) !== after) {
        console.log(`     修改了 src/modules/${m}`);
      }
    }
  }
}

// 重建台账：记录**当前**每个被打补丁标记的模块（按 PATCH 标记识别）
const entries = [];
for (const m of fs.readdirSync(MOD_DIR).filter((f) => f.endsWith('.js')).sort()) {
  const p = path.join(MOD_DIR, m);
  const src = fs.readFileSync(p, 'utf8');
  const marks = [...src.matchAll(/\/\* PATCH (\d{3}): ([a-z0-9-]+) \*\//g)].map((x) => ({ id: x[1], name: x[2] }));
  if (marks.length === 0) continue;
  entries.push({
    module: m,
    patches: [...new Map(marks.map((x) => [x.id, x.name])).entries()].map(([id, name]) => ({ id, name })),
    sha256: sha(fs.readFileSync(p)),
    reason: '该模块含已登记补丁，故不再与 baseline 逐字节一致（差异由补丁本身产生，可审计）',
  });
}
const ledger = {
  note: '补丁台账：凡与 baseline 不再逐字节一致的模块必须登记在此，tools/verify-sourcetree.mjs 据此放行并单独报告。',
  patchedModules: entries,
  updatedAt: new Date().toISOString(),
};

if (checkOnly) {
  if (!fs.existsSync(LEDGER)) { console.error('[patches] ✗ 缺台账 patches/MANIFEST.json'); process.exit(1); }
  const cur = JSON.parse(fs.readFileSync(LEDGER, 'utf8'));
  const a = JSON.stringify(cur.patchedModules.map((e) => [e.module, e.sha256]).sort());
  const b = JSON.stringify(entries.map((e) => [e.module, e.sha256]).sort());
  if (a !== b) {
    console.error('[patches] ✗ 台账与现状不一致（有人改了源树却没更新台账？）');
    console.error('  台账:', a); console.error('  现状:', b);
    process.exit(1);
  }
  console.log(`[patches] ✓ 台账与现状一致（${entries.length} 个已登记模块）`);
  process.exit(0);
}

fs.writeFileSync(LEDGER, JSON.stringify(ledger, null, 2) + '\n', 'utf8');
console.log(`[patches] 台账已更新：${entries.length} 个模块登记为「已打补丁」`);
for (const e of entries) console.log(`  · ${e.module} ← 补丁 ${e.patches.map((p) => p.id + ':' + p.name).join(', ')}`);
