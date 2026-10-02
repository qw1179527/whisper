#!/usr/bin/env node
/**
 * gen-asylum-v1.mjs — 由**布局表**生成 asylum_v1.json（V9 §19.2）
 *
 * 为什么用生成而不是手写：布局的硬约束是几何的（共享墙重合、开口对齐、不重叠），
 * 手写 10 个房间的坐标必错（实测第一版被校验器抓出 7 处几何错误）。
 * 这里把"房间盒 + 门位置"写成表，门的贴墙坐标由几何推导，避免人为算错。
 *
 * 用法：node tools/gen-asylum-v1.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/**
 * 房间盒：{id, x0,x1, z0,z1, h, floor, kit, zone, evidence, props, doors}
 * 门用「贴哪面墙 + 沿墙绝对坐标 at」声明；offset 由几何反算，保证与对端开口一致。
 */
const BOXES = [
  // ── ① 入口区（安全教学）：贴主干西墙，门对开于 x=4 ──
  { id: 'entrance_safe', x0: 0, x1: 4, z0: 0, z1: 3, h: 3.5, floor: 0, kit: 'hall_main', zone: 'safe', evidence: false,
    props: [{ kit: 'cabinet_a', pos: [1.5, 0, 0.6], rot: 180 }],
    doors: [{ id: 'd_east', wall: 'east', at: 1.5 }] },

  // ── ② 主干走廊（东西长廊 x=4..18，z=0..3）：承载三个支线的南端 ──
  { id: 'corridor_main', x0: 4, x1: 22, z0: 0, z1: 3, h: 3.0, floor: 0, kit: 'hall_main', zone: 'pressure', evidence: false, props: [],
    doors: [
      { id: 'd_west', wall: 'west', at: 1.5 },
      { id: 'd_n_morgue', wall: 'north', at: 21 },
      { id: 'd_n_link', wall: 'north', at: 4.5 },
    ] },

  // ── ③ 太平间支线：主干北墙 → 地下深处 → 前室（楼梯口即 z=0 共享墙）──
  { id: 'morgue_deep', x0: 20, x1: 22, z0: 3, z1: 6, h: 3.2, floor: 0, kit: 'morgue', zone: 'high-risk', evidence: false, props: [],
    doors: [{ id: 'd_south', wall: 'south', at: 21 }, { id: 'd_north', wall: 'north', at: 21 }] },
  { id: 'morgue_ante', x0: 20, x1: 22, z0: 6, z1: 9, h: 3.2, floor: 0, kit: 'morgue', zone: 'high-risk', evidence: false,
    props: [{ kit: 'cabinet_a', pos: [0, 0, 1.0], rot: 180 }],
    doors: [{ id: 'd_south', wall: 'south', at: 21 }] },

  // ── ④ 竖向连接廊（x=4..8，门位 x=4.5；刻意与住院部走廊在 x 上错开）──
  { id: 'corridor_link', x0: 4, x1: 8, z0: 3, z1: 5, h: 3.0, floor: 0, kit: 'hall_main', zone: 'pressure', evidence: false, props: [],
    doors: [{ id: 'd_south', wall: 'south', at: 4.5 }, { id: 'd_north', wall: 'north', at: 4.5 }] },

  // ── ⑤ 住院部东西走廊（x=4..18，z=5..6；东段伸出连接廊之外 → 与其北门对齐于 x=4.5）──
  { id: 'corridor_ward', x0: 4, x1: 19, z0: 5, z1: 6, h: 3.0, floor: 0, kit: 'hall_main', zone: 'pressure', evidence: false, props: [],
    doors: [
      { id: 'd_south', wall: 'south', at: 4.5 },
      { id: 'd_n1', wall: 'north', at: 5.5 }, { id: 'd_n2', wall: 'north', at: 8.5 },
      { id: 'd_n3', wall: 'north', at: 11.5 }, { id: 'd_n4', wall: 'north', at: 14.5 }, { id: 'd_n5', wall: 'north', at: 17.5 },
    ] },

  // ── ⑥ 5 间病房（北侧一排，门全在南墙，与走廊北门逐一对齐）──
  { id: 'ward_01', x0: 4, x1: 7, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'bed_b', pos: [0, 0, -0.5], rot: 90 }], doors: [{ id: 'd_south', wall: 'south', at: 5.5 }] },
  { id: 'ward_02', x0: 7, x1: 10, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'bed_b', pos: [0, 0, -0.5], rot: 90 }], doors: [{ id: 'd_south', wall: 'south', at: 8.5 }] },
  { id: 'ward_03', x0: 10, x1: 13, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'bed_b', pos: [0, 0, -0.5], rot: 90 }], doors: [{ id: 'd_south', wall: 'south', at: 11.5 }] },
  { id: 'ward_04', x0: 13, x1: 16, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'cabinet_a', pos: [0, 0, 0.8], rot: 270 }], doors: [{ id: 'd_south', wall: 'south', at: 14.5 }] },
  { id: 'ward_05', x0: 16, x1: 19, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'bed_b', pos: [0, 0, -0.5], rot: 90 }], doors: [{ id: 'd_south', wall: 'south', at: 17.5 }] },
];

