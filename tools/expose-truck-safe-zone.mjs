#!/usr/bin/env node
/**
 * expose-truck-safe-zone.mjs — 把货车安全区暴露到组合根（单一口径）
 *
 * ## 为什么
 * 上一步给 `GameSession` 接了 `inSafeZone`，但随后发现一个更大的问题：
 * **`GameSession` 全仓零实例化** —— 局内实际跑的是 `GameBootstrap` 自己的简化逻辑。
 * 所以"把 inSafeZone 接上"这件事，如果只改 GameSession，等于改了一段没人跑的死代码。
 *
 * ## 本脚本的选择：**暴露单一口径，而不是再造一套**
 * 在 `GameBootstrap` 上加 `TruckSafeZone` / `PlayerInTruckSafeZone` 两个只读属性，
 * 数据源唯一（`_hall.TruckSafeZone`）。这样：
 * · 撤离条件判定（§8：所有存活玩家都在车内，门与坡道才关闭）能用同一口径；
 * · 将来的理智/怪物判定也走这里，不会出现"三处各算一遍安全区"；
 * · 不引入第二套安全区实现（本项目在 UI 与生成器上已经吃够"两套口径"的苦）。
 *
 * ## 明确不做（留待"接线"那一轮，已在文档标为 P0）
 * 不在这里把 GameSession 塞进 GameBootstrap —— 那是**架构级接线**，
 * 涉及出生点、证据点、怪物实例化、撤离结算多处，必须单独一轮做且有回归测试。
 * 在这里硬塞会把"安全区"这件小事和"整套玩法层接线"耦合在一起，风险与收益不匹配。
 *
 * 用法：node tools/expose-truck-safe-zone.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('PlayerInTruckSafeZone')) { console.log('[expose] 已存在，跳过'); process.exit(0); }

const anchor = '        public string NetRoomCode => LanSession.NetRoomCode;';
const n = raw.split(anchor).length - 1;
if (n !== 1) { console.error(`[expose] ✗ 锚点命中 ${n} 次（应为 1）`); process.exit(1); }

const add = [
  anchor,
  '',
  '        /// <summary>',
  '        /// 货车安全区（世界 AABB）。**唯一数据源**是 `_hall.TruckSafeZone`。',
  '        /// 用户《补充说明》§8：「货车及其周围是安全区：鬼无法进入，在车内不掉理智」。',
  '        /// 暴露在组合根是为了**单一口径** —— 撤离判定、理智保护、怪物寻路都读它，',
  '        /// 不再各自算一遍（本项目在 UI 与关卡生成器上已多次吃过"两套口径"的亏）。',
  '        /// </summary>',
  '        public Bounds TruckSafeZone => _hall != null',
  '            ? _hall.TruckSafeZone',
  '            : new Bounds(new Vector3(0f, -1000f, 0f), Vector3.zero);',
  '',
  '        /// <summary>玩家是否在货车安全区内（§8：安全区不掉理智、鬼进不来）。</summary>',
  '        public bool PlayerInTruckSafeZone',
  '        {',
  '            get',
  '            {',
  '                if (_player == null) return false;',
  '                var b = TruckSafeZone;',
  '                if (b.size.x <= 0f || b.size.z <= 0f) return false;   // 货车未建 → 不保护（fail-safe）',
  '                float dx = Mathf.Abs(_player.X - b.center.x);',
  '                float dz = Mathf.Abs(_player.Z - b.center.z);',
  '                return dx <= b.size.x * 0.5f && dz <= b.size.z * 0.5f;',
  '            }',
  '        }',
].join('\n');

if (!checkOnly) {
  const bak = FILE + '.bak-safeexpose';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, raw.replace(anchor, add), 'utf8');
}
console.log('[expose] 货车安全区已暴露到组合根' + (checkOnly ? '（--check：不写文件）' : ''));
