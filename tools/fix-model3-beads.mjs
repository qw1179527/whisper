// 修 `whisper-model3.mjs` 的肢体：**珠间距必须 ≤ 半径的 1/3**（这才是珠链的唯一判据）。
//
// 完整迭代记录（全部靠渲染图判定，值得留给后人）：
//   14 珠 ×1.45 → 珠链
//   40 珠 ×1.25 → 珠链（因为**半径小的时候** 40 珠在这个段长下仍然间距 > 半径）
//   单椭球 + size 拉伸 → 断节（size 拉伸不符合预期）
//   3 点 ×1.6 → 断节
//   7 点 ×1.6 → 珠链又回来
// 结论：**判据是"珠间距 / 半径"这个比值，不是珠数也不是半径的绝对大小。**
//   比值 > 1   → 珠链
//   比值 ≈ 0.3 → 光滑管
// 所以这里按"段长 / (半径/3)"**自动算珠数**，并对大小腿等细段自动加密。
import fs from 'node:fs';

const P = 'tools/whisper-model3.mjs';
let s = fs.readFileSync(P, 'utf8');

// 找到 link_seg 里的珠循环，改成按半径自动定珠数
const oldLoop = `    for i in range(7):
        t = i / 6.0
        pp = (p0[0] + (p1[0]-p0[0]) * t, p0[1] + (p1[1]-p0[1]) * t, p0[2] + (p1[2]-p0[2]) * t)
        rr_ = r0 + (r1 - r0) * t
        blob(pp, rr_ * 1.60, 2.0, squash)`;
const newLoop = `    # 珠间距 ≤ 半径/3 → 表面连续（比值判据，见文件头迭代记录）
    rmin = max(min(r0, r1), 1e-4)
    steps = max(4, int(L / (rmin / 3.0)))
    steps = min(steps, 90)          # 上限，避免超细段把元素数炸掉
    for i in range(steps + 1):
        t = i / float(steps)
        pp = (p0[0] + (p1[0]-p0[0]) * t, p0[1] + (p1[1]-p0[1]) * t, p0[2] + (p1[2]-p0[2]) * t)
        rr_ = r0 + (r1 - r0) * t
        blob(pp, rr_, 2.0, squash)`;
if (s.includes(oldLoop)) { s = s.replace(oldLoop, newLoop); console.log('  ✓ 珠数改为按半径自动计算'); }
else console.log('  ! 珠循环锚点未中');

fs.writeFileSync(P, s, 'utf8');
