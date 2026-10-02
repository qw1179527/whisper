#!/usr/bin/env node
/**
 * validate-assets.mjs — 资产清单与产物的准入校验（V9 §19.1 C3 / §19.3）
 *
 * 为什么需要：C3 承诺「资产零导入」。但清单原本声明的 5 个资产**文件一个都不存在**
 * （连 sourceRoot 目录都没有）——承诺停留在文档层。现在有两条门禁把这条承诺钉住：
 *   ① 清单里每个套件的 file 必须真实存在（本脚本）；
 *   ② 产物的 sha256 / 字节数必须与清单记录一致（tools/gen-kits.mjs --check）。
 *
 * 另校验：套件 id 唯一、kind 合法、level 用到的 kit 必须在清单内且 kind 匹配。
 * 用法：node tools/validate-assets.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');
const LEVEL = path.join(ROOT, 'unity/Assets/Levels/asylum_v1.json');

const P = [];
const manifest = JSON.parse(fs.readFileSync(MANIFEST, 'utf8'));
const kits = Array.isArray(manifest.kits) ? manifest.kits : [];
if (kits.length === 0) P.push('清单里没有任何套件');

const srcRoot = path.join(ROOT, 'unity', manifest.sourceRoot ?? 'Assets/ThirdParty/CC0');
if (!fs.existsSync(srcRoot)) P.push(`sourceRoot 不存在：${manifest.sourceRoot}（"资产零导入"的前提是目录落地）`);

const VALID_KINDS = new Set(['room', 'prop']);
const seen = new Map();
for (const k of kits) {
  if (!k.id) { P.push('存在无 id 的套件'); continue; }
  if (seen.has(k.id)) P.push(`套件 id 重复：${k.id}`);
  seen.set(k.id, k);
  if (!VALID_KINDS.has(k.kind)) P.push(`套件 ${k.id} 的 kind 非法：${k.kind}（合法：room/prop）`);
  if (!k.file) P.push(`套件 ${k.id} 缺 file`);
  else {
    const f = path.join(srcRoot, k.file);
    if (!fs.existsSync(f)) P.push(`套件 ${k.id} 的文件不存在：${k.file}`);
    else if (fs.statSync(f).size === 0) P.push(`套件 ${k.id} 的文件为空：${k.file}`);
  }
  if (!Array.isArray(k.tags) || k.tags.length === 0) P.push(`套件 ${k.id} 缺 tags`);
  if (!k.addressablesGroup) P.push(`套件 ${k.id} 缺 addressablesGroup（V9 §19.3 要求按组入库）`);
}

// 关卡引用的 kit 必须存在于清单且 kind 匹配
let levelRoomRefs = 0, levelPropRefs = 0;
if (fs.existsSync(LEVEL)) {
  const lvl = JSON.parse(fs.readFileSync(LEVEL, 'utf8'));
  for (const r of lvl.rooms ?? []) {
    levelRoomRefs++;
    const k = seen.get(r.kit);
    if (!k) P.push(`房间 ${r.id} 的 kit \`${r.kit}\` 不在清单内`);
    else if (k.kind !== 'room') P.push(`房间 ${r.id} 的 kit \`${r.kit}\` 的 kind=${k.kind}，房间必须用 kind=room`);
    for (const pr of r.props ?? []) {
      levelPropRefs++;
      const pk = seen.get(pr.kit);
      if (!pk) P.push(`房间 ${r.id} 的道具 kit \`${pr.kit}\` 不在清单内`);
      else if (pk.kind !== 'prop') P.push(`房间 ${r.id} 的道具 kit \`${pr.kit}\` 的 kind=${pk.kind}，道具必须用 kind=prop`);
    }
  }
} else P.push(`关卡文件不存在：${path.relative(ROOT, LEVEL)}`);

// 管线入口必须与代码侧一致（C3：CI editor batchmode 入口）
const entry = manifest.contentPipeline?.entry;
if (!entry) P.push('contentPipeline.entry 缺失（CI 资产管线的入口）');
else if (!entry.startsWith('Whisper.Editor.')) P.push(`contentPipeline.entry 命名空间可疑：${entry}`);

// 记录完整性：产物必须带 sha256/bytes（由 gen-kits.mjs 回写）
for (const k of kits) if (!k.sha256 || !k.bytes) P.push(`套件 ${k.id} 缺 sha256/bytes 记录（跑 tools/gen-kits.mjs 回写）`);

console.log('[assets] 资产清单准入校验（V9 §19.1 C3 / §19.3）');
console.log(`  套件 ${kits.length} 个（room ${kits.filter((k) => k.kind === 'room').length} / prop ${kits.filter((k) => k.kind === 'prop').length}）` +
  ` · 关卡引用 房间 ${levelRoomRefs} / 道具 ${levelPropRefs} · 管线入口 ${entry ?? '(缺)'}`);
if (P.length) {
  console.log(`  结果：${P.length} 个问题 ✗`);
  for (const p of P) console.log('  ✗ ' + p);
  process.exit(1);
}
console.log('  结果：清单完整、产物落地、引用与 kind 匹配、记录齐备 ✓');
