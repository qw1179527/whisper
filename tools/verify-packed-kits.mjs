#!/usr/bin/env node
/**
 * verify-packed-kits.mjs — 对**构建产物**核验套件是否真的进包、且字节可解析
 *
 * ## 为什么必须在产物上验，而不是在仓库里验
 * 本项目的头号失效模式是"验证过 ≠ 在产品里"（几何层 / 内容管线 / 怪物实例化 / 理智系统，
 * 栽过四次）。套件这一轮尤其阴：把 GLB 放 `Assets/Resources/` 时，**在产物里搜 `Kits/`
 * 与套件名能搜到** —— 那其实是 `asset-manifest.json` 里的路径字符串，GLB 本体并没进包
 * （`.glb` 被 Unity 当 ModelImporter 资产导入，而 Unity 原生不支持 glTF）。
 * 所以判据只能落在**产物里的真实文件**上。
 *
 * ## 判据（三条，任一不满足 → 非零退出）
 *   ① 产物里存在套件目录（Windows 独立：`*_Data/StreamingAssets/Kits`；Android：APK 解包后的 `assets/Kits`）
 *   ② 清单里每个套件都有对应文件、字节数非零、字节数与清单记录一致
 *   ③ 文件头是合法 GLB（magic/version/length）+ JSON 块可解析 + 与清单 sha256 一致
 *
 * 用法：
 *   node tools/verify-packed-kits.mjs <产物目录或解包后的APK目录>
 *   node tools/verify-packed-kits.mjs build/Android/apk-unzipped
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const target = process.argv[2];
if (!target) { console.error('用法：node tools/verify-packed-kits.mjs <产物目录>'); process.exit(2); }
if (!fs.existsSync(target)) { console.error(`✗ 产物目录不存在：${target}`); process.exit(2); }

const manifest = JSON.parse(fs.readFileSync(path.join(ROOT, 'unity/Assets/Data/asset-manifest.json'), 'utf8'));
const problems = [];

// ── ① 找套件目录 ──
function findKitDir(root) {
  // Windows/Linux 独立构建
  for (const e of fs.readdirSync(root, { withFileTypes: true })) {
    if (e.isDirectory() && e.name.endsWith('_Data')) {
      const c = path.join(root, e.name, 'StreamingAssets', 'Kits');
      if (fs.existsSync(c)) return c;
    }
  }
  // 解包后的 APK / 其它布局
  for (const rel of ['assets/Kits', 'StreamingAssets/Kits', 'Kits']) {
    const c = path.join(root, rel);
    if (fs.existsSync(c)) return c;
  }
  for (const e of fs.readdirSync(root, { withFileTypes: true })) {
    if (!e.isDirectory()) continue;
    const found = findKitDir(path.join(root, e.name));
    if (found) return found;
  }
  return null;
}

const kitDir = findKitDir(target);
console.log(`产物目录：${target}`);
console.log(`套件目录：${kitDir ?? '（未找到）'}`);
if (!kitDir) { console.error('✗ 产物里找不到套件目录（套件没进包）'); process.exit(1); }

// ── ②③ 逐个核验 ──
const sha256 = (b) => crypto.createHash('sha256').update(b).digest('hex');
let ok = 0;
for (const kit of manifest.kits) {
  const f = path.join(kitDir, `${kit.id}.glb`);
  if (!fs.existsSync(f)) { problems.push(`${kit.id}: 产物缺文件 ${path.basename(f)}`); continue; }
  const buf = fs.readFileSync(f);
  if (buf.length === 0) { problems.push(`${kit.id}: 产物里是空文件`); continue; }
  if (kit.bytes && buf.length !== kit.bytes) problems.push(`${kit.id}: 字节数不符（清单 ${kit.bytes} vs 产物 ${buf.length}）`);
  const sha = sha256(buf);
  if (kit.sha256 && sha !== kit.sha256) problems.push(`${kit.id}: sha256 不符（清单 ${kit.sha256.slice(0, 12)} vs 产物 ${sha.slice(0, 12)}）`);

  // GLB 容器有效
  const magic = buf.readUInt32LE(0);
  if (magic !== 0x46546c67) { problems.push(`${kit.id}: 不是 GLB（magic=${magic.toString(16)}）`); continue; }
  const version = buf.readUInt32LE(4), total = buf.readUInt32LE(8);
  if (version !== 2) problems.push(`${kit.id}: glTF 版本 ${version}（应 2）`);
  if (total !== buf.length) problems.push(`${kit.id}: 头长度 ${total} ≠ 实际 ${buf.length}`);
  const jlen = buf.readUInt32LE(12);
  let json = null;
  try { json = JSON.parse(buf.toString('utf8', 20, 20 + jlen)); } catch (e) { problems.push(`${kit.id}: JSON 块解析失败（${e.message}）`); continue; }
  const parts = json.meshes ? json.meshes.length : 0;
  const tris = json.meshes ? json.meshes.reduce((s, m) => s + (m.primitives || []).reduce((t, p) => t + (p.indices != null ? json.accessors[p.indices].count / 3 : 0), 0), 0) : 0;
  if (kit.triangles && tris !== kit.triangles) problems.push(`${kit.id}: 三角面数不符（清单 ${kit.triangles} vs 产物 ${tris}）`);
  console.log(`  ✓ ${kit.id.padEnd(15)} ${buf.length} B · ${parts} 部件 · ${tris} 面 · sha ${sha.slice(0, 12)}`);
  ok++;
}

console.log('');
if (problems.length) {
  console.error(`✗ 套件进包核验失败（${problems.length} 项）：`);
  for (const p of problems) console.error('  - ' + p);
  process.exit(1);
}
console.log(`✓ 套件进包核验通过：${ok}/${manifest.kits.length} 个套件在产物内且字节可解析（sha256 与清单一致）`);
