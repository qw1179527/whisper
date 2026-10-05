// 落"温度系统"配置（刺骨寒温 = 27 只鬼的证据之一）
//
// 为什么配置先行：本项目的纪律是"数值不得硬编码"（config.note 里写死了这条）。
// V9 总方案与补充方案都没有温度数值，所以这里按恐鬼症的标准机制定：
//   正常房间 ~20°C；鬼所在房间持续降温；降到阈值以下 = 「刺骨寒温」证据成立；
//   温度计是玩家读数的道具（读数 0.5s 刷新、有量程与精度）。
import fs from 'node:fs';

const files = [
  'data/config.json',
  'unity/Assets/Data/config.json',
  'unity/Assets/Resources/Data/config.json',
  'native/csharp-verify/config.json',
];

const TEMPERATURE = {
  _note: 'Temperature system (freezing evidence). V9 and the supplement have no numbers, so these follow the standard ghost-hunting formula: rooms sit near 20C, the ghost room cools down, below the freezing threshold the evidence is confirmed.',
  ambientC: 20.0,          // 远离鬼的房间温度
  ghostRoomC: -2.0,        // 鬼所在房间的**目标**温度（会持续往这里降）
  coolPerSecC: 0.35,       // 鬼房降温速率（°C/秒）—— 从 20 降到 0 约 57 秒
  rewarmPerSecC: 0.12,     // 鬼离开后回暖速率（比降温慢，制造"待久了更冷"的体感）
  freezingThresholdC: 0.0, // 低于此值 = 「刺骨寒温」证据成立
  holdSecondsToConfirm: 3, // 需连续低于阈值这么久才确认（防抖：鬼路过一下不算）
  sampling: {
    intervalSec: 0.5,      // 温度计读数刷新间隔（补充方案第 6 条对通灵盒也要求 0.5s，统一）
    precisionC: 0.1,       // 显示精度
    rangeC: [-10.0, 40.0], // 量程（超出显示为极限值）
    noiseC: 0.15,          // 读数噪声（真实温度计不会稳定到小数位）
  },
  _evidenceNote: '寒温证据成立 = 某房间温度连续 holdSecondsToConfirm 秒低于 freezingThresholdC。',
};

for (const f of files) {
  const j = JSON.parse(fs.readFileSync(f, 'utf8'));
  j.temperature = TEMPERATURE;
  fs.writeFileSync(f, JSON.stringify(j, null, 2) + '\n', 'utf8');
  console.log('  ok ' + f);
}

const c = JSON.parse(fs.readFileSync('data/config.json', 'utf8'));
const t = c.temperature;
console.log('  ambient ' + t.ambientC + 'C -> ghost room ' + t.ghostRoomC + 'C @ ' + t.coolPerSecC + 'C/s');
console.log('  freezing below ' + t.freezingThresholdC + 'C for ' + t.holdSecondsToConfirm + 's');
console.log('  cool-down 20->0 takes ' + ((t.ambientC - t.freezingThresholdC) / t.coolPerSecC).toFixed(1) + 's');
