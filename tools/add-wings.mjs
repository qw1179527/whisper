#!/usr/bin/env node
/**
 * add-wings.mjs — 给关卡文件注入 `wing`（分翼）字段。
 *
 * ══════════════════════════════════════════════════════════════════════════════
 * 为什么需要分翼（官方依据，不是我们发明的）
 * ══════════════════════════════════════════════════════════════════════════════
 * `docs/reference-official/04-场景介绍初始界面与地图.md` §7.2 Sunny Meadows 精神病院
 * （**它替代了原来的疯人院 Asylum**）：
 *   · "规模庞大，分为多个**独立分翼**——限制病房 (Restricted Ward)、礼拜堂 (Chapel)、庭院 (Courtyard) 等"
 *   · "**猎杀时所在分翼封锁**，极难躲藏；房间高度相似，**极易迷路**"
 *
 * ⇒ "分翼"不只是一个标签，它**承载一条玩法机制**：猎杀时封锁玩家所在的那一片。
 *   所以每间房都必须登记翼 —— 漏登记 = 那间房永远封不住 = 玩家能从本该封死的翼跑出去。
 *   `LevelLoader` 因此对缺 `wing` 的房间**判红**（而不是默认成"无翼"）。
 *
 * ⚠ 小地图（单栋住宅）**也有翼**：官方小图的风险点是"地下室/阁楼难藏"，
 *   那本质上就是"另一片区域"。所以小图给 `house` + `basement`/`attic` 两个翼，
 *   而不是填一个占位符 —— 占位符会让"封锁"在这两张图上变成空操作。
 *
 * 用法：node tools/add-wings.mjs            # 全部关卡
 *       node tools/add-wings.mjs --check    # 只报告，不写
 */
import fs from 'node:fs';
import path from 'node:path';

const CHECK = process.argv.includes('--check');
const DIR = 'unity/Assets/Levels';

/** 每张图的翼分配规则：按房间 id 匹配，第一个命中的规则生效。 */
const RULES = {
  asylum_v1: [
    [/^corridor_main|^entrance_safe/, 'reception'],
    [/^morgue/, 'morgue'],
    [/^ward_|^corridor_link|^corridor_ward/, 'ward'],
    [/^lobby/, 'chapel'],
    [/^boiler/, 'plant'],
  ],
  tanglewood_v1: [
    [/basement|cellar|地下/i, 'basement'],
    [/./, 'house'],
  ],
  bleasdale_v1: [
    [/attic|阁楼|_f2/i, 'attic'],
    [/basement|cellar|地下/i, 'basement'],
    [/./, 'house'],
  ],
};

const wingOf = (levelId, roomId) => {
  const rules = RULES[levelId];
  if (!rules) return null;
  for (const [re, wing] of rules) if (re.test(roomId)) return wing;
  return null;
};

let changed = 0;
const files = fs.readdirSync(DIR).filter((f) => f.endsWith('.json'));
for (const f of files) {
  const levelId = f.replace(/\.json$/, '');
  const p = path.join(DIR, f);
  const level = JSON.parse(fs.readFileSync(p, 'utf8'));
  const rooms = level.rooms ?? [];
  if (rooms.length === 0) { console.log(`  ${f}: 无房间，跳过`); continue; }

  let n = 0, unresolved = [];
  for (const r of rooms) {
    if (r.wing) { n++; continue; }              // 已有：不覆盖（asylum 由生成器写）
    const w = wingOf(levelId, r.id);
    if (!w) { unresolved.push(r.id); continue; }
    r.wing = w;
    n++;
  }
  const wings = {};
  for (const r of rooms) wings[r.wing] = (wings[r.wing] ?? 0) + 1;

  const status = unresolved.length ? `⚠ 未解析 ${unresolved.length}: ${unresolved.slice(0, 4).join(', ')}` : '✓';
  console.log(`  ${f.padEnd(18)} 房间 ${String(rooms.length).padStart(3)} · 有翼 ${n} · 分翼 ${JSON.stringify(wings)} ${status}`);

  if (!CHECK && n > 0) {
    fs.writeFileSync(p, JSON.stringify(level, null, 2) + '\n');
    changed++;
  }
}
console.log(CHECK ? `[add-wings] 只检查，未写入` : `[add-wings] 已写入 ${changed} 个关卡文件`);
