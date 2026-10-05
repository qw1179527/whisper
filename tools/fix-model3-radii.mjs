// 修 `whisper-model3.mjs`：元球**半径太细 → 段与段融不到一起**（渲染成散球）。
//
// 关键认知（这条是元球的本质，不是调参技巧）：
//   **元球表面的实际半径 ≈ 元素 radius × 0.5~0.6**（radius 是"影响范围"，不是表面）。
//   所以我表里的 0.036（膝）实际只做出 ~0.02m 的细管，而段长 0.24m → 段间必然断开。
// 修法两条一起上：
//   ① 半径整体调粗（小腿/踝加粗最多，因为原来最细）
//   ② 珠数 3 → 7（间距 L/6，配 1.6 倍半径必然重叠）
import fs from 'node:fs';

const P = 'tools/whisper-model3.mjs';
let s = fs.readFileSync(P, 'utf8');

// ① 半径表：整体调粗，细处加粗更多
const oldR = `  R: { foot: 0.030, ankle: 0.026, knee: 0.036, thigh: 0.052, hip: 0.056, s1: 0.058, s2: 0.062,
       chest: 0.070, shoulder: 0.048, neck: 0.030, head: 0.072, headTop: 0.040,
       arm: 0.024, elbow: 0.021, wrist: 0.016 },`;
const newR = `  // ⚠ 元球的 radius 是"影响范围"，**实际表面半径 ≈ radius × 0.5~0.6**。
  // 所以这些数看着"粗"，做出来才是正常人体粗细。写细了会段段断开（实测渲染成散球）。
  R: { foot: 0.052, ankle: 0.048, knee: 0.060, thigh: 0.078, hip: 0.082, s1: 0.084, s2: 0.090,
       chest: 0.100, shoulder: 0.074, neck: 0.052, head: 0.108, headTop: 0.070,
       arm: 0.046, elbow: 0.042, wrist: 0.034 },`;
if (s.includes(oldR)) { s = s.replace(oldR, newR); console.log('  ✓ 半径表已调粗'); }
else console.log('  ! 半径表锚点未中');

// ② 三点 → 七点
const oldThree = `    blob(p0, r0 * 1.60, 2.0, squash)
    blob((cx, cy, cz), r * 1.60, 2.0, squash)
    blob(p1, r1 * 1.60, 2.0, squash)`;
const newSeven = `    for i in range(7):
        t = i / 6.0
        pp = (p0[0] + (p1[0]-p0[0]) * t, p0[1] + (p1[1]-p0[1]) * t, p0[2] + (p1[2]-p0[2]) * t)
        rr_ = r0 + (r1 - r0) * t
        blob(pp, rr_ * 1.60, 2.0, squash)`;
if (s.includes(oldThree)) { s = s.replace(oldThree, newSeven); console.log('  ✓ 珠数 3 → 7'); }
else console.log('  ! 珠布局锚点未中');

fs.writeFileSync(P, s, 'utf8');
