// 修 `tools/whisper-model3.mjs` 的肢体生成：单中心椭球 → **三点布局（端点+中点，半径 ×1.6）**。
//
// 迭代记录（元球方案的三次尝试，都靠渲染图判定）：
//   ① 14 珠 + 半径 ×1.45  → 融合了但表面是"珠链"（一节节肿包）
//   ② 40 珠 + 半径 ×1.25  → 珠链仍在（珠间距始终大于融合场宽度）
//   ③ 单中心椭球 + size 拉伸 → **段与段断开，渲染成散球**（size 拉伸不是我要的效果）
//   ④ 三点布局 + 半径 ×1.6：三点间距 = L/2，半径取段半径 ×1.6 → 完全重叠，
//      既无珠链也不会断节。这是"少量大珠子"的正确折中。
import fs from 'node:fs';

const P = 'tools/whisper-model3.mjs';
let s = fs.readFileSync(P, 'utf8');
const old = '    return blob((cx, cy, cz), r * 1.30, 2.0, (sx * squash[0], sy * squash[1], sz * squash[2]))';
const neu = [
  '    # 迭代记录（都靠渲染图判定）：',
  '    #   14 珠 ×1.45 → 融合但表面"珠链"',
  '    #   40 珠 ×1.25 → 珠链仍在',
  '    #   单中心椭球 + size 拉伸 → 段与段断开，渲染成散球',
  '    #   本版 = 三点布局（端点+中点）+ 半径 ×1.6：三点间距 L/2，完全重叠，无珠链不断节',
  '    blob(p0, r0 * 1.60, 2.0, squash)',
  '    blob((cx, cy, cz), r * 1.60, 2.0, squash)',
  '    blob(p1, r1 * 1.60, 2.0, squash)',
].join('\n');
if (s.includes(old)) { s = s.replace(old, neu); fs.writeFileSync(P, s, 'utf8'); console.log('  ✓ 已改为三点布局（半径 ×1.6）'); }
else console.log('  ! 锚点未中');
