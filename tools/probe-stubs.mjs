// 逐个符号探测 UnityStubs 是否真的具备程序化贴图所需的 API。
// 不猜：把每个符号单独检查并报告 —— 之前那份脚本用 includes 判整块，条件写错了导致"已齐全"的假结论。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
let s = fs.readFileSync(P, 'utf8');

const NEED = [
  ['struct Color32', 'Color32 类型（SetPixels32 的参数）'],
  ['class Texture2D', 'Texture2D 类'],
  ['SetPixels32', 'Texture2D.SetPixels32'],
  ['public void Apply()', 'Texture2D.Apply() 无参重载'],
  ['public void Apply(bool', 'Texture2D.Apply(bool)'],
  ['enum TextureWrapMode', 'TextureWrapMode'],
  ['wrapMode', 'Texture.wrapMode'],
  ['public static void Destroy', 'Object.Destroy'],
  ['Texture2D(int width, int height, TextureFormat', 'Texture2D 四参构造'],
  ['public static T CreatePrimitive', '占位（不应缺）'],
];

let bad = 0;
for (const [sig, desc] of NEED) {
  const ok = s.includes(sig);
  if (!ok) bad++;
  console.log(`  ${ok ? '✓' : '✗'} ${desc}  [${sig}]`);
}
console.log(bad ? `\n  ✗ 缺 ${bad} 项 —— 语法预检不会报（桩是宽松的），但真 Unity 编译会报 CS 错` : '\n  ✓ 全部具备');
console.log(`  · 文件 ${s.split('\n').length} 行`);
process.exit(bad ? 1 : 0);
