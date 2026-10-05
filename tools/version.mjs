#!/usr/bin/env node
/**
 * version.mjs — 版本真源与产物指纹
 *
 * 纪律（对齐定稿里的出口约定）：**自增补丁位**——只有"构件/功能变更"才进位；
 * 本轮为工程化重构、可玩基线不变，故版本停在 0.6.0（与 DSH专用/ 下基线 APK 同名）。
 *
 * 用法：
 *   node tools/version.mjs            # 打印当前版本与源树指纹
 *   node tools/version.mjs --bump patch   # 补丁位 +1（构件/功能变更时用）
 *   node tools/version.mjs --bump minor   # 中位 +1（有感知的行为增量）
 *   node tools/version.mjs --set 0.7.0
 * 版本写在 data/version.json（唯一真源）；打包器从中取版本号写入产物 banner。
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const VFILE = path.join(ROOT, 'data/version.json');

function load() {
  if (!fs.existsSync(VFILE)) {
    return { version: '0.6.0', channel: 'graybox', note: '初始版本（对齐 0.6.0 基线产物）', history: [] };
  }
  return JSON.parse(fs.readFileSync(VFILE, 'utf8'));
}

/** 源树指纹：14 个模块 + 入口的合并哈希（版本进位时留档，便于回溯"哪个版本对应哪份源"） */
function sourceFingerprint() {
  const dir = path.join(ROOT, 'src/modules');
  const files = fs.readdirSync(dir).filter((f) => f.endsWith('.js')).sort();
  const h = crypto.createHash('sha256');
  for (const f of files) {
    h.update(f);
    h.update(fs.readFileSync(path.join(dir, f)));
  }
  const entry = path.join(ROOT, 'src/entry.js');
  if (fs.existsSync(entry)) h.update(fs.readFileSync(entry));
  return h.digest('hex');
}

function bump(v, part) {
  const [maj, min, pat] = v.split('.').map((x) => parseInt(x, 10));
  if (part === 'minor') return `${maj}.${min + 1}.0`;
  if (part === 'major') return `${maj + 1}.0.0`;
  return `${maj}.${min}.${pat + 1}`;
}

const state = load();
const i = process.argv.indexOf('--bump');
const j = process.argv.indexOf('--set');
if (i > 0) {
  const part = process.argv[i + 1] ?? 'patch';
  const next = bump(state.version, part);
  state.history = [...(state.history ?? []), { from: state.version, to: next, reason: part, at: new Date().toISOString(), fingerprint: sourceFingerprint() }];
  state.version = next;
  fs.writeFileSync(VFILE, JSON.stringify(state, null, 2) + '\n', 'utf8');
  console.log(`[version] ${state.history.at(-1).from} → ${next}（${part}）`);
} else if (j > 0) {
  state.version = process.argv[j + 1];
  fs.writeFileSync(VFILE, JSON.stringify(state, null, 2) + '\n', 'utf8');
  console.log(`[version] 设为 ${state.version}`);
} else {
  fs.writeFileSync(VFILE, JSON.stringify(state, null, 2) + '\n', 'utf8');
  console.log(`[version] 当前 ${state.version}（channel=${state.channel}）`);
}

const fp = sourceFingerprint();
console.log(`[version] 源树指纹 ${fp.slice(0, 16)}`);
console.log(`[version] 真源文件 ${path.relative(ROOT, VFILE)}`);