/** 走廊连接表：两端门必须贴同一条共享墙且开口对齐（几何由 BOXES 的 at 保证） */
const LINKS = [
  ['entrance_safe/d_east', 'corridor_main/d_west', 2.0],
  ['corridor_main/d_n_morgue', 'morgue_deep/d_south', 2.0],
  ['morgue_deep/d_north', 'morgue_ante/d_south', 1.8],
  ['corridor_main/d_n_link', 'corridor_link/d_south', 2.0],
  ['corridor_link/d_north', 'corridor_ward/d_south', 2.0],
  ['corridor_ward/d_n1', 'ward_01/d_south', 1.6],
  ['corridor_ward/d_n2', 'ward_02/d_south', 1.6],
  ['corridor_ward/d_n3', 'ward_03/d_south', 1.6],
  ['corridor_ward/d_n4', 'ward_04/d_south', 1.6],
  ['corridor_ward/d_n5', 'ward_05/d_south', 1.6],
];

// 门宽由连接表给出（走廊宽度即门宽），避免两处各写一遍导致不一致
const DOOR_WIDTH = new Map();
for (const [a, b, w] of LINKS) { DOOR_WIDTH.set(a, w); DOOR_WIDTH.set(b, w); }

const byId = new Map(BOXES.map((b) => [b.id, b]));
const size = (b) => [b.x1 - b.x0, b.h, b.z1 - b.z0];
/**
 * pos = 房间**最小角点**（min corner）—— 与灰盒 `__m4.rect()` 的约定一致：
 *   const [x,,z] = room.pos; x0 = x; z0 = z; x1 = x + w; z1 = z + d
 * 早期我把它写成"中心点"，与灰盒不兼容（LevelBuilder 会整体错位半间房）。灰盒是本项目
 * 唯一"已验证行为"的参照物，故此处按灰盒约定输出。
 */
const pos = (b) => [b.x0, b.z0];
/**
 * 门沿墙位置用**米**（offsetM），与灰盒 `compileWalls` 的 `d.offsetM` 一致：
 * 沿墙起点 = 该边坐标系较小的那一端（西/东墙沿 z 递增，南/北墙沿 x 递增）。
 *
 * 布局表里写的是**门洞中心**的绝对坐标 `at`，这里反算起点并**夹取到墙内**
 * （与灰盒 `compileWalls` 的 `Math.max(0, Math.min(length - w, d.offsetM))` 同一策略）：
 * 窄墙（如 2 米宽的病房南墙）上放 1.6 米门时，中心无法正好落在布局表位置，
 * 必须夹取，否则门洞越界（校验器会拦）。
 */
const offsetMOf = (b, d) => {
  const wallLen = (d.wall === 'north' || d.wall === 'south') ? (b.x1 - b.x0) : (b.z1 - b.z0);
  const w = d.widthM;
  const centerLocal = ((d.wall === 'north' || d.wall === 'south') ? (d.at - b.x0) : (d.at - b.z0));
  const start = Math.max(0, Math.min(wallLen - w, centerLocal - w / 2));
  return Math.round(start * 1000) / 1000;
};

// 门宽先定（走廊宽度即门宽），offsetM 依赖它做夹取
for (const box of BOXES) for (const d of box.doors) d.widthM = DOOR_WIDTH.get(`${box.id}/${d.id}`) ?? 1.2;

const rooms = BOXES.map((b) => ({
  id: b.id,
  pos: pos(b),
  size: size(b),
  rotY: 0,
  floor: b.floor,
  kit: b.kit,
  doors: b.doors.map((d) => ({ id: d.id, wall: d.wall, offsetM: offsetMOf(b, d), widthM: d.widthM ?? 1.2, locked: false })),
  props: b.props,
  evidencePoint: b.evidence,
  lightZone: b.zone,
}));

// 门的绝对坐标表（生成时自校验：对端必须同墙同轴同 at）
const wallOf = (b, d) => {
  switch (d.wall) {
    case 'north': return { axis: 'z', fixed: b.z1, normal: [0, 1], at: d.at };
    case 'south': return { axis: 'z', fixed: b.z0, normal: [0, -1], at: d.at };
    case 'west': return { axis: 'x', fixed: b.x0, normal: [-1, 0], at: d.at };
    case 'east': return { axis: 'x', fixed: b.x1, normal: [1, 0], at: d.at };
    default: throw new Error('bad wall ' + d.wall);
  }
};
const doors = new Map();
for (const b of BOXES) for (const d of b.doors) doors.set(`${b.id}/${d.id}`, { box: b, door: d, wall: wallOf(b, d) });

