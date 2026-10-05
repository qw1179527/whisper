#!/usr/bin/env node
/**
 * gate-asset-bbox.mjs — 资产几何真源门禁（复核 F3 的直接产物）
 *
 * ## 为什么需要
 * `asset-manifest.json` 里的 `footprint` 是**碰撞盒与道具落位的真源**。
 * 但此前**没有任何一处读过 GLB 的实际网格**去核对它 —— 于是真源写错时，
 * 生成器、碰撞盒、建模门禁 M6 会一起错，而且全都"绿"。
 * 实测事故：footprint 被写成 `[宽x, 高y]`（因为病床模型是竖板），
 * 导致碰撞盒与可见模型相差 1.40m —— 看得见的床和撞得到的床不是同一个东西。
 *
 * ## 判据
 *   B1 每个 kit 的 GLB 可解析出 POSITION accessor 的 min/max
 *   B2 footprint 的 XZ 必须与该 kit **占地部分**的 XZ 一致
 *      （占地部分 = role 为 floor 的部件；kit 还包含顶梁/灯槽/窗框等，会超过地面范围）
 *   B3 GLB 的 Y 向尺寸必须与"结构高度"相符（地面类 kit 的 Y 应远小于其 XZ，即躺平）
 *
 * ## 用法
 *   node tools/gate-asset-bbox.mjs [--inject-footprint|--inject-flat]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');
const args = process.argv.slice(2);
const inject = (n) => args.includes(`--inject-${n}`);

const fails = [], oks = [];
const ok = (m) => { oks.push(m); console.log('  ✓ ' + m); };
const bad = (m) => { fails.push(m); console.log('  ✗ ' + m); };

const manifest = JSON.parse(fs.readFileSync(MANIFEST, 'utf8'));
const srcRoot = path.join(ROOT, 'unity', manifest.sourceRoot ?? 'Assets/ThirdParty/CC0');

/**
 * 某个套件被哪些房间使用、这些房间的**最大层高**是多少。
 * 用于 B3 的第二条判据："套件整体高度不得超过使用它的房间层高 + 0.15m"——
 * 抓的是"套件比房间还高、捅穿楼板"这个新风险（接入天花板后才可能出现）。
 * 数据来源是关卡 DSL 的唯一真源 `unity/Assets/Levels/asylum_v1.json`（房间的 size[1] 即层高）。
 * 读不到就返回 null（**不猜**：宁可跳过这条，也不要凭默认值误判）。
 */
function maxCeilingFor(kitId) {
  try {
    const levelsDir = path.join(ROOT, 'unity/Assets/Levels');
    let maxY = null;
    for (const f of fs.readdirSync(levelsDir).filter((n) => n.endsWith('.json'))) {
      const lv = JSON.parse(fs.readFileSync(path.join(levelsDir, f), 'utf8'));
      for (const r of (lv.rooms ?? [])) {
        if (r.kit !== kitId) continue;
        const h = Array.isArray(r.size) ? r.size[1] : null;
        if (typeof h === 'number' && (maxY === null || h > maxY)) maxY = h;
      }
    }
    return maxY;
  } catch { return null; }
}

/** 读 GLB 的 JSON chunk */
function readGlbJson(file) {
  const b = fs.readFileSync(file);
  if (b.readUInt32LE(0) !== 0x46546C67) throw new Error(`${file} 不是 GLB`);
  const len = b.readUInt32LE(12);
  return JSON.parse(b.slice(20, 20 + len).toString('utf8'));
}
/**
 * 遍历场景节点并**应用节点平移**求整体 bbox。
 * 我第一版只合并 accessor 的 min/max、忽略 translation，于是把 3.4m 高的顶灯槽也算进
 * "占地"，误判病房套件"部件竖立" —— 取值方式错了，判据再对也没用。
 */
function meshBBox(j) {
  let mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
  const walk = (ni, off) => {
    const n = j.nodes?.[ni];
    if (!n) return;
    const o = [off[0] + (n.translation?.[0] ?? 0), off[1] + (n.translation?.[1] ?? 0), off[2] + (n.translation?.[2] ?? 0)];
    const m = j.meshes?.[n.mesh];
    if (m) for (const p of m.primitives ?? []) {
      const a = j.accessors?.[p.attributes?.POSITION];
      if (!a?.min || !a?.max) continue;
      for (let i = 0; i < 3; i++) {
        mn[i] = Math.min(mn[i], a.min[i] + o[i]);
        mx[i] = Math.max(mx[i], a.max[i] + o[i]);
      }
    }
    for (const c of n.children ?? []) walk(c, o);
  };
  for (const ni of (j.scenes?.[0]?.nodes ?? j.nodes?.map((_, i) => i) ?? [])) walk(ni, [0, 0, 0]);
  return { mn, mx, size: [mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]] };
}
/** 单个 node 的 bbox（按 node 名匹配 mesh） */
function nodeBBox(j, name) {
  const idx = (j.nodes ?? []).findIndex((n) => n.name === name);
  if (idx < 0) return null;
  const n = j.nodes[idx];
  const mesh = j.meshes?.[n.mesh];
  if (!mesh) return null;
  let mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
  for (const p of mesh.primitives ?? []) {
    const a = j.accessors?.[p.attributes?.POSITION];
    for (let i = 0; i < 3; i++) { mn[i] = Math.min(mn[i], a.min[i]); mx[i] = Math.max(mx[i], a.max[i]); }
  }
  return { mn, mx, size: [mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]] };
}

