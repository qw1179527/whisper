#!/usr/bin/env node
/**
 * config-lint.mjs — 配置表的**符号约定与量纲门禁**
 *
 * 为什么需要它：实测踩过 `monsterBehavior.contactSanityLoss = 35`（**正数=损失量**）
 * 与 `sanity.drain.*`（**负数=每秒流失**）约定不一致，导致移植时把"扣理智"写成了"涨理智"
 * （clamp 到 max 后表现为"完全没效果"）。这类错误不会崩、只会静默错，必须有门禁。
 *
 * 用法：node tools/config-lint.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const cfg = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/config.json'), 'utf8'));

const problems = [];
const notes = [];
const get = (p) => p.split('.').reduce((o, k) => (o == null ? undefined : o[k]), cfg);

/** 约定表：路径 → [期望符号, 说明]；符号 'neg' | 'pos' | 'any' */
const RULES = [
  ['sanity.max', 'pos', '理智上限必须为正'],
  ['sanity.drain.lookAtMonsterPerSec', 'neg', '注视怪物的每秒流失必须为负'],
  ['sanity.drain.darknessPerSec', 'neg', '黑暗每秒流失必须为负'],
  ['sanity.drain.allyDeath', 'neg', '队友死亡一次性损耗必须为负'],
  ['sanity.drain.jumpscare', 'neg', '跳吓一次性损耗必须为负'],
  ['sanity.recover.extractionSafeZonePerSec', 'pos', '安全区每秒恢复必须为正'],
  ['sanity.recover.sedative', 'pos', '镇静剂一次性恢复必须为正'],
  ['monsterBehavior.contactSanityLoss', 'pos', '**已知不一致**：此处为「正数=损失量」，与 sanity.drain.* 相反；消费方必须取负'],
  ['monsterBehavior.lostContactSeconds', 'pos', '失联回归秒数必须为正'],
  ['monsterBehavior.investigateArriveRadiusM', 'pos', '到达判定半径必须为正'],
  // 注意：chaseSpeedScale 只在 monsters.* 下（monsterBehavior 无此键）——曾按错路径读，靠 ?? 1.6 兜底掩盖
  ['monsters.stitcher.chaseSpeedScale', 'pos', '追击倍率（per-monster）必须为正'],
  ['monsterBehavior.finalRageWindowBeforeExtractionSec', 'pos', '终局狂暴窗口必须为正'],
  ['network.tickRate', 'pos', 'Tick 率必须为正'],
  ['voiceCalibration.minRuntimeSnrDb', 'pos', '可辨下限 SNR 必须为正'],
  ['voiceCalibration.hysteresisDb', 'pos', '迟滞带必须为正'],
  ['voiceCalibration.clampCeil', 'pos', '归一化安全夹取必须为正'],
];

for (const [p, sign, why] of RULES) {
  const v = get(p);
  if (typeof v !== 'number') { problems.push(`缺数值或类型不对：${p}（${why}）`); continue; }
  if (sign === 'pos' && v <= 0) problems.push(`${p} = ${v}，应为正数（${why}）`);
  if (sign === 'neg' && v >= 0) problems.push(`${p} = ${v}，应为负数（${why}）`);
}
if (get('monsterBehavior.contactSanityLoss') > 0)
  notes.push('monsterBehavior.contactSanityLoss 为正数（损失量语义）——消费方已按机制取负，见 SanitySystem.MonsterContact');

// 刺激源表完整性：强度为正、半径为正或 null
const src = cfg.stimulusSources ?? {};
const srcKeys = Object.keys(src).filter((k) => !k.startsWith('_'));
if (srcKeys.length === 0) problems.push('stimulusSources 为空');
for (const k of srcKeys) {
  const s = src[k];
  if (!(s.intensity > 0)) problems.push(`stimulusSources.${k}.intensity 必须为正`);
  if (s.radiusM != null && !(s.radiusM > 0)) problems.push(`stimulusSources.${k}.radiusM 必须为正或 null`);
}
// 怪物表完整性：三怪齐、阈值与速度为正
const monsters = Object.keys(cfg.monsters ?? {}).filter((k) => !k.startsWith('_'));
if (monsters.length !== 3) problems.push(`monsters 应为 3 个（缝匠/低语者/收殓人），实际 ${monsters.length}`);
for (const m of monsters) {
  const mm = cfg.monsters[m];
  if (!(mm.speedMps > 0)) problems.push(`monsters.${m}.speedMps 必须为正`);
  if (!(mm.hearingThreshold > 0)) problems.push(`monsters.${m}.hearingThreshold 必须为正`);
}
// 理智档位必须覆盖 0..max 且区间不重叠
const bands = get('sanity.bands') ?? [];
for (let i = 0; i < bands.length; i++)
  for (let j = i + 1; j < bands.length; j++)
    if (bands[i].min <= bands[j].max && bands[j].min <= bands[i].max)
      problems.push(`理智档位区间重叠：${bands[i].id}[${bands[i].min},${bands[i].max}] 与 ${bands[j].id}[${bands[j].min},${bands[j].max}]`);

// 事件池 ↔ C# 内建事件类型必须逐字一致（曾因想当然的简名导致「合法事件被判非法」）
{
  const pool = (get('level.eventPool') ?? []).slice().sort();
  const csPath = path.join(ROOT, 'unity/Assets/Scripts/Gameplay/Level/LevelLoader.cs');
  const cs = fs.readFileSync(csPath, 'utf8');
  const m = cs.match(/BuiltinEventTypes = new HashSet<string>\(StringComparer\.Ordinal\)\s*\{([^}]*)\}/);
  if (!m) problems.push('未能在 LevelLoader.cs 中定位 BuiltinEventTypes（门禁失效，请检查实现）');
  else {
    const csTypes = m[1].split(',').map((x) => x.trim().replace(/^"|"$/g, '')).filter(Boolean).sort();
    const onlyCs = csTypes.filter((t) => !pool.includes(t));
    const onlyCfg = pool.filter((t) => !csTypes.includes(t));
    if (onlyCs.length) problems.push(`C# 内建事件类型多出配置里没有的：${onlyCs.join(', ')}`);
    if (onlyCfg.length) problems.push(`配置事件池里有 C# 未内建的：${onlyCfg.join(', ')}`);
    if (!onlyCs.length && !onlyCfg.length) notes.push(`事件类型 ↔ 配置 eventPool 逐字一致（${pool.length} 个）`);
  }
}

console.log('[config-lint] 符号约定与量纲门禁');
console.log(`  规则 ${RULES.length} 条 · 刺激源 ${srcKeys.length} 个 · 怪物 ${monsters.length} 个 · 理智档位 ${bands.length} 档`);
for (const n of notes) console.log('  · ' + n);
if (problems.length) {
  console.log(`  结果：${problems.length} 个问题 ✗`);
  for (const p of problems) console.log('  ✗ ' + p);
  process.exit(1);
}
console.log('  结果：全部通过 ✓（符号约定一致、区间不重叠、表完整性满足）');
