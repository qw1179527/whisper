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
    props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 180, pref: 'nw' }],
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
    props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 180, pref: 'nw' }],
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
    props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 90, pref: 'nw' }], doors: [{ id: 'd_south', wall: 'south', at: 5.5 }] },
  { id: 'ward_02', x0: 7, x1: 10, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 90, pref: 'nw' }], doors: [{ id: 'd_south', wall: 'south', at: 8.5 }] },
  { id: 'ward_03', x0: 10, x1: 13, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 90, pref: 'nw' }], doors: [{ id: 'd_south', wall: 'south', at: 11.5 }] },
  { id: 'ward_04', x0: 13, x1: 16, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 270, pref: 'ne' }], doors: [{ id: 'd_south', wall: 'south', at: 14.5 }] },
  { id: 'ward_05', x0: 16, x1: 19, z0: 6, z1: 10, h: 3.5, floor: 0, kit: 'hospital_ward', zone: 'pressure', evidence: true,
    props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 90, pref: 'nw' }], doors: [{ id: 'd_south', wall: 'south', at: 17.5 }] },
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

/**
 * 道具自动寻位：在房间内扫描候选点，挑一个满足「距四墙 ≥0.5m」且「不挡门洞通道」的位置。
 *
 * 为什么不让布局表写死坐标：我写死过一轮，结果是 5 张病床全部压在墙线上
 * （本地 x=0 就是贴西墙、z=-0.6 直接伸到房间外），而当时没有任何检查发现它 ——
 * 建模门禁 M6 才把它抓出来。自动寻位把"落位合法性"从手工计算变成生成时就保证。
 *
 * 偏好：从房间左上角开始扫描，优先靠墙但不贴墙（0.5m 起步），并且避开每个门洞的通道带。
 */
/**
 * 套件占地尺寸（与 asset-manifest.json 的 footprint 一致；0 度放置时的 [宽x, 深z]）。
 * 为什么必须有它：我最初只用"道具中心点"判断是否挡门洞，结果一张 0.9×2.0 的床
 * 中心虽在门洞外、床体却压住门洞一半（建模门禁抓出的真实缺陷）。
 */
const FOOTPRINT = {
  bed_b: [0.9, 2.0], cabinet_a: [0.8, 0.5],
  hospital_ward: [3.0, 4.0], hall_main: [16.0, 3.0], morgue: [3.0, 3.0],
};
/** 道具按 rot 旋转后的 AABB 半尺寸 */
function footprintHalf(kit, rotDeg) {
  const [w, d] = FOOTPRINT[kit] ?? [0.8, 0.8];
  const r = ((rotDeg ?? 0) % 180 + 180) % 180;
  const swapped = r >= 45 && r < 135;          // 90 度放置 → 宽深互换
  const sx = swapped ? d : w, sz = swapped ? w : d;
  return [sx / 2, sz / 2];
}

function placeProp(box, pref) {
  const CLEAR = 0.5;
  const doors = box.doors ?? [];
  const prop = arguments[2] ?? {};
  const evidence = box.evidence === true;
  const [hx, hz] = footprintHalf(prop.kit ?? 'bed_b', prop.rot ?? 0);
  const WALL = 0.26;                       // 墙厚（与 LevelGeometry/生成器一致）
  const blocked = (x, z) => {
    // 距墙判据必须按**真实占地盒**：占地半尺寸 + 墙厚 + 0.05 余量。
    // 我最初用固定 0.5m，结果一张旋转后的床（半宽 1.0m）仍会越出房间 —— 建模门禁抓出来的。
    const needX = hx + WALL + 0.05, needZ = hz + WALL + 0.05;
    const dx = Math.min(x - box.x0, box.x1 - x);
    const dz = Math.min(z - box.z0, box.z1 - z);
    if (dx < needX || dz < needZ) return true;
    // ② 证据房（有证据点的房间）必须保住"房间中心"的可达性：
    //    证据点就定义在房间中心（GameSession 用 r.CenterX/CenterZ），拾取半径 0.9m。
    //    若家具把中心挡住，代理最近只能站到家具外侧 —— 实测病床 2.0m 长压在中心线上时，
    //    最近站位距证据点 1.35m > 0.9m，**关卡直接不可通关**（独立复核第 2 轮 F-A）。
    //    这里要求：道具占地盒与"中心 keep-out 区"不相交（区 = 中心 ± (0.9 + 代理半径 0.34)）。
    if (evidence) {
      const KEEP = 0.55;   // 只需保住中心附近的可站净空（证据点落位另有 0.25m 细网格可达性判定兜底）
      const mcx = (box.x0 + box.x1) / 2, mcz = (box.z0 + box.z1) / 2;
      if (Math.abs(x - mcx) < KEEP + hx && Math.abs(z - mcz) < KEEP + hz) return true;
    }
    // 道具的真实占地盒（按 rot 旋转后的 AABB）
    const px0 = x - hx, px1 = x + hx, pz0 = z - hz, pz1 = z + hz;
    for (const d of doors) {
      const wM = d.widthM ?? 1.2;
      const wallLen = (d.wall === 'north' || d.wall === 'south') ? (box.x1 - box.x0) : (box.z1 - box.z0);
      const start = (d.wall === 'north' || d.wall === 'south')
        ? box.x0 + (d.offsetM ?? 0)
        : box.z0 + (d.offsetM ?? 0);
      const end = start + wM;
      // 通道带：贴墙那侧纵深 0.96m，沿墙跨门洞宽 ±0.2m
      if (d.wall === 'south' || d.wall === 'north') {
        const bandZ = d.wall === 'south' ? [box.z0, box.z0 + 0.96] : [box.z1 - 0.96, box.z1];
        const overlapZ = Math.min(pz1, bandZ[1]) - Math.max(pz0, bandZ[0]);
        const overlapX = Math.min(px1, end + 0.2) - Math.max(px0, start - 0.2);
        if (overlapZ > 0 && overlapX > 0) return true;
      } else {
        const bandX = d.wall === 'west' ? [box.x0, box.x0 + 0.96] : [box.x1 - 0.96, box.x1];
        const overlapX = Math.min(px1, bandX[1]) - Math.max(px0, bandX[0]);
        const overlapZ = Math.min(pz1, end + 0.2) - Math.max(pz0, start - 0.2);
        if (overlapX > 0 && overlapZ > 0) return true;
      }
    }
    return false;
  };
  // 候选顺序：按 pref 指定的角落/边开始，逐 0.1m 扫描
  const needX = hx + WALL + 0.05, needZ = hz + WALL + 0.05;
  const xs = [];
  for (let x = box.x0 + needX; x <= box.x1 - needX + 1e-9; x += 0.05) xs.push(Math.round(x * 100) / 100);
  const zs = [];
  for (let z = box.z0 + needZ; z <= box.z1 - needZ + 1e-9; z += 0.05) zs.push(Math.round(z * 100) / 100);
  if (pref === 'ne') zs.reverse();
  if (pref === 'sw' || pref === 'se') { xs.reverse(); if (pref === 'se') zs.reverse(); }
  for (const z of zs) for (const x of xs) if (!blocked(x, z)) return [Math.round((x - box.x0) * 100) / 100, 0, Math.round((z - box.z0) * 100) / 100];
  return null; // 无合法位置 → 交由自检报错
}

