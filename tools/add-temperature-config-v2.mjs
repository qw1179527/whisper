// 重做"温度机制"配置 —— 依据用户 2026-10-04 晚原件的第二节（本文是权威出处）
//
// 与上一版的区别（上一版是我按 Phasmophobia 标准**自拟**的，用户指出不对）：
//   ① 降温是**两档语义**：鬼房（gradual）与鬼周围（更陡），且**鬼在鬼房时降温更快**
//   ② 下限分两类：普通鬼 −2~5°C；**有「刺骨寒温」证据的鬼 −5~−8°C**
//   ③ 玩家周围 < −1°C 时**口吐寒气**（其他玩家可见）
//   ④ 不同天气室温不同，一般 **−1~23°C** 徘徊；8 种天气
//   ⑤ 鬼离开后温度**逐渐回升**到与周围正常区域持平
import fs from 'node:fs';

const files = [
  'data/config.json',
  'unity/Assets/Data/config.json',
  'unity/Assets/Resources/Data/config.json',
  'native/csharp-verify/config.json',
];

const TEMPERATURE = {
  _note: 'Temperature mechanics. AUTHORITATIVE SOURCE: user spec 2026-10-04 late, section 2 (docs/spec/supplement-2026-10-04-late-full.md). Supersedes my earlier Phasmophobia-derived guess.',
  ambient: {
    minC: -1.0,
    maxC: 23.0,
    _note: '不同天气/局数下的正常室温在 -1~23C 徘徊（用户第 7 条）。具体值由 weather 决定。',
  },
  ghostRoom: {
    // 鬼房温度逐渐降低，**降到一个区间**（不是单一值）；区间取决于该鬼是否带「刺骨寒温」证据。
    // 用户原文第 2 条：「所有鬼怪的温度降低机制都会降低到**至少 -2 至 5 摄氏度**」
    //   → 普通鬼的降温区间 = [-2, 5]，取中值附近 0C 作目标，下限 -2C。
    // 用户原文第 3 条：「拥有刺骨寒温证据的鬼怪会降低到 **-5 至 -8 摄氏度**附近」
    //   → 带证据的区间 = [-8, -5]，取 -5C 作目标，下限 -8C。
    // 自检要验的是**区间关系**：带证据的整个区间必须比普通鬼更冷。
    targetC: 0.0,
    floorC: -2.0,
    targetWithFreezingEvidenceC: -5.0,
    floorWithFreezingEvidenceC: -8.0,
    coolPerSecC: 0.35,
    _inRoomCoolMultiplier: 1.6,
    _note: '普通鬼区间 [-2,5]C；带刺骨寒温证据的区间 [-8,-5]C。鬼在鬼房内时降温速度 x1.6（用户第 5 条）。',
  },
  aroundGhost: {
    // 鬼周围的温度比房间更陡（用户第 1、6 条："相比其他地区就是突然降低"）
    radiusM: 3.0,
    coolPerSecC: 0.8,
    _note: '鬼周围 3m 内降温更快 —— 玩家体感是"走到鬼边上突然一冷"，这是找鬼的主要线索之一。',
  },
  rewarmPerSecC: 0.12,
  _rewarmNote: '鬼离开后逐渐回升，直至与周围正常区域持平（用户第 6 条）。比降温慢，制造"待久了更冷"。',
  playerBreathC: -1.0,
  _breathNote: '玩家周围温度 < -1C 时口吐寒气，其他玩家可见（用户第 4 条）。',
  freezingEvidence: {
    thresholdC: 0.0,
    holdSecondsToConfirm: 3.0,
    _note: '「刺骨寒温」证据成立 = 连续低于阈值满 holdSeconds（防抖：鬼路过不算）。',
  },
  sampling: {
    intervalSec: 0.5,
    precisionC: 0.1,
    rangeC: [-15.0, 45.0],
    noiseC: 0.15,
  },
  weather: {
    _note: '8 种天气（用户第 8 条）。baselineC = 该天气下的正常室温；wind 表示是否伴随刮风类事件。',
    clear: { label: '晴天', baselineC: 23.0 },
    cloudy: { label: '阴天', baselineC: 18.0 },
    rain: { label: '下雨', baselineC: 15.0 },
    heavyRain: { label: '暴雨', baselineC: 12.0 },
    snow: { label: '下雪', baselineC: 4.0 },
    sleet: { label: '雨加雪', baselineC: 2.0 },
    hail: { label: '冰雹', baselineC: 0.0 },
    blizzard: { label: '暴雪', baselineC: -1.0 },
  },
  events: {
    _note: '天气之外的环境事件（用户第 9 条）。',
    wind: { label: '刮风', coolsBy: 1.5, _note: '刮风进一步压低室温 1.5C' },
  },
};

for (const f of files) {
  const j = JSON.parse(fs.readFileSync(f, 'utf8'));
  j.temperature = TEMPERATURE;
  fs.writeFileSync(f, JSON.stringify(j, null, 2) + '\n', 'utf8');
}

// 自检：把用户原文的约束逐条验一遍，避免我抄错
const c = JSON.parse(fs.readFileSync('data/config.json', 'utf8'));
const t = c.temperature;
const checks = [];
// 用户第 2 条：普通鬼「至少到 -2 至 5C」→ 整个区间落在 [-2,5]
checks.push(['普通鬼降温区间落在 [-2,5]C', t.ghostRoom.floorC === -2 && t.ghostRoom.targetC >= -2 && t.ghostRoom.targetC <= 5]);
// 用户第 3 条：带证据的「-5 至 -8C 附近」→ 整个区间落在 [-8,-5]
checks.push(['带证据鬼降温区间落在 [-8,-5]C', t.ghostRoom.floorWithFreezingEvidenceC === -8 && t.ghostRoom.targetWithFreezingEvidenceC >= -8 && t.ghostRoom.targetWithFreezingEvidenceC <= -5]);
// 「刺骨寒温更冷」的正确表达：带证据的**区间上界**都比普通鬼的**区间下界**冷
checks.push(['带证据鬼最暖的一端仍比普通鬼最冷的一端冷', t.ghostRoom.targetWithFreezingEvidenceC < t.ghostRoom.floorC]);
checks.push(['玩家吐气阈值 = -1C', t.playerBreathC === -1]);
checks.push(['室温范围 -1~23C', t.ambient.minC === -1 && t.ambient.maxC === 23]);
checks.push(['鬼周围降温比鬼房更陡', t.aroundGhost.coolPerSecC > t.ghostRoom.coolPerSecC]);
checks.push(['鬼在房内降温加速 > 1', t.ghostRoom._inRoomCoolMultiplier > 1]);
checks.push(['回暖比降温慢', t.rewarmPerSecC < t.ghostRoom.coolPerSecC]);
const w = Object.keys(t.weather).filter(k => !k.startsWith('_'));
checks.push(['8 种天气', w.length === 8]);
const baselines = w.map(k => t.weather[k].baselineC);
checks.push(['天气基线都在 -1~23C 内', baselines.every(b => b >= -1 && b <= 23)]);

let bad = 0;
for (const [name, ok] of checks) { console.log('  ' + (ok ? 'ok  ' : 'FAIL') + ' ' + name); if (!ok) bad++; }
console.log('  weather: ' + w.map(k => t.weather[k].label + ' ' + t.weather[k].baselineC + 'C').join(' / '));
console.log(bad === 0 ? '  self-check: OK' : '  self-check: ' + bad + ' FAILED');
process.exit(bad ? 1 : 0);
