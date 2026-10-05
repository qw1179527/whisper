// 给现有 3 只怪补"鬼种映射 + 证据三选"，把 config.monsters 与 config.ghosts（27 鬼表）桥接起来
//
// 为什么要这一步：温度机制的降温区间取决于"该鬼是否带**刺骨寒温**证据"
// （用户原件第二节第 3 条：带证据的降到 −5~−8°C，普通的降到 −2~5°C）。
// 而项目现有的 3 只怪（缝匠/低语者/收殓人）没有证据字段 → 温度永远算不出该用哪个区间。
//
// 映射依据（都在 27 鬼表里、且**都带刺骨寒温证据**，同时贴合角色定位）：
//   缝匠 stitcher  → demon  恶魔：高压迫、猎杀阈值最高（70%），与本项目"教学怪（压力型）"定位一致
//   低语者 whisperer → hantu 寒魔：低温更快的速度规则，与"语音猎手"的绕行压迫感相容
//   收殓人 coroner → jinn   巨灵：常驻太平间一带（本项目把它放在 morgue 附近巡逻），巨灵的"开闸加速"提供终局压力
//
// ⚠️ 这三条映射是**我按定位选的**，用户原件没有指定；如果用户要求某个具体鬼种，改这里的 `ghost` 字段即可。
import fs from 'node:fs';

const files = [
  'data/config.json',
  'unity/Assets/Data/config.json',
  'unity/Assets/Resources/Data/config.json',
  'native/csharp-verify/config.json',
];

const MAP = {
  stitcher: { ghost: 'demon', note: '教学怪（压力型）→ 恶魔：猎杀阈值最高、冷却最短' },
  whisperer: { ghost: 'hantu', note: '语音猎手（核心怪）→ 寒魔：低温更快，绕行压迫感' },
  coroner: { ghost: 'jinn', note: '终局压迫（高压怪）→ 巨灵：常驻太平间一带' },
};

const c0 = JSON.parse(fs.readFileSync('data/config.json', 'utf8'));
const roster = c0.ghosts.ghosts;

// 先校验映射的目标鬼种确实存在、且证据是三选
const problems = [];
for (const [id, m] of Object.entries(MAP)) {
  const g = roster[m.ghost];
  if (!g) { problems.push(`${id} → ${m.ghost}：27 鬼表里没有这个 id`); continue; }
  if (!Array.isArray(g.evidence) || g.evidence.length !== 3) problems.push(`${id} → ${m.ghost}：证据不是三条`);
}
if (problems.length) { problems.forEach(p => console.log('  FAIL ' + p)); process.exit(1); }

for (const f of files) {
  const j = JSON.parse(fs.readFileSync(f, 'utf8'));
  for (const [id, m] of Object.entries(MAP)) {
    const mon = j.monsters[id];
    if (!mon) { console.log('  skip ' + f + ' → 没有怪 ' + id); continue; }
    const g = roster[m.ghost];
    mon.ghost = m.ghost;              // 映射到 27 鬼表里的鬼种
    mon.evidence = g.evidence.slice(); // 证据三选（含 freezing 与否由它决定温度区间）
    mon.hasFreezingEvidence = g.evidence.includes('freezing');
    mon._ghostMappingNote = m.note;
  }
  fs.writeFileSync(f, JSON.stringify(j, null, 2) + '\n', 'utf8');
}

const c = JSON.parse(fs.readFileSync('data/config.json', 'utf8'));
let withF = 0;
for (const [id, m] of Object.entries(MAP)) {
  const mon = c.monsters[id];
  const has = mon.hasFreezingEvidence;
  if (has) withF++;
  console.log(`  ${id.padEnd(10)} → ${mon.ghost.padEnd(8)} 证据 [${mon.evidence.join(', ')}] 刺骨寒温=${has}`);
}
console.log(`  → 本局 3 只怪里带刺骨寒温证据的：${withF} 只（决定鬼房降到 [-8,-5] 还是 [-2,5]）`);
