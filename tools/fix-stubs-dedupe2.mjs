// 删掉 `native/unity-stubs/UnityStubs.cs` 里**重复的桩成员**（我上一轮补桩时重复添加）。
//
// 重复的后果不只是两行冗余：`Color.white` 变成**二义性**（CS0229），会把 GameBootstrap /
// HudBuilder / PlayerController / MonsterViews 全部连带报错 —— 看起来像"到处都坏了"，
// 实际只有一个根因。教训：**补桩前先确认桩里有没有**（`grep` 一下再动手）。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
const lines = fs.readFileSync(P, 'utf8').split('\n');

// 要删的**精确行**（按内容匹配，行号会漂移）
const dropExact = [
  '        public static Color black => new Color(0f, 0f, 0f);',
  '        public static Color white => new Color(1f, 1f, 1f);',
  '        public static Color red30 { get { return new Color(1f, 0f, 0f); } }',
  '        /// <summary>整数通道构造（出处 ScriptReference/Color.Color）。</summary>',
];

// GetBuiltinResource：保留**第一处**（带完整注释的那处），删掉后面重复的一处（含其上方注释）
const seen = new Set();
const out = [];
let removed = 0;
let gbrKept = false;
for (let i = 0; i < lines.length; i++) {
  const t = lines[i].trim();
  if (dropExact.includes(lines[i])) { removed++; continue; }
  if (t.startsWith('public static T GetBuiltinResource<T>')) {
    if (!gbrKept) { gbrKept = true; out.push(lines[i]); continue; }
    removed++;
    // 连带删掉紧邻上方的重复注释行（若存在）
    while (out.length && out[out.length - 1].trim().startsWith('///') &&
           out[out.length - 1].includes('GetBuiltinResource')) out.pop();
    continue;
  }
  out.push(lines[i]);
}
fs.writeFileSync(P, out.join('\n'), 'utf8');
console.log(`  ✓ 删除 ${removed} 行重复桩成员（Color.black/white/red30 + 重复的 GetBuiltinResource）`);
