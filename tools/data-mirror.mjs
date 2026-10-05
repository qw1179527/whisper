#!/usr/bin/env node
/**
 * data-mirror.mjs — 真源镜像一致性（管线真源 ↔ Unity 运行时真源）
 *
 * 管线真源（仓库根 data/）：工具消费、可 diff、与 baseline 对拍
 * 运行时真源（unity/Assets/Data/）：Unity 运行时直接读
 * 两者必须逐字节一致，否则出现「改了数值、游戏里没变」这类静默漂移。
 * （design-tokens.json 的一致性同时也在 gen-design-tokens.mjs --check 内校验；
 *   本脚本覆盖其余数据文件，并把全部镜像收在一处便于审阅。）
 *
 * 用法：node tools/data-mirror.mjs [--sync]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PAIRS = [
  ['data/config.json', 'unity/Assets/Data/config.json'],
  ['data/design-tokens.json', 'unity/Assets/Data/design-tokens.json'],
  ['unity/Assets/Levels/asylum_v1.json', 'unity/Assets/Resources/Levels/asylum_v1.json'],
  // APK 出货数据也必须与真源一致（第三轮复核：此前 res/raw 无任何同步/门禁，关卡重写后就过期了）
  ['unity/Assets/Levels/asylum_v1.json', 'native/micprobe/res/raw/asylum_v1.json'],
  ['unity/Assets/Data/asset-manifest.json', 'native/micprobe/res/raw/asset_manifest.json'],
  // Resources/Data：运行时经 Resources.Load 读取的配置与清单（必须与真源一致）
  ['data/config.json', 'unity/Assets/Resources/Data/config.json'],
  ['unity/Assets/Data/asset-manifest.json', 'unity/Assets/Resources/Data/asset-manifest.json'],
  // 注：design-tokens 的一致性另由 tools/gen-design-tokens.mjs --check 负责（含产物自检）
];
const sync = process.argv.includes('--sync');

const problems = [];
const rows = [];
for (const [a, b] of PAIRS) {
  const pa = path.join(ROOT, a);
  const pb = path.join(ROOT, b);
  const ea = fs.existsSync(pa);
  const eb = fs.existsSync(pb);
  if (!ea) { problems.push(`管线真源缺失：${a}`); continue; }
  if (!eb) { problems.push(`运行时镜像缺失：${b}`); continue; }
  const same = fs.readFileSync(pa).equals(fs.readFileSync(pb));
  rows.push({ a, b, same, bytes: fs.statSync(pa).size });
  if (!same) {
    problems.push(`镜像不一致：${a} ↔ ${b}`);
    if (sync) { fs.copyFileSync(pa, pb); console.log(`  ↻ 已同步 ${b}`); }
  }
}
for (const r of rows) console.log(`  ${r.same ? '✓' : '✗'} ${r.a}  (${r.bytes} 字节) ↔ ${r.b}`);
if (problems.length && !sync) {
  console.log(`结果：${problems.length} 个镜像问题 ✗（同步：node tools/data-mirror.mjs --sync）`);
  for (const p of problems) console.log('  ✗ ' + p);
  process.exit(1);
}
if (problems.length && sync) {
  console.log(`结果：已同步 ${problems.length} 处；请重跑本脚本确认 ✓`);
  process.exit(0);
}
console.log(`结果：${rows.length} 对镜像逐字节一致 ✓`);
