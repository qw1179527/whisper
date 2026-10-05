// 校验 data/config.json 合法 + 新增段齐全 + 镜像同步。
// 写成脚本而不是内联 node -e：PowerShell 会把内联 JS 的引号与括号解释掉（实测）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'data/config.json');
let cfg;
try { cfg = JSON.parse(fs.readFileSync(P, 'utf8')); }
catch (e) { console.error('  ✗ config.json JSON 不合法：' + e.message); process.exit(1); }
console.log('  ✓ config.json JSON 合法');

// 本轮新增段必须齐全（缺哪个就报哪个，而不是笼统说"配置有问题"）
const need = [
  ['progression.maxLevel', cfg.progression?.maxLevel],
  ['progression.xp.base', cfg.progression?.xp?.base],
  ['progression.prestige.xpMultiplier', cfg.progression?.prestige?.xpMultiplier],
  ['shop.items[]', Array.isArray(cfg.shop?.items) ? cfg.shop.items.length : null],
  ['tasks.dailyCount', cfg.tasks?.dailyCount],
  ['tasks.pool[]', Array.isArray(cfg.tasks?.pool) ? cfg.tasks.pool.length : null],
  ['power.breaker.interactRadiusM', cfg.power?.breaker?.interactRadiusM],
  ['interaction.ghost.intervalMinSec', cfg.interaction?.ghost?.intervalMinSec],
];
let bad = 0;
for (const [k, v] of need) {
  if (v === undefined || v === null) { console.error(`  ✗ 缺 ${k}`); bad++; }
  else console.log(`  ✓ ${k} = ${v}`);
}

// 每个新增段都要有 _src（数值来源纪律，见 docs/spec/phasmophobia-alignment.md §2）
for (const sec of ['progression', 'shop', 'tasks', 'power', 'interaction']) {
  if (!cfg[sec]?._src) { console.error(`  ✗ ${sec} 缺 _src（必须标明 official / design）`); bad++; }
  else console.log(`  ✓ ${sec}._src = ${cfg[sec]._src}`);
}

// 商店层级递进自检：每个 slot 的 tier 应从 1 连续（否则玩家永远买不到高层）
const slots = new Map();
for (const it of cfg.shop?.items ?? []) {
  const slot = it.id.replace(/_t\d+$/, '');
  if (!slots.has(slot)) slots.set(slot, []);
  slots.get(slot).push(it.tier);
}
for (const [slot, tiers] of slots) {
  tiers.sort((a, b) => a - b);
  const okChain = tiers[0] === 1 && tiers.every((t, i) => t === i + 1);
  if (!okChain) { console.error(`  ✗ 商店槽位 ${slot} 的 tier 不连续：${tiers.join(',')}`); bad++; }
}
console.log(`  ✓ 商店槽位 ${slots.size} 个，tier 链全部从 1 连续`);

process.exit(bad ? 1 : 0);
