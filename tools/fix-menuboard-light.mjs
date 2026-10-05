// 菜单板补光定稿：让**纸片被照亮**，而不是只在板面中间留一条光带。
//
// 0.1.62 真机现象：射灯光轴穿过板面中部，但"纸片"分行在上下两侧、没被照到 →
// 观众看到的是"一块灰板 + 中间一条亮带"，读不出"这是菜单板"。
//
// 设计决定：**用三盏近距点光沿板面均匀布置**（左/中/右），每盏负责一片纸片。
// 为什么不用一盏更强的射灯：射灯打在平面上是**一个亮斑**（截图里那条光带就是），
// 要铺满 7.2x3.4m 的板必须把强度拉到会过曝的程度；而三盏近距点光在板前 1m 处
// 每盏覆盖 ~2.6m 宽，叠加后亮度均匀 —— 这正是"展板照明"的真实做法。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs');
let s = fs.readFileSync(P, 'utf8');
const NL = '\n';

const oldBlock = [
  '            // 近距补光：贴在板前 0.9m，保证纸片可读（这是"焦点必须最亮"的保险）',
  '            Light(_root, "MenuFill", new Vector3(0f, by + 0.35f, z + 0.95f), LightType.Point,',
  '                  new Color(0.96f, 0.95f, 0.90f), 5.5f, 7f, false);',
].join(NL);

const newBlock = [
  '            // 展板照明：三盏近距点光沿板面均匀布置（左/中/右）。',
  '            // 依据：0.1.62 真机截图里单盏射灯只在板面中部留下**一条光带**，',
  '            // 而纸片分上下两行、没被照到 → 读不出"这是菜单板"。',
  '            // 射灯打平面本质是**一个亮斑**，要铺满 7.2x3.4m 就得过曝；',
  '            // 三盏近距点光各覆盖约 2.6m，叠加后均匀 —— 这就是真实展板照明的做法。',
  '            for (int k = -1; k <= 1; k++)',
  '            {',
  '                Light(_root, "MenuFill",',
  '                      new Vector3(k * bw * 0.33f, by + 0.30f, z + 1.05f), LightType.Point,',
  '                      new Color(0.97f, 0.96f, 0.92f), 6.0f, 6.5f, false);',
  '            }',
].join(NL);

if (s.includes(oldBlock)) {
  s = s.replace(oldBlock, newBlock);
  fs.writeFileSync(P, s, 'utf8');
  console.log('  ✓ 菜单板改为三盏展板灯');
} else {
  console.log('  ! 锚点未中，需人工检查');
  process.exit(1);
}
