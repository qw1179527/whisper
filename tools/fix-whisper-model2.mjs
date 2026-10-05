// 给 `tools/whisper-model.mjs` 加**全局质量档**：一处调低所有段数/细分。
//
// 为什么需要一个总开关：我最初把段数/细分写死在每个调用点（seg=14/22、subsurf=2），
// 结果 GLB 顶点数 83k —— 手机游戏一屏 3~5 只就是 40 万顶点，必然掉帧。
// 有了 `--quality` 就能在"看起来够好"和"跑得动"之间一次性权衡，不用逐处改。
import fs from 'node:fs';

const P = 'tools/whisper-model.mjs';
let s = fs.readFileSync(P, 'utf8');

// ① Python 侧：读 QUAL 档，用它替换写死的 seg/subsurf
s = s.replace(
  "CANON, BODY, OUT, TAG = ARGS['canon'], ARGS['body'], ARGS['out'], ARGS['tag']",
  `CANON, BODY, OUT, TAG = ARGS['canon'], ARGS['body'], ARGS['out'], ARGS['tag']
# ── 全局质量档（由 --quality 传）────────────────────────────────────────────
# 手机游戏的顶点预算：**一屏 3~5 只鬼 + 关卡几何**。所以单只鬼目标 3~6k 顶点。
# 段数与细分级别的乘积决定顶点数；这里一处收口，避免逐调用点写死导致 83k 那种事故。
QUAL = ARGS.get('quality', 'med')
Q = {
  'low':  dict(seg=8,  segBig=12, rings=3, subsurf=1, sphere=10, sphereV=7,  bevelSeg=1),
  'med':  dict(seg=10, segBig=16, rings=4, subsurf=1, sphere=14, sphereV=10, bevelSeg=2),
  'high': dict(seg=14, segBig=22, rings=6, subsurf=2, sphere=20, sphereV=14, bevelSeg=3),
}[QUAL]
SEG, SEGBIG, RINGS, SUB, SPH, SPHV, BSEG = (Q['seg'], Q['segBig'], Q['rings'],
                                            Q['subsurf'], Q['sphere'], Q['sphereV'], Q['bevelSeg'])`);

