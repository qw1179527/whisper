// 落"鬼房（Ghost Room）"配置 —— 用户 2026-10-04 晚的澄清
//
// 【用户原话】「不是鬼所处的房间就是鬼房，鬼房是一个设定，证据灵球只在鬼房能被看见，
//   游戏途中鬼可能会换鬼房，概率看选择的难度」
//
// 这三条彻底改变了语义（我此前实现成"鬼现在在哪哪就是鬼房"，是错的）：
//   ① 鬼房是**开局选定的一个房间**，是一个**设定**，不随鬼移动而变；
//   ② 鬼房**恒冷**（"这里被设定过了"）—— 与"鬼周围降温"是两件事，可以同时存在；
//   ③ **灵球（orb）只在鬼房可见** —— 这让灵球成为"确认鬼房"的判据；
//   ④ 鬼**可以在途中换鬼房**，概率由难度决定。
//
// 为什么"换鬼房"要有冷却而不是纯概率：用户说"概率看难度"，但没说频率。
// 若每帧掷概率，换个不停就没意义了。所以 = **最小间隔 + 每次到点掷一次概率**，
// 这与猎杀节奏（HuntScheduler）是同一套设计语言：阈值/间隔只是触发条件，实际发生靠概率。
import fs from 'node:fs';

const files = [
  'data/config.json',
  'unity/Assets/Data/config.json',
  'unity/Assets/Resources/Data/config.json',
  'native/csharp-verify/config.json',
];

const GHOST_ROOM = {
  _note: 'Ghost room is a SETTING, not "wherever the ghost currently is". Orbs are only visible in the ghost room. The ghost may relocate mid-match; probability depends on difficulty.',
  orb: {
    visibleOnlyInGhostRoom: true,
    _note: '灵球只在鬼房可见（用户原话）。这是确认鬼房的判据，所以实现上必须与"鬼当前所在房间"分开。',
  },
  coolWhileGhostAway: true,
  _coolNote: '鬼房恒冷（不因为鬼离开而回暖）—— 它是"被设定过的房间"。鬼周围另有独立的瞬时降温。',
  relocate: {
    minIntervalSec: 90,
    _note: '换鬼房的最小间隔：到点后**掷一次概率**决定是否换（不是每帧掷，否则换个不停）。',
    perDifficulty: {
      easy:     { chance: 0.05, label: '简单' },
      normal:   { chance: 0.15, label: '普通' },
      nightmare:{ chance: 0.30, label: '噩梦' },
      insanity: { chance: 0.45, label: '疯狂' },
    },
    defaultDifficulty: 'normal',
  },
  temperature: {
    ghostRoomFloorC: -2.0,
    _note: '鬼房地板温度（未带刺骨寒温证据时）。带证据的下限在 temperature.ghostRoom.floorWithFreezingEvidenceC = -8C。',
  },
};

for (const f of files) {
  const j = JSON.parse(fs.readFileSync(f, 'utf8'));
  j.ghostRoom = GHOST_ROOM;
  fs.writeFileSync(f, JSON.stringify(j, null, 2) + '\n', 'utf8');
}

const c = JSON.parse(fs.readFileSync('data/config.json', 'utf8'));
const g = c.ghostRoom;
console.log('  orb only in ghost room: ' + g.orb.visibleOnlyInGhostRoom);
console.log('  ghost room stays cold when ghost away: ' + g.coolWhileGhostAway);
console.log('  relocate min interval: ' + g.relocate.minIntervalSec + 's');
for (const [k, v] of Object.entries(g.relocate.perDifficulty))
  console.log('    ' + v.label.padEnd(4) + ' (' + k + ') → ' + (v.chance * 100) + '% per roll');
