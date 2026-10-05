// 把 `tools/gen-ghost-roster-models.mjs` 里 **Python 模板中的非 ASCII 字符**全部去掉。
//
// ## 为什么必须做（血泪根因）
// Blender 在中文 Windows 上**按 GBK 读 `.py` 文件**（不按 UTF-8，也不看 `# -*- coding -*-`）。
// 我模板里写了全角标点（「」≈·→ 等）→ GBK 解出乱码 → 其中乱码字节序列里出现引号类字符
// → **三引号 docstring 被提前截断** → `SyntaxError: unterminated triple-quoted string literal`。
// 我先后两次误判为"反引号截断 JS 模板"和"ASCII 双引号"，都只治了表象。
//
// 正解：**Python 侧一律 ASCII**，中文说明留在 JS 侧注释（JS 是 UTF-8，不受影响）。
// 这条对以后所有"JS 里嵌 Python"的工具都适用。
import fs from 'node:fs';

const P = 'tools/gen-ghost-roster-models.mjs';
let s = fs.readFileSync(P, 'utf8');

const startMark = 'const PY = String.raw`';
const endMark = '\n`;\n';
const a = s.indexOf(startMark);
const b = s.indexOf(endMark, a);
if (a < 0 || b < 0) { console.error('  未找到 PY 模板边界'); process.exit(2); }

const head = s.slice(0, a + startMark.length);
let py = s.slice(a + startMark.length, b);
const tail = s.slice(b);

// 逐行处理：去掉非 ASCII 字符（Python 注释/文档串里的中文会变空，但语法保持有效）
const kept = [];
for (const line of py.split('\n')) {
  // 全角标点直接替换成 ASCII 等价物，避免"空注释"影响可读性
  const map = { '（': '(', '）': ')', '，': ',', '：': ':', '；': ';', '。': '.', '「': '"', '」': '"',
                '≤': '<=', '≥': '>=', '≈': '~', '→': '->', '×': 'x', '·': '-', '—': '-', '"': '"', '"': '"' };
  let out = '';
  for (const ch of line) {
    if (ch.charCodeAt(0) < 128) { out += ch; continue; }
    out += map[ch] ?? '';
  }
  kept.push(out.replace(/[ \t]+$/, ''));
}
py = kept.join('\n');

fs.writeFileSync(P, head + py + tail, 'utf8');
const nonAscii = [...py].filter((c) => c.charCodeAt(0) >= 128).length;
console.log(`  ✓ Python 模板已纯 ASCII（残留非 ASCII 字符 ${nonAscii} 个）`);