// ② 把所有写死的段数换成档位变量
const swaps = [
  ["def taper_capsule(bm, p0, p1, r0, r1, seg=14, rings=5, bulge=0.0):",
   "def taper_capsule(bm, p0, p1, r0, r1, seg=None, rings=None, bulge=0.0):\n    seg = seg or SEG; rings = rings or RINGS"],
  ["def ribcage(bm, zHi, zLo, rHi, rLo, seg=20, ribs=5, flare=0.0):",
   "def ribcage(bm, zHi, zLo, rHi, rLo, seg=None, ribs=None, flare=0.0):\n    seg = seg or SEGBIG; ribs = ribs or RINGS"],
  ["def finish(o, bevel=0.010, subsurf=2):", "def finish(o, bevel=0.010, subsurf=None):\n    subsurf = SUB if subsurf is None else min(subsurf, SUB)"],
  ["        b = o.modifiers.new('Bevel','BEVEL'); b.width = bevel; b.segments = 2",
   "        b = o.modifiers.new('Bevel','BEVEL'); b.width = bevel; b.segments = BSEG"],
  // 显式传参的调用点：一律交给档位（传 None）
  ["taper_capsule(bm, (0,0,JZ('waistZ')), (0,0,JZ('hipZ')*0.72), JW('waist'), JW('hip')*0.55, seg=20, rings=4)",
   "taper_capsule(bm, (0,0,JZ('waistZ')), (0,0,JZ('hipZ')*0.72), JW('waist'), JW('hip')*0.55)"],
  ["taper_capsule(bm, (0,0,JZ('hipZ')*0.72), (0,0,JZ('hipZ')*0.72-0.30*H), JW('hip')*0.55, JW('hip')*0.10, seg=16, rings=3)",
   "taper_capsule(bm, (0,0,JZ('hipZ')*0.72), (0,0,JZ('hipZ')*0.72-0.30*H), JW('hip')*0.55, JW('hip')*0.10)"],
  ["bmesh.ops.create_uvsphere(bm, u_segments=22, v_segments=16, radius=JW('headR'))",
   "bmesh.ops.create_uvsphere(bm, u_segments=SPH, v_segments=SPHV, radius=JW('headR'))"],
  ["taper_capsule(bm, (0,0,JZ('neckZ')-0.02), (0,0,JZ('headCenterZ')), JW('neck'), JW('neck')*0.92, seg=12, rings=3)",
   "taper_capsule(bm, (0,0,JZ('neckZ')-0.02), (0,0,JZ('headCenterZ')), JW('neck'), JW('neck')*0.92)"],
  ["JW('upperArm')*1.15, JW('elbow'), seg=12, rings=4, bulge=0.06)", "JW('upperArm')*1.15, JW('elbow'), bulge=0.06)"],
  ["JW('elbow'), JW('wrist'), seg=12, rings=4)", "JW('elbow'), JW('wrist'))"],
  ["JW('hand'), JW('hand')*0.42, seg=10, rings=3)", "JW('hand'), JW('hand')*0.42)"],
  ["JW('thigh')*1.10, JW('knee'), seg=12, rings=4, bulge=0.05)", "JW('thigh')*1.10, JW('knee'), bulge=0.05)"],
  ["JW('knee')*taperLegs, JW('ankle')*taperLegs, seg=12, rings=4)", "JW('knee')*taperLegs, JW('ankle')*taperLegs)"],
  ["bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=12, radius=JW('headR')*0.24)",
   "bmesh.ops.create_uvsphere(bm, u_segments=SPH, v_segments=SPHV, radius=JW('headR')*0.24)"],
  ["bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=JW('headR')*0.145)",
   "bmesh.ops.create_uvsphere(bm, u_segments=SPH, v_segments=SPHV, radius=JW('headR')*0.145)"],
  ["bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=JW('headR')*0.058)",
   "bmesh.ops.create_uvsphere(bm, u_segments=SPH, v_segments=SPHV, radius=JW('headR')*0.058)"],
  // finish 的显式 subsurf 参数去掉（交给档位）
  ["finish(new_obj(\"Torso\", bm, mbody), bevel=0.011, subsurf=2)", "finish(new_obj(\"Torso\", bm, mbody), bevel=0.011)"],
  ["finish(hm, bevel=None, subsurf=2)", "finish(hm, bevel=None)"],
  ["finish(new_obj(\"Neck\", bm, mbody), bevel=0.008, subsurf=2)", "finish(new_obj(\"Neck\", bm, mbody), bevel=0.008)"],
  ["finish(new_obj(\"Arm\", bm, mbody), bevel=0.009, subsurf=2)", "finish(new_obj(\"Arm\", bm, mbody), bevel=0.009)"],
  ["finish(new_obj(\"Leg\", bm, mbody), bevel=0.009, subsurf=2)", "finish(new_obj(\"Leg\", bm, mbody), bevel=0.009)"],
  ["parts.append(finish(o, bevel=None, subsurf=1))", "parts.append(finish(o, bevel=None, subsurf=1))"],
  // ③ CLI：--quality
  ["  const height = parseFloat(opt('height', '1.95'));",
   "  const height = parseFloat(opt('height', '1.95'));\n  const quality = opt('quality', 'med');"],
  ["const r = runBlender(PY, { canon: CANON, body: BODIES[body], bodyName: body, out, tag, height });",
   "const r = runBlender(PY, { canon: CANON, body: BODIES[body], bodyName: body, out, tag, height, quality });"],
];
let n = 0;
for (const [a, b] of swaps) {
  if (s.includes(a)) { s = s.replace(a, b); n++; }
  else console.log('  ! 未匹配：' + a.slice(0, 70));
}
fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 应用 ${n}/${swaps.length} 处 · 新增 --quality low|med|high`);
