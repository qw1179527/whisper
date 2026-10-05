#!/usr/bin/env node
/**
 * gen-manifest.mjs — 由 dependency-lock.json 生成 Packages/manifest.json
 *
 * 目的：让"依赖版本锁定"只有一个真源。手改 manifest.json 会被下次生成覆盖；
 * CI 用 --check 模式验证二者一致，防止有人偷偷升版本绕过 16KB 四件套门槛。
 *
 * 用法：node tools/gen-manifest.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const LOCK = path.join(ROOT, 'unity/dependency-lock.json');
const MANIFEST = path.join(ROOT, 'unity/Packages/manifest.json');

const lock = JSON.parse(fs.readFileSync(LOCK, 'utf8'));
const checkOnly = process.argv.includes('--check');

const out = {
  dependencies: Object.fromEntries(Object.entries(lock.packages).sort(([a], [b]) => a.localeCompare(b))),
  note: '本文件由 tools/gen-manifest.mjs 从 unity/dependency-lock.json 生成，请勿手改（CI 以 --check 校验一致性）。',
  scopedRegistries: [],
  thirdPartySlots: Object.fromEntries(
    Object.entries(lock.thirdParty).map(([k, v]) => [k, { version: v.version, slot: v.slot }]),
  ),
};
const want = JSON.stringify(out, null, 2) + '\n';
const have = fs.existsSync(MANIFEST) ? fs.readFileSync(MANIFEST, 'utf8') : null;

if (checkOnly) {
  if (have === want) {
    console.log('[manifest] Packages/manifest.json 与 dependency-lock.json 一致 ✓');
    process.exit(0);
  }
  console.log('[manifest] 不一致 ✗（manifest 被手改或锁文件已更新但未重新生成）');
  process.exit(1);
}
fs.mkdirSync(path.dirname(MANIFEST), { recursive: true });
fs.writeFileSync(MANIFEST, want, 'utf8');
console.log(`[manifest] 生成完成：${Object.keys(out.dependencies).length} 个包 + 第三方槽位 ${Object.keys(out.thirdPartySlots).length} 个`);