const ROUND = (v) => Math.round(v * 100) / 100;
let injected = null;
if (inject('footprint')) {
  // 把某个 kit 的 footprint 的 z 改成 y 向尺寸（复刻真实事故）
  const k = manifest.kits.find((x) => x.id === 'bed_b');
  const j = readGlbJson(path.join(srcRoot, k.file));
  const bb = meshBBox(j);
  k.footprint = [ROUND(bb.size[0]), ROUND(bb.size[1])];   // 故意用 y 替代 z
  injected = 'footprint 写成了 [宽x, 高y]';
} else if (inject('flat')) {
  const k = manifest.kits.find((x) => x.id === 'hospital_ward');
  k.footprint = [k.footprint[1], k.footprint[0]];         // 宽深互换
  injected = 'footprint 宽深互换';
}
if (injected) console.log(`[gate-bbox] 注入模式：${injected}（预期判红）`);

console.log('[gate-bbox] 资产几何真源门禁（footprint ↔ GLB 实际网格）');

const bads = [];
let checked = 0;
for (const k of manifest.kits) {
  const file = path.join(srcRoot, k.file);
  if (!fs.existsSync(file)) { bads.push(`${k.id} 缺产物`); continue; }
  let j;
  try { j = readGlbJson(file); } catch (e) { bads.push(`${k.id} GLB 解析失败：${e.message}`); continue; }
  const bb = meshBBox(j);
  if (!Number.isFinite(bb.size[0])) { bads.push(`${k.id} 无 POSITION accessor`); continue; }
  if (!k.footprint) { bads.push(`${k.id} 清单缺 footprint`); continue; }
  checked++;

  // 占地尺寸的来源：
  //   · 房间套件 → floor 节点的 XZ（套件还含顶灯槽/窗框，它们高度远超地面，不能算占地）
  //   · 道具套件 → 整体 bbox 的 XZ（道具是实体家具）
  const floor = nodeBBox(j, 'floor') ?? nodeBBox(j, 'body') ?? nodeBBox(j, 'frame') ?? null;
  const src = (k.kind === 'room' && floor) ? floor : bb;
  const wantX = ROUND(src.size[0]), wantZ = ROUND(src.size[2]);
  const [fx, fz] = k.footprint;
  if (Math.abs(fx - wantX) > 0.05 || Math.abs(fz - wantZ) > 0.05) {
    bads.push(`${k.id} footprint=[${fx},${fz}] 与 GLB 占地 XZ=[${wantX},${wantZ}] 不符` +
      `（bbox X=${ROUND(bb.size[0])} Y=${ROUND(bb.size[1])} Z=${ROUND(bb.size[2])}）`);
  }
  // 躺平检查（B3，2026-10-04 改为两条更严的判据）：
  //
  // 【为什么改】原判据是 `房间套件的整体 Y > max(XZ)` 即判红，它抓的是"部件竖立"
  // （病床竖板那次事故）。但接入天花板之后这个前提就不成立了：房间套件**本来就该有层高**，
  // 于是"整体 Y 大于 XZ"变成了正常现象。实测反例：morgue 楼板 2×3、层高 3.2
  // → 整体 Y = 3.3 > max(2,3) = 3 → 按原判据**必红**，等于"要天花板就过不了门禁"。
  //
  // 新判据（更严，不是更松）：
  //   ① 占地件（floor/body/frame）必须躺平 —— 直接对着"竖板事故"的那个部件，
  //      而不是拿整体 bbox 猜
  //   ② 房间套件整体高度必须 ≤ 使用它的房间最大层高 + 0.15m ——
  //      这条能抓到新风险："套件比房间还高、捅穿楼板"
  if (k.kind === 'room' && floor && floor.size[1] > Math.max(floor.size[0], floor.size[2])) {
    bads.push(`${k.id} 的占地件 Y=${ROUND(floor.size[1])} 大于其 XZ（${ROUND(floor.size[0])}×${ROUND(floor.size[2])}），疑似竖立`);
  }
  const maxRoomY = maxCeilingFor(k.id);
  if (k.kind === 'room' && maxRoomY !== null && bb.size[1] > maxRoomY + 0.15) {
    bads.push(`${k.id} 整体高度 ${ROUND(bb.size[1])} 超过用它的房间最大层高 ${ROUND(maxRoomY)} + 0.15（会捅穿楼板）`);
  }
}
bads.length === 0
  ? ok(`B1/B2/B3 ${checked} 个套件：GLB 可解析 · footprint 与占地 XZ 一致 · 朝向合理`)
  : bads.slice(0, 6).forEach(bad);

console.log(`\n[gate-bbox] 结果：通过 ${oks.length} · 失败 ${fails.length}${fails.length ? ' ✗' : ' ✓'}`);
if (injected && fails.length === 0) {
  console.log(`[gate-bbox] ✗ 注入 ${injected} 后仍未判红 —— 该门禁不可信`);
  process.exit(1);
}
process.exit(fails.length ? 1 : 0);
