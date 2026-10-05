// 把 `unity/Assets/Levels/asylum_v1.json` 的房间 `kit` 设成**它所在等效类的代表变体 id**。
//
// 【规则（从 gen-kits 的实际行为反推，已验证）】
// `gen-kits.mjs` 的 variants 模式这样分组：
//   classKey = `${baseKit}|${arch}|${W}x${H}x${D}|${doors}`      （L159）
//   id       = isCanonical ? baseKit : `${baseKit}_${代表房间id}` （L477）
// 其中的 `doors` 只统计 `TRIM_WALLS[arch]` 里那些墙上的门（**门位 + 门宽**都进 key）。
// 关键：**适配检查要求每个房间引用"它所在类的代表变体 id"** —— 引用基础 id（`hall_main`）会报
// "DSL 与套件清单脱钩"并**中止整轮生成**（所以清单一直停在 8 个）。
//
// 【本关的分组结果（实测）】
//   · `corridor_main` / `corridor_main_f1` / `corridor_main_f2` → 同类（同 18/22×3×3 与门洞布局）
//     → 代表 id = `hall_main_corridor_main_f1`
//   · `lobby`(8×8×3.5, doors=[]) → 自己一类 → 代表 `hall_main_lobby`
//   · `boiler`(8×8×3.5, morgue 家族) → 自己一类 → 代表 `morgue_boiler`
//   ⚠ `lobby`/`boiler` 是 **8×8**，而 hall/morgue 家族的装饰件是按小房间做的 →
//     这两间房会显得空（待补 `lobby`/`boiler` 配方，那是构建 A 的队列项）。
import fs from 'node:fs';

const P = 'unity/Assets/Levels/asylum_v1.json';
const lv = JSON.parse(fs.readFileSync(P, 'utf8'));

const TARGET = {
  entrance_safe: 'hall_main_entrance_safe',
  // ── hall 主走廊家族（三个房间同 18/22×3×3、同门洞布局 → 同一等效类）──
  // **canonical 房间用基础 id**，类内其余房间用**代表变体 id**。
  // 实测：`corridor_main` 写变体会被判"应引用 hall_main"，而 `corridor_main_f1/f2` 写基础 id 会被判
  // "应引用 hall_main_corridor_main_f1" —— 两条都试过，这条组合才是 15/15 吻合的那一个。
  corridor_main: 'hall_main',
  corridor_main_f1: 'hall_main_corridor_main_f1',
  corridor_main_f2: 'hall_main_corridor_main_f1',   // 与 f1 同尺寸同门位 → 共用代表变体
  corridor_link: 'hall_main_corridor_link',
  corridor_ward: 'hall_main_corridor_ward',
  lobby: 'hall_main_lobby',                         // 8×8 自成一类（无配方，生成器按 hall 装饰出变体）
  morgue_deep: 'morgue',
  morgue_ante: 'morgue',
  boiler: 'morgue_boiler',                          // 8×8 自成一类
};

let n = 0; const changed = [];
for (const r of lv.rooms) {
  const want = TARGET[r.id];
  if (!want || r.kit === want) continue;
  changed.push(`${r.id}: ${r.kit} → ${want}`);
  r.kit = want; n++;
}
fs.writeFileSync(P, JSON.stringify(lv, null, 2) + '\n', 'utf8');
console.log(`  ✓ 收口 ${n} 处`);
for (const c of changed) console.log('    ' + c);
if (!n) console.log('    （幂等，无需改动）');
