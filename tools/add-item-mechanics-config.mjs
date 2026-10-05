// 落"道具机制"配置：EMF 五级 + 8 件新增道具的机制参数
//
// 来源：C:\Users\qing_\Desktop\补充.md 第一节（8 件道具 · 含一/二级与耐久）与
//       "补充的 EMF 设定"（五级 + 8% 概率 + 猎杀时 1~5 随机且判定不准）。
// 数值一律进配置：本工程纪律是"代码不得硬编码数值"（config.note 写死了这条）。
import fs from 'node:fs';

const files = [
  'data/config.json',
  'unity/Assets/Data/config.json',
  'unity/Assets/Resources/Data/config.json',
  'native/csharp-verify/config.json',
];

const EMF = {
  _note: 'EMF five levels (supplement section "EMF setting"). Ghost interaction in range -> level 1-4 alert; a ghost WITH the emf5 evidence has an 8% chance to spike to level 5; during a hunt ANY ghost makes it read 1-5 randomly, and readings then are UNRELIABLE by design (they must not count as evidence).',
  level5ChanceWithEvidence: 0.08,
  lowLevelMin: 1,
  lowLevelMax: 4,
  readingHoldSeconds: 3.0,
  _reliabilityNote: 'duringHunt=true -> IsReliable=false. The player must read EMF when the ghost is not hunting.',
};

const NEW_ITEMS = {
  _note: 'The 8 added items from supplement section 1. tier1/tier2 differ in durability AND in hand model (the supplement explicitly requires a different model per durability).',
  salt: {
    label: '盐', kind: 'placed',
    tiers: { t1: { label: '罐装盐', uses: 1 }, t2: { label: '袋装盐', uses: 2 } },
    mechanic: 'ghost_walking_over_leaves_trace',
    note: '放置性；鬼经过留痕；撒完道具消失；耐久不同时手上建模不同',
  },
  smudge: {
    label: '圣木', kind: 'consumable',
    burnSeconds: 3,
    effectImmediateOnIgnite: true,
    huntBlockSecondsNormal: 90,      // 未猎杀时点燃 → 重置猎杀时刻，90s 内无法猎杀
    huntBlockSecondsSpirit: 150,     // 魂魄：150s
    huntBlockSecondsDemon: 60,       // 恶魔：仅 60s
    freezeGhostSeconds: 5,           // 猎杀中点燃 → 普通鬼丢失目标并定身 5s
    moroiBlindSeconds: 7.5,
    throwableWhileLit: true,         // 未点燃/点燃都可丢
    note: '点燃瞬间即生效；3 秒内燃烧消失（有动画）；猎杀中在鬼周围点燃 → 普通鬼丢失目标并定身 5s',
  },
  candlestick: {
    label: '烛台', kind: 'carryable',
    ghostBlowOutChance: 0.35,        // 鬼有一定概率吹灭
    relightable: true,
    blocksHuntForYurei: true,        // 幽灵：火焰覆盖区内不猎杀
    blocksHuntForOnryo: true,        // 怨灵：火焰可阻止其猎杀
    note: '可手持可放置；鬼有概率吹灭（互动）；吹灭后可再次点燃',
  },
  motionSensor: {
    label: '运动传感仪', kind: 'placed',
    triggerOnLine: true,             // 判定范围**只有判定线本身**
    infraredVisible: true,           // 红外线在玩家视角可见
    blockedByWalls: true,            // 碰墙不延伸
    reflectsOffMirrors: true,        // 碰镜反弹
    noRefraction: true,              // 不折射
    lineLengthM: 6.0,
    note: '判定范围=判定线本身，直线、不穿墙、不折射；红外线符合物理（碰墙止、碰镜反弹）',
  },
  sanityMeds: {
    label: '理智回复药', kind: 'consumable',
    tiers: { t1: { label: '理智药', restore: 15 }, t2: { label: '肾上腺素', restore: 30 } },
    animationVisibleToOthers: true,
    note: '一级 +15，二级 +30；使用有动画且其他玩家可见',
  },
  spiritBox: {
    label: '通灵盒', kind: 'carryable',
    refreshSeconds: 0.5,             // 0.5s 刷新一次状态
    responseChance: 0.05,            // 5% 概率收到回复
    requiresMicVolume: true,
    onlyUserHearsResponse: true,     // 仅使用者能听到
    toggleByViewKey: true,           // 查看键开/关
    note: '手握 + 查看键 → 屏幕提示开始使用；麦克风检测音量启动判定；仅使用者能听到回复',
  },
  ghostWriting: {
    label: '鬼魂笔记', kind: 'placed',
    writeChance: 0.08,               // 8% 概率写下
    writtenModelDiffers: true,       // 写过/未写过建模不同
    writingSoundRadiusM: 8.0,        // 声音仅一定范围内玩家可闻
    floatingPen: true,               // 笔是悬空写的
    note: '有该证据的鬼经过时 8% 概率写下；写过/未写过建模不同；写字音效 + 悬空笔动作',
  },
  dotsProjector: {
    label: '点阵投影仪', kind: 'placed',
    grabableByOthers: true,
    note: '放置性；可直接放地上后被其他玩家拿取（补充方案第 151 行明确允许）',
  },
  crucifix: {
    label: '十字架', kind: 'placed',
    blocksHuntRadiusM: 3.0,
    charges: 2,
    note: '防护装备；Gallu 会因使用防护装备而暴怒',
  },
  thermometer: {
    label: '温度计', kind: 'carryable',
    note: '读房间温度；读数参数在 config.temperature.sampling',
  },
  uvLight: {
    label: '紫外线', kind: 'carryable',
    note: '照出紫外线证据（指纹/脚印）',
  },
  camera: {
    label: '相机', kind: 'carryable',
    note: '拍照取证；Phantom 拍照后暂时消失',
  },
};

const CARRY = {
  _note: 'Carry rules (supplement line 151): max 3 props + 1 head-mounted per player. Head-mounted gear can NOT be dropped - it can only be returned to the van.',
  maxProps: 3,
  maxHeadMounted: 1,
  headMountedCannotBeDropped: true,
  headMountedReturnToVanOnly: true,
  droppableAndPickableByAnyone: true,
  placedAndOneTimeItemsExcluded: true,
};

for (const f of files) {
  const j = JSON.parse(fs.readFileSync(f, 'utf8'));
  j.items = j.items ?? {};
  j.items.emf = EMF;
  j.items.roster = NEW_ITEMS;
  j.items.carry = CARRY;
  fs.writeFileSync(f, JSON.stringify(j, null, 2) + '\n', 'utf8');
  console.log('  ok ' + f);
}

const c = JSON.parse(fs.readFileSync('data/config.json', 'utf8'));
console.log('  emf: level5=' + (c.items.emf.level5ChanceWithEvidence * 100) + '% low=' +
  c.items.emf.lowLevelMin + '-' + c.items.emf.lowLevelMax + ' hold=' + c.items.emf.readingHoldSeconds + 's');
console.log('  roster: ' + Object.keys(c.items.roster).filter(k => !k.startsWith('_')).length + ' entries');
console.log('  carry: max ' + c.items.carry.maxProps + ' props + ' + c.items.carry.maxHeadMounted + ' head-mounted');
