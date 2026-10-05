// 给 GI 段补回 `float3 bounce = 0;` 声明（上一轮重写时漏了）。
// 用 node 而不是 edit 工具：.shader 是 CRLF，edit 的精确匹配在这份文件上反复失败。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Resources/Shaders/WhisperPostFx.shader');
let s = fs.readFileSync(P, 'utf8');

if (s.includes('float3 bounce = 0;')) {
  console.log('  · bounce 声明已存在');
} else {
  // 在 "DecodeDN(i.uv, n, d);" 之后、GI 注释之前插入声明。
  // 用**行级**处理，避免 CRLF/LF 精确匹配问题。
  const lines = s.split(/\r?\n/);
  let done = false;
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].includes('DecodeDN(i.uv, n, d);') && lines[i + 1] && lines[i + 1].includes('GI 段') === false
        && lines.slice(i + 1, i + 4).some((l) => l.includes('嵌套三元'))) {
      lines.splice(i + 1, 0, '                    float3 bounce = 0;');
      done = true;
      break;
    }
  }
  if (!done) {
    // 兜底：找第一处 "bounce +=" 往上找最近的作用域起点插入
    console.error('  ✗ 没找到插入点，需人工检查');
    process.exit(1);
  }
  s = lines.join('\n');
  fs.writeFileSync(P, s, 'utf8');
  console.log('  ✓ 已插入 float3 bounce = 0;');
}

// 复查：本文件里不能再有嵌套三元（gles3 转译器处理不了）
const nested = [];
s.split(/\r?\n/).forEach((l, i) => {
  const t = l.replace(/\/\/.*$/, '');
  const q = (t.match(/\?/g) || []).length;
  if (q >= 2) nested.push(`L${i + 1}: ${l.trim().slice(0, 90)}`);
});
console.log(nested.length ? '  ✗ 仍有嵌套三元：\n    ' + nested.join('\n    ') : '  ✓ 无嵌套三元（gles3 安全）');
process.exit(nested.length ? 1 : 0);