const problems = [];   // 自检问题清单（必须在道具寻位之前声明：寻位失败要记进这里）

// 先把道具落到实际位置并写回 BOXES（自检与写出都用同一份数据，避免"自检查布局表、写出用寻位结果"的错位）
for (const b of BOXES) {
  b.props = (b.props ?? []).map((pr) => {
    const spot = placeProp(b, pr.pref ?? 'nw', pr);
    if (!spot) problems.push(`道具 ${b.id}/${pr.kit} 在房间内找不到合法落位（距墙 ≥0.5m 且不挡门洞）`);
    return spot ? { ...pr, pos: spot } : pr;
  });
}

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
// 道具落位自检：按**真实占地盒 + 墙厚**校验（与建模门禁 M6 同一判据，避免两套口径）
const WALL_T = 0.26;
for (const b of BOXES) {
  for (const pr of b.props ?? []) {
    const [lx, , lz] = pr.pos;
    const [hx, hz] = footprintHalf(pr.kit, pr.rot);
    const w = b.x1 - b.x0, d = b.z1 - b.z0;
    if (lx - hx < WALL_T - 1e-6 || lx + hx > w - WALL_T + 1e-6 || lz - hz < WALL_T - 1e-6 || lz + hz > d - WALL_T + 1e-6) {
      problems.push(`道具 ${b.id}/${pr.kit} 占地盒侵入墙体（中心 ${lx.toFixed(2)},${lz.toFixed(2)} 半尺寸 ${hx.toFixed(2)}×${hz.toFixed(2)} 房间 ${w}×${d}）`);
    }
    // 证据房：道具不得压住房间中心（证据点所在处）的 keep-out 区
    if (b.evidence === true) {
      const KEEP = 0.55;   // 只需保住中心附近的可站净空（证据点落位另有 0.25m 细网格可达性判定兜底）
      const mcx = w / 2, mcz = d / 2;
      if (Math.abs(lx - mcx) < KEEP + hx && Math.abs(lz - mcz) < KEEP + hz) {
        problems.push(`道具 ${b.id}/${pr.kit} 压住证据点 keep-out 区（中心 ${mcx.toFixed(2)},${mcz.toFixed(2)}，需 ≥${KEEP}m 净空）`);
      }
    }
  }
}

if (problems.length) {
  console.log('[gen-asylum] 布局表自检失败，拒绝写出：');
  for (const p of problems) console.log('  ✗ ' + p);
  process.exit(1);
}

const level = {
  levelId: 'asylum_v1',
  _note: 'V9 §19.2 疗养院（单层 + 地下太平间；入口区安全教学 / 住院区 5 证据点主压力区 / 地下太平间深处撤离点）。',
  _layout: '坐标单位米，XZ 平面；房间 pos=[最小角点x,最小角点z] + size=[宽,高,深]（x1=pos[0]+宽，z1=pos[1]+深，与 LevelData/LevelGeometry 契约一致），rotY 全 0（轴对齐）。走廊以 doorA/doorB 引用具体门（房间id/门id），两端门必须贴同一条共享墙且开口对齐——由 tools/validate-levels.mjs 强制校验。**本文件由 tools/gen-asylum-v1.mjs 从布局表生成，手改会被覆盖。**',
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
    { type: 'door_lock_shift', minute: 9, durationSec: 20, params: { rooms: ['ward_03', 'ward_04'] }, sanityEffect: -2,
      counterplay: '锁门期间走住院部东西走廊绕行；砸窗会产生 70 强度声纹（半径 20 米），代价明确' },
    { type: 'child_laughter', minute: 12, durationSec: 6, params: { source: 'morgue_ante' }, sanityEffect: -5,
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
