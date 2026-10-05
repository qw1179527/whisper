#!/usr/bin/env node
/**
 * gen-map.mjs — 通用关卡生成器（多地图）：读布局表 → 校验几何自检 → 产出关卡 JSON
 *
 * ## 为什么单独一个生成器
 * `gen-asylum-v1.mjs` 的布局表、门对齐、道具寻位是**经过门禁验证**的一整套规则；
 * 多地图应该在**同一套规则**下换布局，而不是复制一份慢慢漂移。
 * 故：本文件只负责「通用几何编译 + 自检」，布局数据放 `tools/lib/map-layouts.mjs`。
 *
 * ## 编译规则（与 gen-asylum-v1 的产物契约一致）
 * · `pos = [x0, z0]`（**最小角点**，与灰盒 `__m4.rect()` 一致 —— 写成中心点会让 LevelBuilder 整体错位半间房）；
 * · `size = [宽, 高, 深]`；
 * · 门沿墙位置用 `offsetM`（米），起点取该墙**坐标较小的一端**（东/西墙沿 z 递增，南/北墙沿 x 递增）；
 *   布局表写门洞**中心**的绝对坐标 `at`，这里反算并**夹取到墙内**（窄墙放宽门时必须夹，否则越界）；
 * · 门宽由连接表（LINKS）给出 = 走廊宽度即门宽，避免两处各写一遍。
 *
 * ## 自带几何自检（产物交给 validate-levels 之前先自查）
 * G1 同层不重叠 · G2 门必须落在**两房共享的那面墙**上且开口对齐 ·
 * G3 从入口洪水填充可达全部房间 · G4 门洞不越界。
 * 自检不过直接非零退出，**不产出半成品关卡**。
 *
 * 用法：node tools/gen-map.mjs --layout <id> --out <file>
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { LAYOUTS } from './lib/map-layouts.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const argOf = (n, d) => { const i = argv.indexOf(n); return i >= 0 && i + 1 < argv.length ? argv[i + 1] : d; };
const layoutId = argOf('--layout', null);
const outArg = argOf('--out', null);
if (!layoutId) { console.error('[gen-map] ✗ 需要 --layout <id>'); process.exit(1); }
const L = LAYOUTS[layoutId];
if (!L) { console.error(`[gen-map] ✗ 未知布局：${layoutId}（可用：${Object.keys(LAYOUTS).join(', ')}）`); process.exit(1); }

const BOXES = L.boxes;
const LINKS = L.links;

// ── 门宽（走廊宽度即门宽）────────────────────────────────────────────
const DOOR_WIDTH = new Map();
for (const [a, b, w] of LINKS) { DOOR_WIDTH.set(a, w); DOOR_WIDTH.set(b, w); }
for (const box of BOXES) for (const d of box.doors ?? []) d.widthM = DOOR_WIDTH.get(`${box.id}/${d.id}`) ?? 1.2;

// ── 几何自检（产出之前）──────────────────────────────────────────────
const problems = [];
const byId = new Map(BOXES.map((b) => [b.id, b]));

// G1 同层不重叠
for (let i = 0; i < BOXES.length; i++) {
  for (let j = i + 1; j < BOXES.length; j++) {
    const a = BOXES[i], b = BOXES[j];
    if ((a.floor ?? 0) !== (b.floor ?? 0)) continue;
    const ox = Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0);
    const oz = Math.min(a.z1, b.z1) - Math.max(a.z0, b.z0);
    if (ox > 0.01 && oz > 0.01) problems.push(`G1 同层重叠：${a.id} 与 ${b.id}（${ox.toFixed(2)}×${oz.toFixed(2)}m）`);
  }
}
// G4 门洞不越界（夹取前先看原始中心是否落在墙内）
for (const b of BOXES) {
  for (const d of b.doors ?? []) {
    const horiz = d.wall === 'north' || d.wall === 'south';
    const lo = horiz ? b.x0 : b.z0, hi = horiz ? b.x1 : b.z1;
    const wallLen = hi - lo, w = d.widthM;
    if (w > wallLen + 0.001) { problems.push(`G4 ${b.id}/${d.id} 门宽 ${w} 超过墙长 ${wallLen.toFixed(2)}`); continue; }
    const centerLocal = d.at - lo;
    const start = Math.max(0, Math.min(wallLen - w, centerLocal - w / 2));
    d.offsetM = Math.round(start * 1000) / 1000;
    if (centerLocal - w / 2 < -0.001 || centerLocal + w / 2 > wallLen + 0.001) {
      problems.push(`G4 ${b.id}/${d.id} 门洞中心 ${d.at} 越界（墙 ${lo}..${hi}，宽 ${w}）—— 已夹取但布局表应修正`);
    }
  }
}
// G2 连接表两端必须在共享墙上且开口对齐
const wallFixed = (b, wall) => wall === 'east' ? b.x1 : wall === 'west' ? b.x0 : wall === 'north' ? b.z1 : b.z0;
const alongAt = (b, d) => {
  const horiz = d.wall === 'north' || d.wall === 'south';
  const lo = horiz ? b.x0 : b.z0;
  return lo + d.offsetM + d.widthM / 2;   // 夹取之后的实际中心
};
const doorOf = (ref) => {
  const [rid, did] = ref.split('/');
  const b = byId.get(rid);
  const d = (b?.doors ?? []).find((x) => x.id === did);
  return b && d ? { b, d } : null;
};
for (const [ra, rb] of LINKS) {
  const A = doorOf(ra), B = doorOf(rb);
  if (!A || !B) { problems.push(`G2 连接表引用了不存在的门：${!A ? ra : rb}`); continue; }
  const opp = { north: 'south', south: 'north', east: 'west', west: 'east' };
  if (opp[A.d.wall] !== B.d.wall) { problems.push(`G2 ${ra}(${A.d.wall}) 与 ${rb}(${B.d.wall}) 不相对`); continue; }
  const fx = wallFixed(A.b, A.d.wall), gx = wallFixed(B.b, B.d.wall);
  if (Math.abs(fx - gx) > 0.01) { problems.push(`G2 ${ra} 与 ${rb} 不在共享墙上（${fx} vs ${gx}）`); continue; }
  const ca = alongAt(A.b, A.d), cb = alongAt(B.b, B.d);
  if (Math.abs(ca - cb) > 0.02) problems.push(`G2 ${ra} 与 ${rb} 开口未对齐（中心 ${ca.toFixed(2)} vs ${cb.toFixed(2)}）`);
}

// G3 连通性（按连接表建图，从第一个房间洪水填充）
{
  const adj = new Map(BOXES.map((b) => [b.id, []]));
  for (const [ra, rb] of LINKS) {
    const [a] = ra.split('/'), [b] = rb.split('/');
    if (adj.has(a) && adj.has(b)) { adj.get(a).push(b); adj.get(b).push(a); }
  }
  // ⚠ 竖井必须算成**跨层边**：二层房间只能靠竖井到达。
  // 这不是放松判据 —— gate-model 的 M8 就是这么算的（竖井矩形落在哪几层的哪些房间里 → 连边）。
  // G3 作为"产出前自检"必须与 M8 同口径，否则会出现"自检说不可达、门禁说可达"的两套口径。
  for (const sh of (L.shafts ?? [])) {
    const hosted = [];
    for (const b of BOXES) {
      if ((b.floor ?? 0) < sh.fromFloor || (b.floor ?? 0) > sh.toFloor) continue;
      const ox = Math.min(b.x1, sh.maxX) - Math.max(b.x0, sh.minX);
      const oz = Math.min(b.z1, sh.maxZ) - Math.max(b.z0, sh.minZ);
      if (ox > 0.01 && oz > 0.01) hosted.push(b.id);
    }
    for (let i = 1; i < hosted.length; i++) {
      adj.get(hosted[0])?.push(hosted[i]);
      adj.get(hosted[i])?.push(hosted[0]);
    }
  }
  const startId = BOXES[0].id;
  const seen = new Set([startId]);
  const q = [startId];
  while (q.length) { for (const n of adj.get(q.shift()) ?? []) if (!seen.has(n)) { seen.add(n); q.push(n); } }
  for (const b of BOXES) if (!seen.has(b.id)) problems.push(`G3 不可达：${b.id}（从 ${startId} 洪水填充不到）`);
}

// events 的字段校验写在上面的 map 里，故这里统一在产出前汇总判红
// G5 竖井自检：与 gate-model 的 M8 同原理 —— 竖井矩形落在哪几层的哪些房间里，那些房间才连得上。
// 若某层矩形与竖井不相交，那一层就"站不住人"，M8 会判不通。
for (const sh of (L.shafts ?? [])) {
  if (!sh.id || !sh.kind) { problems.push('G5 竖井缺 id/kind'); continue; }
  if (!['lift', 'stair'].includes(sh.kind)) problems.push(`G5 竖井 ${sh.id} 的 kind 非法：${sh.kind}（合法：lift/stair）`);
  if (!(sh.maxX > sh.minX) || !(sh.maxZ > sh.minZ)) { problems.push(`G5 竖井 ${sh.id} 矩形非正`); continue; }
  if (!(sh.toFloor > sh.fromFloor)) { problems.push(`G5 竖井 ${sh.id} 层区间非正（${sh.fromFloor}→${sh.toFloor}）`); continue; }
  for (let f = sh.fromFloor; f <= sh.toFloor; f++) {
    const hosted = BOXES.some((b) => {
      if ((b.floor ?? 0) !== f) return false;
      const ox = Math.min(b.x1, sh.maxX) - Math.max(b.x0, sh.minX);
      const oz = Math.min(b.z1, sh.maxZ) - Math.max(b.z0, sh.minZ);
      return ox > 0.01 && oz > 0.01;   // 与该层某房间有真实交集
    });
    if (!hosted) problems.push(`G5 竖井 ${sh.id} 在 ${f} 层没有可走房间承载（玩家到不了）`);
  }
}

if (problems.length) {
  console.error(`[gen-map] ✗ 自检 ${problems.length} 个问题（未产出）：`);
  for (const p of problems) console.error('   ' + p);
  process.exit(1);
}
console.log(`[gen-map] 几何自检通过：房间 ${BOXES.length} · 连接 ${LINKS.length}（共享墙/开口对齐/无重叠/全可达）`);

// ── 产过关卡 JSON ────────────────────────────────────────────────────
const rooms = BOXES.map((b) => ({
  id: b.id,
  pos: [b.x0, b.z0],
  size: [b.x1 - b.x0, b.h, b.z1 - b.z0],
  rotY: 0,
  floor: b.floor ?? 0,
  kit: b.kit,
  doors: (b.doors ?? []).map((d) => ({
    id: d.id, wall: d.wall, offsetM: d.offsetM, widthM: d.widthM,
    ...(d.type ? { type: d.type } : {}),
    ...(d.locked ? { locked: true } : {}),
  })),
  props: (b.props ?? []).map((p) => ({ kit: p.kit, pos: p.pos, rot: p.rot, pref: p.pref })),
  evidencePoint: b.evidence === true,
  lightZone: b.zone ?? 'pressure',
}));

const level = {
  levelId: L.id,
  _note: L.note,
  _layout: '坐标单位米，XZ 平面；房间 pos=[最小角点x,最小角点z] + size=[宽,高,深]；'
    + '走廊以 doorA/doorB 引用具体门，两端门必须贴同一条共享墙且开口对齐——由 tools/validate-levels.mjs 强制校验。'
    + '**本文件由 tools/gen-map.mjs 从 tools/lib/map-layouts.mjs 生成，手改会被覆盖。**',
  _zones: L.zones ?? {},
  rooms,
  // 「真源形状」照抄自 unity/Assets/Levels/asylum_v1.json：
  //   { from, to, doorA, doorB, width } —— **from/to 是房间 id**，不是门引用；宽度键名是 width。
  // 我第一版只写 doorA/doorB/widthM，被 validate-levels 逐条判红（"走廊 from 必须是字符串"）。
  corridors: LINKS.map(([a, b, w]) => ({
    from: a.split('/')[0], to: b.split('/')[0], doorA: a, doorB: b, width: w,
  })),
  events: (L.events ?? []).map((e) => {
    // 事件schema 照抄真源：必须带 type/minute/durationSec/counterplay（V9 §30.2 要求每个事件有明确反制手段）。
    // 缺字段 → 报错不产出（宁可生成失败，也不要产出一个门禁必红的关卡）。
    for (const k of ['type', 'minute', 'durationSec', 'counterplay']) {
      if (e[k] === undefined || e[k] === null) problems.push(`事件 ${e.type ?? '?'} 缺字段 ${k}`);
    }
    return { type: e.type, minute: e.minute, durationSec: e.durationSec,
             params: e.params ?? {}, sanityEffect: e.sanityEffect ?? 0, counterplay: e.counterplay };
  }),
  extraction: L.extraction,
  // 竖井（跨层连通）：形状照抄真源 asylum_v1.json → { id, kind, minX, minZ, maxX, maxZ, fromFloor, toFloor }
  // 单层图给空数组（真源 asylum 有 2 条；空数组对单层图是正确表达，不是缺失）。
  shafts: (L.shafts ?? []).map((sh) => ({
    id: sh.id, kind: sh.kind, minX: sh.minX, minZ: sh.minZ, maxX: sh.maxX, maxZ: sh.maxZ,
    fromFloor: sh.fromFloor, toFloor: sh.toFloor,
  })),
};   // 真源没有 bounds 键 —— 多余字段会被门禁当作可疑输入

const OUT = outArg ? path.resolve(ROOT, outArg) : path.join(ROOT, `unity/Assets/Levels/${L.id}.json`);
fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, JSON.stringify(level, null, 2) + '\n', 'utf8');
console.log(`[gen-map] 已写出 ${path.relative(ROOT, OUT)} · 房间 ${rooms.length} · 走廊 ${level.corridors.length}`);
