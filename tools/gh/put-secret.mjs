#!/usr/bin/env node
/**
 * put-secret.mjs — 通过 GitHub API 写入 Actions Secret（无需网页操作）
 *
 * 为什么需要它：GitHub 网页在手机上操作不便（私有仓库未登录时一律 404），
 * 而 Actions secret 必须用 **libsodium crypto_box_seal** 加密后提交
 * （明文提交会得到 422 "improperly encrypted secret"）。
 * 本机没有 libsodium 且 pnpm 在 Android 上装不了依赖，因此把 npm 包
 * （libsodium + libsodium-wrappers）解包后 vendor 在 tools/gh/nm/ 下。
 *
 * 用法：
 *   GITHUB_TOKEN=<token> node tools/gh/put-secret.mjs <owner/repo> <SECRET_NAME> <value>
 *   GITHUB_TOKEN=<token> node tools/gh/put-secret.mjs <owner/repo> --from-file <NAME> <path>
 *
 * 安全纪律：令牌与值都只经参数/环境变量传入，脚本不写任何日志到磁盘，
 *          也不回显值的全文（只打印长度与前 4 字符）。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { sealSecret, sodium } = require(path.join(path.dirname(fileURLToPath(import.meta.url)), 'seal.cjs'));

const [repo, name, ...rest] = process.argv.slice(2);
if (!repo || !name) {
  console.error('用法: GITHUB_TOKEN=... node tools/gh/put-secret.mjs <owner/repo> <NAME> <value|--from-file path>');
  process.exit(2);
}
let value;
if (rest[0] === '--from-file') {
  value = fs.readFileSync(rest[1], 'utf8');
} else {
  value = rest.join(' ');
}
const token = process.env.GITHUB_TOKEN;
if (!token) { console.error('缺 GITHUB_TOKEN 环境变量'); process.exit(2); }

const H = { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'dsh', 'Content-Type': 'application/json' };

await sodium.ready;
const kr = await fetch(`https://api.github.com/repos/${repo}/actions/secrets/public-key`, { headers: H });
if (!kr.ok) { console.error(`取公钥失败 HTTP ${kr.status}`); process.exit(1); }
const key = await kr.json();
const enc = await sealSecret(key.key, value);

const put = await fetch(`https://api.github.com/repos/${repo}/actions/secrets/${name}`, {
  method: 'PUT', headers: H, body: JSON.stringify({ encrypted_value: enc, key_id: key.key_id }),
});
if (put.status === 201) {
  console.log(`  ✓ ${name} 已写入（${repo}）· 值长度 ${value.length} · 前缀 ${String(value).slice(0, 4)}…`);
} else {
  console.error(`  ✗ ${name} 写入失败 HTTP ${put.status}：${(await put.text()).slice(0, 200)}`);
  process.exit(1);
}
