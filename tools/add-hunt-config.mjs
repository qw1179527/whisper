// 落"猎杀节奏"配置（补充方案 2026-10-04 第三节 · 用户第 150 行明确要求）
//
// 核心语义（必须与实现一致，否则机制又变成"到阈值就疯狂猎杀"）：
//   阈值只是**触发条件**；实际猎杀 = 冷却结束后的**概率判定**，概率与理智负相关。
//   因此存在"有时可以一直不猎杀"的情形 —— 这是设计意图，不是 bug。
import fs from 'node:fs';

const files = [
  'data/config.json',
  'unity/Assets/Data/config.json',
  'unity/Assets/Resources/Data/config.json',
  'native/csharp-verify/config.json',
];

const HUNT = {
  _note: 'Hunt rhythm (supplement 2026-10-04 section 3, user line 150). Threshold is only the trigger; actual hunts are a PROBABILITY roll after cooldown, negatively correlated with sanity. So "sometimes never hunts" is intended behaviour, not a bug.',
  default: {
    minIntervalSec: 60,
    baseChancePerSec: 0.02,
    sanityFactor: 3,
    maxChancePerSec: 0.15,
    huntDurationSec: 25,
    _note: 'Cooldown at least 60s; per-second base chance 2%, multiplied by (1 + sanityFactor * how far below threshold). At threshold expect ~50s, at zero sanity ~7s -- but always probabilistic, never a timer.',
  },
  perMonster: {
    demon: { minIntervalSec: 20, baseChancePerSec: 0.04, _note: 'Demon: mechanism allows as low as 20s, but NOT strictly on that timer (supplement item 4).' },
    spirit: { minIntervalSec: 90, _note: 'Spirit: smudge blocks hunting for 150s (handled by item system); base cooldown also longer.' },
    shade: { minIntervalSec: 75, baseChancePerSec: 0.012, _note: 'Shade: threshold 35%, hunts less often.' },
    onryo: { minIntervalSec: 60, _note: 'Onryo: after its candle is blown out it may hunt ignoring sanity (mechanism slot).' },
  },
  cursed: {
    ignoresSanity: true,
    ignoresCooldown: true,
    smudgeImmune: true,
    _note: 'Cursed hunt: ignores sanity and cooldown and cannot be stopped by smudge (supplement section 5).',
  },
};

for (const f of files) {
  const raw = fs.readFileSync(f, 'utf8');
  const j = JSON.parse(raw);
  j.hunt = HUNT;
  fs.writeFileSync(f, JSON.stringify(j, null, 2) + '\n', 'utf8');
  console.log('  ok ' + f);
}

const check = JSON.parse(fs.readFileSync('data/config.json', 'utf8'));
console.log('  default: ' + check.hunt.default.minIntervalSec + 's / ' + check.hunt.default.baseChancePerSec + ' per sec');
console.log('  perMonster: ' + Object.keys(check.hunt.perMonster).join(', '));
console.log('  cursed ignores sanity+cooldown: ' + check.hunt.cursed.ignoresSanity + '/' + check.hunt.cursed.ignoresCooldown);
