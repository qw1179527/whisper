#!/usr/bin/env node
/**
 * gen-asmdef.mjs — 从 V9 §13.1 规则表确定性生成七个 ASMDEF
 *
 * V9 §13.1 原文（逐字）：
 *   Core（无外部依赖）→ Gameplay（→Core）· Net（→Core+Gameplay）· Audio（→Core）·
 *   Backend（→Core）· UI（→Core+Gameplay+Audio+Net+Backend）· Analytics（→Core）。
 *   禁止循环依赖：Net 不引用 UI，Gameplay 不引用 Net/Backend。
 *
 * 本脚本是 asmdef 的**唯一生成入口**（手改 asmdef 会被下次生成覆盖）：
 * 规则改一处，七个文件一起对齐，杜绝"某个 asmdef 忘了加引用"这类静默漂移。
 *
 * 用法：node tools/gen-asmdef.mjs [--check]
 *   --check 只比对不写盘（CI 用，防止有人手改 asmdef 绕过规则）
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SCRIPTS = path.join(ROOT, 'unity/Assets/Scripts');

/** V9 §13.1 模块边界规则表（唯一真源） */
export const MODULES = {
  Core: { refs: [] },
  Gameplay: { refs: ['Core'] },
  Net: { refs: ['Core', 'Gameplay'] },
  Audio: { refs: ['Core'] },
  Backend: { refs: ['Core'] },
  UI: { refs: ['Core', 'Gameplay', 'Audio', 'Net', 'Backend'] },
  Analytics: { refs: ['Core'] },
  // 组合根：V9 §13.1 的七模块之外。Boot 需要同时引用 Core（契约）与 Gameplay（Level DSL），
  // 若把它塞进 Core 会破坏「Core 无外部依赖」，故单列一层并声明依赖方向。
  Runtime: { refs: ['Core', 'Gameplay', 'Net', 'Audio', 'Backend'] },
};

const asmdefText = (name, refs) => {
  const lines = [
    '{',
    `  "name": "Whisper.${name}",`,
    `  "rootNamespace": "Whisper.${name}",`,
  ];
  if (refs.length) {
    lines.push('  "references": [');
    lines.push(refs.map((r) => `    { "name": "Whisper.${r}" }`).join(',\n'));
    lines.push('  ],');
  }
  lines.push(
    '  "includePlatforms": [],',
    '  "excludePlatforms": [],',
    '  "allowUnsafeCode": false,',
    '  "overrideReferences": false,',
    '  "precompiledReferences": [],',
    '  "autoReferenced": true,',
    '  "defineConstraints": [],',
    '  "versionDefines": [],',
    '  "noEngineReferences": false',
    '}',
    '',
  );
  return lines.join('\n');
};

const checkOnly = process.argv.includes('--check');
let written = 0;
let drifted = 0;
for (const [name, rule] of Object.entries(MODULES)) {
  const dir = path.join(SCRIPTS, name);
  fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, `Whisper.${name}.asmdef`);
  const want = asmdefText(name, rule.refs);
  const have = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : null;
  if (have === want) continue;
  if (checkOnly) {
    drifted++;
    console.log(`  ✗ ${path.relative(ROOT, file)} 与规则表不一致（--check 模式不写盘）`);
  } else {
    fs.writeFileSync(file, want, 'utf8');
    written++;
    console.log(`  ✓ 写入 Whisper.${name}.asmdef（依赖 ${rule.refs.length} 个）`);
  }
}
if (checkOnly) {
  console.log(drifted === 0 ? `[asmdef] ${Object.keys(MODULES).length} 个 asmdef 与规则表完全一致（七模块 + Runtime 组合根）✓` : `[asmdef] ${drifted} 个文件漂移 ✗`);
  process.exit(drifted === 0 ? 0 : 1);
}
console.log(`[asmdef] 生成完成：新写 ${written} 个，其余已一致（共 ${Object.keys(MODULES).length} 个模块）`);
