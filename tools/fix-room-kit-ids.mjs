// 把房间 `kit` 收口到**清单里已存在（或按规则一定会被生成）的 id**。
//
// 【为什么需要这个脚本】`gen-kits.mjs` 的 variants 模式按 `(基础套件, 尺寸, 门洞)` **等效类**出套件，
// 且**每个类只有"代表房间"的 id 会真的生成**；同类其余房间若各自引用 `base_<自己id>`，适配检查会报
// "该房间类应引用 <代表 id>"。而 `lobby`/`boiler` 连配方都没有（只有 hall/ward/morgue 三套架构），
// **永远生成不出** `hall_main_lobby` / `morgue_boiler` → 引用了就会卡住整条链。
//
// 【取舍 · 这是"先让链转绿"的临时收口，不是最终形态】
// 代价：`lobby`/`boiler` 是 8×8，而复用的类远小于它 → **这两间房会显得空**（无内墙、无家具）。
// 真正的修法是给生成器补 4 个配方（`stair`/`lift`/`lobby`/`boiler`）——那是构建 A 的队列项，
// 到位后**只改这张表**即可，房间尺寸与门位都不用动。
import fs from 'node:fs';

const P = 'tools/gen-asylum-v1.mjs';
let s = fs.readFileSync(P, 'utf8');

// roomId → 应收口到的 kit id
const TARGET = {
  // hall 家族：canonical 房间用基础 id；同类走廊共用"代表变体"
  entrance_safe: 'hall_main_entrance_safe',   // 4×3，与基础 18×3 不同 → 自己的变体确实会生成
  corridor_main: 'hall_main',                 // 18×3 —— 该类代表
  corridor_link: 'hall_main_corridor_link',   // 4×2 → 自己的变体
  corridor_ward: 'hall_main_corridor_ward',   // 15×3.4 → 自己的变体
  // 二三层走廊与 corridor_main 同尺寸同门位 → **同一等效类**，必须引用同一个代表 id
  corridor_main_f1: 'hall_main_corridor_main_f1',
  corridor_main_f2: 'hall_main_corridor_main_f1',
  // lobby 无配方 → 先用 hall 家族兜住（几何不吻合，见文件头注释）
  lobby: 'hall_main',
  // morgue 家族：canonical 房间用基础 id
  morgue_deep: 'morgue',
  morgue_ante: 'morgue',
  boiler: 'morgue',
};

let n = 0, miss = 0;
for (const [rid, kit] of Object.entries(TARGET)) {
  // 把该房间那一行的 `kit: '...'` 换成目标值
  const re = new RegExp(`(\\{ id: '${rid}',[^\\n]*?kit: ')[^']*(')`);
  if (re.test(s)) { s = s.replace(re, `$1${kit}$2`); n++; }
  else { console.log(`  ! 未匹配 ${rid}`); miss++; }
}

fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 收口 ${n} 处${miss ? ` · 未匹配 ${miss}` : ''}`);
console.log(`  注意：lobby/boiler 是 8×8 而复用类远小于它 → 这两间房会显得空（待补配方）`);
