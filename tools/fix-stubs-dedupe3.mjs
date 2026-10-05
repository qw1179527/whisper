// 再修一次：`Color` 里 `black`/`white` **各出现两次**，上一次只删掉了一行。
// 这次按"**保留首次出现**"处理（与 GetBuiltinResource 同一策略），一次修净。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
const lines = fs.readFileSync(P, 'utf8').split('\n');

const DUP_KEYS = [
  'public static Color black =>',
  'public static Color white =>',
  'public static Color red30',
];
const seen = new Set();
const out = [];
let removed = 0;
for (const line of lines) {
  const t = line.trim();
  const key = DUP_KEYS.find((k) => t.startsWith(k));
  if (key) {
    if (seen.has(key)) { removed++; continue; }   // 第二次出现 → 删
    seen.add(key);
  }
  out.push(line);
}
fs.writeFileSync(P, out.join('\n'), 'utf8');

// 复核：每个键现在必须只出现一次
const final = fs.readFileSync(P, 'utf8');
const counts = DUP_KEYS.map((k) => [k, (final.match(new RegExp(k.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'g')) || []).length]);
console.log(`  ✓ 删除 ${removed} 行；复核：` + counts.map(([k, n]) => `${k.split(' ')[3]}=${n}`).join(' '));
