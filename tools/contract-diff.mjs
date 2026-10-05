#!/usr/bin/env node
/**
 * contract-diff.mjs — C# 契约 ↔ Java 契约镜像 逐成员对照（验收第 2 条的机械证据）
 *
 * 为什么需要：契约参考实现只有"逐项一致"才有意义；肉眼对照会漏，必须机械比对。
 * 口径：从两侧源码抽 (成员名, 形态) 集合后双向比对；形态含 属性/方法/事件。
 *
 * 用法：node tools/contract-diff.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const CS_DIR = path.join(ROOT, 'unity/Assets/Scripts/Core/Contracts');
const JAVA_DIR = path.join(ROOT, 'native/contract-mirror/src/com/whisper/mirror');

// 规范化：只做小写化。
// 注意（我踩过的坑）：曾写成 `.replace(/^get|^set/, '')` —— 交替优先级让 `SetLocalMute`
// 被削成 `localmute`，与 Java 的 `setLocalMute` 对不上；属性 getter 在这里也不需要特殊处理。
const norm = (s) => String(s).toLowerCase().replace(/[^a-z0-9]/g, '');

/**
 * 从 C# 接口抽成员 —— 按行分词，不用大正则。
 * 为什么不用一条大正则：字符类里 `\w` 会被当成字面量反斜杠+w，且 `event Action<string, float> X;`
 * 这种带泛型的事件会被"先吞 event 当类型"的模式抢走。逐行处理对这种规整格式更可靠。
 * 支持三种行：属性 `T Name { get; }`、方法 `T Name(...)`、事件 `event Action<...> Name;`。
 */
function csMembers(file) {
  const out = [];
  for (const raw of fs.readFileSync(file, 'utf8').split('\n')) {
    let line = raw.trim();
    if (!line || line.startsWith('//') || line.startsWith('///') || line.startsWith('*') || line.startsWith('/*')) continue;
    if (/^(namespace|interface|class|struct|public|private|internal|protected)\b/.test(line) && !/\bevent\b/.test(line)) continue;
    const kindOf = (l) => (l.includes('(') ? '方法' : '属性');
    const ev = line.match(/^event\s+[^;]*?([A-Za-z_]\w*)\s*;$/);
    if (ev) { out.push({ name: ev[1], kind: '事件' }); continue; }
    const mem = line.match(/^[A-Za-z_][A-Za-z0-9_<>.,?\[\]]*\s+([A-Za-z_]\w*)\s*(\(|\{)/);
    if (mem) { out.push({ name: mem[1], kind: kindOf(line) }); continue; }
    // 属性访问器行（get; / set;）与其它噪声一律跳过
  }
  return out;
}

/** 从 Java 接口抽成员：方法 `T name(...)`；C# 的 event 在 Java 里表达为同名回调方法 */
function javaMembers(file) {
  const out = [];
  for (const raw of fs.readFileSync(file, 'utf8').split('\n')) {
    const line = raw.trim();
    if (!line || line.startsWith('//') || line.startsWith('*') || line.startsWith('/*')) continue;
    if (/^(package|public\s+(interface|final)\b|interface\b)/.test(line) && !line.includes('(')) continue;
    const m = line.match(/^[A-Za-z_][A-Za-z0-9_<>.,?\[\]]*\s+([a-z]\w*)\s*\(/);
    if (m) out.push({ name: m[1], kind: '方法' });
  }
  return out;
}

const pairs = [
  ['INetService.cs', 'INetService.java'],
  ['IVoiceService.cs', 'IVoiceService.java'],
  ['IBackendService.cs', 'IBackendService.java'],
];

let bad = 0;
const rows = [];
for (const [cs, java] of pairs) {
  const a = csMembers(path.join(CS_DIR, cs));
  const b = javaMembers(path.join(JAVA_DIR, java));
  const aset = new Map(a.map((x) => [norm(x.name), x]));
  const bset = new Map(b.map((x) => [norm(x.name), x]));
  const onlyCs = [...aset.keys()].filter((k) => !bset.has(k)).map((k) => aset.get(k).name);
  const onlyJava = [...bset.keys()].filter((k) => !aset.has(k)).map((k) => bset.get(k).name);
  const okPair = onlyCs.length === 0 && onlyJava.length === 0;
  if (!okPair) bad++;
  rows.push({ cs, java, csCount: a.length, javaCount: b.length, onlyCs, onlyJava, ok: okPair });
}

console.log('[contract-diff] C# 契约 ↔ Java 契约镜像');
for (const r of rows) {
  console.log(`  ${r.ok ? '✓' : '✗'} ${r.cs} (${r.csCount} 成员) ↔ ${r.java} (${r.javaCount} 成员)`);
  if (!r.ok) {
    if (r.onlyCs.length) console.log(`      C# 独有（Java 缺）：${r.onlyCs.join(', ')}`);
    if (r.onlyJava.length) console.log(`      Java 独有（C# 缺）：${r.onlyJava.join(', ')}`);
  }
}
console.log(`\n说明：Java 侧把 C# 的 event 表达为回调方法（名字同源），故按规范化名比对。`);
if (bad) {
  console.log(`结果：${bad} 个接口不一致 ✗`);
  process.exit(1);
}
console.log('结果：三接口成员集合双向一致 ✓');