const corridors = [];
const problems = [];
for (const [a, c, width] of LINKS) {
  const A = doors.get(a), B = doors.get(c);
  if (!A || !B) { problems.push(`门引用不存在：${a} 或 ${c}`); continue; }
  if (A.wall.axis !== B.wall.axis) problems.push(`${a}↔${c} 轴向不同`);
  else if (Math.abs(A.wall.fixed - B.wall.fixed) > 1e-6) problems.push(`${a}↔${c} 不共墙（${A.wall.fixed} vs ${B.wall.fixed}）`);
  else if (A.wall.normal[0] === B.wall.normal[0] && A.wall.normal[1] === B.wall.normal[1]) problems.push(`${a}↔${c} 法向相同（非同面对开）`);
  else if (Math.abs(A.wall.at - B.wall.at) > 1e-6) problems.push(`${a}↔${c} 开口未对齐（${A.wall.at} vs ${B.wall.at}）`);
  corridors.push({ from: A.box.id, to: B.box.id, doorA: a, doorB: c, width });
}
// 房间重叠自检（角点口径：x0..x1 / z0..z1）
for (let i = 0; i < BOXES.length; i++) for (let j = i + 1; j < BOXES.length; j++) {
  const a = BOXES[i], b = BOXES[j];
  if (a.floor !== b.floor) continue;
  const ox = Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0);
  const oz = Math.min(a.z1, b.z1) - Math.max(a.z0, b.z0);
  if (ox > 0.01 && oz > 0.01) problems.push(`房间重叠：${a.id} 与 ${b.id}（${ox.toFixed(2)}×${oz.toFixed(2)}）`);
}
if (problems.length) {
  console.log('[gen-asylum] 布局表自检失败，拒绝写出：');
  for (const p of problems) console.log('  ✗ ' + p);
  process.exit(1);
}

const level = {
  levelId: 'asylum_v1',
  _note: 'V9 §19.2 疗养院（单层 + 地下太平间；入口区安全教学 / 住院区 5 证据点主压力区 / 地下太平间深处撤离点）。',
  _layout: '坐标单位米，XZ 平面；房间 pos=[中心x,中心z] + size=[宽,高,深]，rotY 全 0（轴对齐）。走廊以 doorA/doorB 引用具体门（房间id/门id），两端门必须贴同一条共享墙且开口对齐——由 tools/validate-levels.mjs 强制校验。**本文件由 tools/gen-asylum-v1.mjs 从布局表生成，手改会被覆盖。**',
  _zones: {
    entrance: ['entrance_safe'],
    pressure: ['corridor_main', 'corridor_link', 'corridor_ward', 'ward_01', 'ward_02', 'ward_03', 'ward_04', 'ward_05'],
    deep: ['morgue_deep', 'morgue_ante'],
  },
  rooms,
  corridors,
  events: [
    { type: 'blackout', minute: 6, durationSec: 10, params: { scope: 'ward_zone' }, sanityEffect: -3,
      counterplay: '手电筒照走廊地面确认出口；黑暗持续掉理智，回到安全区（+2/s）可恢复' },
    { type: 'doorlock', minute: 9, durationSec: 20, params: { rooms: ['ward_03', 'ward_04'] }, sanityEffect: -2,
      counterplay: '锁门期间走住院部东西走廊绕行；砸窗会产生 70 强度声纹（半径 20 米），代价明确' },
    { type: 'laugh', minute: 12, durationSec: 6, params: { source: 'morgue_ante' }, sanityEffect: -5,
      counterplay: '笑声期间蹲行（声纹 8/强度、3 米半径）避免叠加暴露；可借声源方位判断怪物大致位置' },
  ],
  extraction: {
    standard: 'entrance_safe',
    deep: 'morgue_deep',
    _note: 'V9 §7 撤离双点制（标准点安全 / 深处点 +30%）。**已知缺口**：morgue_* 在 V9 里是「地下太平间」，但当前 DSL/校验器不支持跨层走廊（stairs）——本版把两间记为 floor:0、靠位置与命名表达「深处」，待 stair 能力实现后改回 floor:-1',
  },
};

const OUT = path.join(ROOT, 'unity/Assets/Levels/asylum_v1.json');
fs.writeFileSync(OUT, JSON.stringify(level, null, 2) + '\n', 'utf8');
console.log(`[gen-asylum] 生成 ${path.relative(ROOT, OUT)}：房间 ${rooms.length} · 走廊 ${corridors.length} · 事件 ${level.events.length}`);
console.log(`[gen-asylum] 布局表自检通过（共享墙/对开法向/开口对齐/无重叠 全部满足）`);
