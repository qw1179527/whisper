// 调参：① UV 角度阈值放宽（减少切岛）② low/med 档几何更省。
//
// 实测记录（供以后调优参考）：
//   无 UV  : 网格 9.9k → GLB 83k（≈10×，导出器按面角复制顶点）
//   UV 66° : 网格 ~10k → GLB 13.2k（切岛太多，每岛边界要拆点）
// 目标：**低档 ≤6k 顶点/只**（手机一屏 3~5 只也不掉帧）。
import fs from 'node:fs';

const P = 'tools/whisper-model.mjs';
let s = fs.readFileSync(P, 'utf8');

const rules = [
  // ① UV：阈值放宽到 85°，岛边距减小 → 岛更少 → 拆点更少
  ["bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)",
   "bpy.ops.uv.smart_project(angle_limit=math.radians(85), island_margin=0.005)"],
  // ② 三档全面收紧
  ["  'low':  dict(seg=8,  segBig=12, rings=3, subsurf=1, sphere=10, sphereV=7,  bevelSeg=1),",
   "  'low':  dict(seg=6,  segBig=10, rings=2, subsurf=0, sphere=8,  sphereV=6,  bevelSeg=1),"],
  ["  'med':  dict(seg=10, segBig=16, rings=4, subsurf=1, sphere=14, sphereV=10, bevelSeg=2),",
   "  'med':  dict(seg=8,  segBig=14, rings=3, subsurf=1, sphere=10, sphereV=8,  bevelSeg=1),"],
  ["  'high': dict(seg=14, segBig=22, rings=6, subsurf=2, sphere=20, sphereV=14, bevelSeg=3),",
   "  'high': dict(seg=14, segBig=20, rings=5, subsurf=2, sphere=18, sphereV=12, bevelSeg=2),"],
];
let n = 0;
for (const [a, b] of rules) { if (s.includes(a)) { s = s.replace(a, b); n++; } else console.log('  ! 未匹配：' + a.slice(0, 60)); }
fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 调整 ${n}/${rules.length} 处`);
