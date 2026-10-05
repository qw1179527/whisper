#!/usr/bin/env node
/**
 * validate-levels.mjs — Level DSL 关卡校验（V9 §19.2，Node 侧）
 *
 * 与 C#（Assets/Scripts/Gameplay/Level/LevelLoader.cs）**同一套规则**：
 *   · 注释键策略：任何 `_` 前缀键都是注释，忽略
 *   · 结构校验：levelId / rooms / size 正数 / kit 存在 / lightZone 枚举 / 门 wall 与 offset /
 *     走廊端点存在且宽度为正 / 事件类型与时长 / 事件数 2~3 / 撤离双点存在且不同
 *   · 跨文件引用完整性：kit 必须在 Assets/Data/asset-manifest.json 中（C3 资产零导入的准入检查）
 * 任一方新增规则必须同步另一方（否则会出现"CI 过、Unity 里炸"）。
 *
 * 用法：node tools/validate-levels.mjs [--level unity/Assets/Levels/asylum_v1.json]
 * 退出码：0 = 全部通过
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const LEVELS_DIR = path.join(ROOT, 'unity/Assets/Levels');
const ASSET_MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');

const VALID_WALLS = new Set(['north', 'south', 'east', 'west']);
const VALID_LIGHT_ZONES = new Set(['safe', 'pressure', 'high-risk']);
// 事件类型策略（D3 修复）：内建 6 型 + 允许扩展前缀 x- / ext- / ns:
// V9 §30.2 明确要求"AI 生成 5 个新类型"，旧版硬编码闭集会把新事件一律判死。
// 事件类型真源 = data/config.json 的 level.eventPool。
// 曾用想当然的简名（doorlock/laugh…），导致**合法事件被判非法**；C# 侧由 tools/config-lint.mjs 守同一一致性。
const CONFIG_PATH = path.join(ROOT, 'data/config.json');
let BUILTIN_EVENT_TYPES;
try {
  const cfg = JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8'));
  BUILTIN_EVENT_TYPES = new Set(cfg.level.eventPool);
} catch (e) {
  console.error(`[levels] 无法读取配置事件池（${CONFIG_PATH}）：${e.message}`);
  console.error('[levels] 事件类型校验无法进行，拒绝静默通过');
  process.exit(1);
}
const isEventTypeAllowed = (t) =>
  typeof t === 'string' && (BUILTIN_EVENT_TYPES.has(t.replace(/^x-/, '').replace(/^ext-/, '').replace(/^ns:/, '')) || /^(x-|ext-|ns:)/.test(t));

/** 去掉所有 `_` 前缀键（注释键策略，与 C# 解析行为一致） */
const stripComments = (v) => {
  if (Array.isArray(v)) return v.map(stripComments);
  if (v && typeof v === 'object') {
    const out = {};
    for (const [k, val] of Object.entries(v)) if (!k.startsWith('_')) out[k] = stripComments(val);
    return out;
  }
  return v;
};

const manifest = JSON.parse(fs.readFileSync(ASSET_MANIFEST, 'utf8'));
const knownKits = new Set((manifest.kits ?? []).map((k) => k.id));
// kit 的 kind（独立验证轨 D2：manifest 声明了 room/prop，但两侧校验器都忽略 →
// "房间用道具套件"这种必然导致运行时拼装错误的数据可以全绿通过）
const KIND = new Map((manifest.kits ?? []).map((k) => [k.id, k.kind]));

const levelArg = process.argv.indexOf('--level');
const files = levelArg > 0
  ? [path.resolve(ROOT, process.argv[levelArg + 1])]
  : fs.readdirSync(LEVELS_DIR).filter((f) => f.endsWith('.json')).map((f) => path.join(LEVELS_DIR, f));

let allProblems = [];
const stats = [];

for (const file of files) {
  const raw = JSON.parse(fs.readFileSync(file, 'utf8'));
  const level = stripComments(raw);
  const problems = [];
  const P = (m) => problems.push(`${path.basename(file)}: ${m}`);

  // 类型断言（独立验证轨 S3：Node 以往对非数组/非字符串只做真值判断，
  // 会出现「CI 绿、Unity 抛」；且 rooms 为对象时 JS 直接 TypeError 崩溃 —— 必须先挡住）
  if (typeof level.levelId !== 'string' || level.levelId.trim() === '') P('levelId 必须是非空字符串');
  if (!Array.isArray(level.rooms) || level.rooms.length === 0) P('rooms 必须是非空数组');
  if (level.corridors !== undefined && !Array.isArray(level.corridors)) P('corridors 必须是数组');
  if (level.events !== undefined && !Array.isArray(level.events)) P('events 必须是数组');

  const ids = new Set();
  let evidence = 0;
  const zoneCount = { safe: 0, pressure: 0, 'high-risk': 0 };
  for (const r of (Array.isArray(level.rooms) ? level.rooms : [])) {
    if (typeof r.id !== 'string' || r.id.trim() === '') P('房间 id 必须是非空字符串');
    else if (ids.has(r.id)) P(`房间 id 重复：${r.id}`);
    else ids.add(r.id);
    if (r.doors !== undefined && !Array.isArray(r.doors)) P(`房间 ${r.id} 的 doors 必须是数组`);
    if (r.props !== undefined && !Array.isArray(r.props)) P(`房间 ${r.id} 的 props 必须是数组`);
    if (!Array.isArray(r.size) || r.size.length !== 3) P(`房间 ${r.id} 的 size 必须是三个数`);
    else if (r.size.some((n) => typeof n !== 'number' || n <= 0)) P(`房间 ${r.id} 的 size 必须为正数`);
    // D1 修复：布局字段（没有它们，§19.2 的 LevelBuilder 无法"实例化房间几何 → 连接走廊"）
    if (!Array.isArray(r.pos) || r.pos.length !== 2) P(`房间 ${r.id} 缺 pos:[x,z]（布局必需，D1）`);
    else if (r.pos.some((n) => typeof n !== 'number')) P(`房间 ${r.id} 的 pos 必须是两个数字`);
    if (r.rotY !== undefined && typeof r.rotY !== 'number') P(`房间 ${r.id} 的 rotY 必须是数字（度）`);
    if (r.floor !== undefined && !Number.isInteger(r.floor)) P(`房间 ${r.id} 的 floor 必须是整数（层）`);
    if (!r.kit) P(`房间 ${r.id} 缺 kit`);
    else if (!knownKits.has(r.kit)) P(`房间 ${r.id} 的 kit \`${r.kit}\` 不在 asset-manifest 中`);
    else if (KIND.get(r.kit) !== 'room') P(`房间 ${r.id} 的 kit \`${r.kit}\` 的 kind=${KIND.get(r.kit)}，房间必须用 kind=room 的套件`);
    if (!VALID_LIGHT_ZONES.has(r.lightZone)) P(`房间 ${r.id} 的 lightZone 非法：${r.lightZone}`);
    else zoneCount[r.lightZone]++;
    if (r.evidencePoint === true) evidence++;
    const doorIds = new Set();
    for (const d of r.doors ?? []) {
      if (!VALID_WALLS.has(d.wall)) P(`房间 ${r.id} 的门 wall 非法：${d.wall}`);
      // D1：门必须有 id，才能被走廊引用（旧版门与走廊数量对不上却无人校验）
      if (typeof d.id !== 'string' || d.id.trim() === '') P(`房间 ${r.id} 的门缺 id（走廊需要引用它，D1）`);
      else if (doorIds.has(d.id)) P(`房间 ${r.id} 门 id 重复：${d.id}`);
      else doorIds.add(d.id);
      // 门字段用**米**（灰盒约定）：offsetM 沿墙起点算起、widthM 门洞宽；缺省 0 / 1.2
      const offM = d.offsetM === undefined ? 0 : d.offsetM;
      const wM = d.widthM === undefined ? 1.2 : d.widthM;
      const wallLen = (d.wall === 'north' || d.wall === 'south') ? (r.size ?? [0, 0, 0])[0] : (r.size ?? [0, 0, 0])[2];
      if (typeof offM !== 'number' || offM < 0) P(`房间 ${r.id} 的门 ${d.id} 的 offsetM 不能为负：${d.offsetM}`);
      if (typeof wM !== 'number' || wM <= 0) P(`房间 ${r.id} 的门 ${d.id} 的 widthM 必须为正：${d.widthM}`);
      else if (offM + wM > wallLen + 1e-4) P(`房间 ${r.id} 的门 ${d.id} 门洞越界：offsetM ${offM} + widthM ${wM} > 墙长 ${wallLen}`);
    }
    for (const pr of r.props ?? []) {
      if (!pr.kit) P(`房间 ${r.id} 有道具缺 kit`);
      else if (!knownKits.has(pr.kit)) P(`房间 ${r.id} 的道具 kit \`${pr.kit}\` 不在 asset-manifest 中`);
      else if (KIND.get(pr.kit) !== 'prop') P(`房间 ${r.id} 的道具 kit \`${pr.kit}\` 的 kind=${KIND.get(pr.kit)}，道具必须用 kind=prop 的套件`);
    }
  }
  // 建立 door 索引：`房间id/门id` → {room, door}
  const doorIndex = new Map();
  for (const r of (Array.isArray(level.rooms) ? level.rooms : [])) {
    for (const d of r.doors ?? []) doorIndex.set(`${r.id}/${d.id}`, { room: r, door: d });
  }
  const roomById = new Map((Array.isArray(level.rooms) ? level.rooms : []).map((r) => [r.id, r]));

  /**
   * 房间按 pos/size/rotY(=0) 求 XZ 包围盒。
   * **pos 是最小角点**（与灰盒 __m4.rect 严格一致：x0=pos[0], z0=pos[1], x1=x0+w, z1=z0+d）。
   * 早期版本按"中心点"实现，与灰盒不兼容，会让 LevelBuilder 整体错位半间房。
   */
  const box = (r) => {
    const [x, z] = r.pos ?? [0, 0];
    const [w, , d] = r.size ?? [0, 0, 0];
    return { x0: x, x1: x + w, z0: z, z1: z + d, cx: x + w / 2, cz: z + d / 2, w, d };
  };
  /** 门在 XZ 平面上的贴墙线：返回沿墙的线段与外法向 */
  const doorLine = (r, d) => {
    const b = box(r);
    const offM = typeof d.offsetM === 'number' ? d.offsetM : 0;
    const wM = typeof d.widthM === 'number' ? d.widthM : 1.2;
    const mid = offM + wM / 2;   // 沿墙取门洞中心作对齐比较（口径与灰盒 compileDoors 一致）
    switch (d.wall) {
      case 'north': return { axis: 'z', fixed: b.z1, from: b.x0, to: b.x1, at: b.x0 + mid, width: wM, normal: [0, 1], bbox: b };
      case 'south': return { axis: 'z', fixed: b.z0, from: b.x0, to: b.x1, at: b.x0 + mid, width: wM, normal: [0, -1], bbox: b };
      case 'west': return { axis: 'x', fixed: b.x0, from: b.z0, to: b.z1, at: b.z0 + mid, width: wM, normal: [-1, 0], bbox: b };
      case 'east': return { axis: 'x', fixed: b.x1, from: b.z0, to: b.z1, at: b.z0 + mid, width: wM, normal: [1, 0], bbox: b };
      default: return null;
    }
  };

  for (const c of (Array.isArray(level.corridors) ? level.corridors : [])) {
    const label = `${c.from}→${c.to}`;
    if (typeof c.from !== 'string') P('走廊 from 必须是字符串');
    if (typeof c.to !== 'string') P('走廊 to 必须是字符串');
    if (typeof c.from === 'string' && !ids.has(c.from)) P(`走廊起点不存在：${c.from}`);
    if (typeof c.to === 'string' && !ids.has(c.to)) P(`走廊终点不存在：${c.to}`);
    if (!(typeof c.width === 'number' && c.width > 0)) P(`走廊 ${label} 的 width 必须为正`);
    // D1：必须引用两端的具体门
    if (typeof c.doorA !== 'string' || typeof c.doorB !== 'string') { P(`走廊 ${label} 必须给 doorA/doorB（引用 房间id/门id，D1）`); continue; }
    const A = doorIndex.get(c.doorA);
    const B = doorIndex.get(c.doorB);
    if (!A) { P(`走廊 ${label} 的 doorA 不存在：${c.doorA}`); continue; }
    if (!B) { P(`走廊 ${label} 的 doorB 不存在：${c.doorB}`); continue; }
    if (A.room.id !== c.from) P(`走廊 ${label} 的 doorA 属于 ${A.room.id}，与 from 不一致`);
    if (B.room.id !== c.to) P(`走廊 ${label} 的 doorB 属于 ${B.room.id}，与 to 不一致`);
    if (A.room.rotY || B.room.rotY) { P(`走廊 ${label} 端点的房间带 rotY（当前几何校验只支持轴对齐，请先置 0 或扩展校验器）`); continue; }
    if (A.room.floor !== B.room.floor) P(`走廊 ${label} 两端楼层不同（${A.room.floor} vs ${B.room.floor}），跨层走廊当前不支持`);
    const la = doorLine(A.room, A.door);
    const lb = doorLine(B.room, B.door);
    if (!la || !lb) continue;
    // ① 两门必须同轴、贴在同一条共享墙上
    if (la.axis !== lb.axis) P(`走廊 ${label} 的两门朝向轴不同（${la.axis} vs ${lb.axis}），无法对接`);
    else if (Math.abs(la.fixed - lb.fixed) > 0.05) P(`走廊 ${label} 的两门不在同一墙面（${la.fixed} vs ${lb.fixed}）`);
    // ② 两门走向必须相反（对开）
    else if (la.normal[0] === lb.normal[0] && la.normal[1] === lb.normal[1]) P(`走廊 ${label} 的两门法向相同，不是对开门`);
    // ③ 门在墙上的位置必须对齐（同一开口）
    else if (Math.abs(la.at - lb.at) > 0.05) P(`走廊 ${label} 的两门开口未对齐（沿墙位置 ${la.at.toFixed(2)} vs ${lb.at.toFixed(2)}）`);
  }

  // ③ 房间不得在 XZ 上重叠（同一层）—— 旧版这一条根本不存在，是 D1 的直接症状之一
  const roomsArr = Array.isArray(level.rooms) ? level.rooms : [];
  for (let i = 0; i < roomsArr.length; i++) {
    for (let j = i + 1; j < roomsArr.length; j++) {
      const a = roomsArr[i], b = roomsArr[j];
      if (!Array.isArray(a.pos) || !Array.isArray(b.pos)) continue;
      if ((a.floor ?? 0) !== (b.floor ?? 0)) continue;
      const ba = box(a), bb = box(b);
      const eps = 0.01;
      const overlapX = Math.min(ba.x1, bb.x1) - Math.max(ba.x0, bb.x0);
      const overlapZ = Math.min(ba.z1, bb.z1) - Math.max(ba.z0, bb.z0);
      if (overlapX > eps && overlapZ > eps) P(`房间 ${a.id} 与 ${b.id} 在 XZ 上重叠（${overlapX.toFixed(2)}×${overlapZ.toFixed(2)} 米）`);
    }
  }
  const evs = Array.isArray(level.events) ? level.events : [];
  for (const e of evs) {
    if (!isEventTypeAllowed(e.type)) P(`事件类型非法：${e.type}（内建 6 型或 x-/ext-/ns: 前缀扩展，V9 §30.2）`);
    // §30.2 schema：params / sanityEffect / counterplay（反制手段是硬要求："必须有明确反制手段"）
    if (e.params !== undefined && (typeof e.params !== 'object' || Array.isArray(e.params))) P(`事件 ${e.type} 的 params 必须是对象`);
    if (e.sanityEffect !== undefined && typeof e.sanityEffect !== 'number') P(`事件 ${e.type} 的 sanityEffect 必须是数字`);
    if (e.counterplay !== undefined && typeof e.counterplay !== 'string') P(`事件 ${e.type} 的 counterplay 必须是字符串`);
    if (e.counterplay === undefined) P(`事件 ${e.type} 缺 counterplay（V9 §30.2 要求每个事件都有明确反制手段）`);
    // minute 必须存在且为数字（C# 侧缺 minute 会抛；此处补齐，消除「CI 过、Unity 炸」）
    if (typeof e.minute !== 'number' || Number.isNaN(e.minute)) P(`事件 ${e.type} 的 minute 必须存在且为数字`);
    if (!(typeof e.durationSec === 'number' && e.durationSec > 0)) P(`事件 ${e.type} 的 durationSec 必须为正`);
  }
  if (evs.length > 0 && (evs.length < 2 || evs.length > 3)) P(`动态事件数量应为 2~3 个（V9 §19.2），当前 ${evs.length}`);
  if (level.extraction) {
    if (typeof level.extraction.standard !== 'string') P('extraction.standard 必须是字符串');
    if (typeof level.extraction.deep !== 'string') P('extraction.deep 必须是字符串');
    if (typeof level.extraction.standard === 'string' && !ids.has(level.extraction.standard)) P(`撤离标准点不存在：${level.extraction.standard}`);
    if (typeof level.extraction.deep === 'string' && !ids.has(level.extraction.deep)) P(`撤离深处点不存在：${level.extraction.deep}`);
    if (level.extraction.standard === level.extraction.deep) P('撤离标准点与深处点不能同一房间');
  }

  stats.push({
    file: path.basename(file),
    levelId: level.levelId,
    rooms: (level.rooms ?? []).length,
    corridors: (level.corridors ?? []).length,
    events: evs.length,
    evidence,
    zones: zoneCount,
    extraction: level.extraction ? `${level.extraction.standard} / ${level.extraction.deep}` : '(未定义)',
  });
  allProblems = allProblems.concat(problems);
}

console.log('[levels] 校验报告');
for (const s of stats) {
  console.log(`  · ${s.file}  levelId=${s.levelId}  房间 ${s.rooms}  走廊 ${s.corridors}  事件 ${s.events}  证据点 ${s.evidence}`);
  console.log(`      光区 safe=${s.zones.safe} pressure=${s.zones.pressure} high-risk=${s.zones['high-risk']}  撤离点 ${s.extraction}`);
}
console.log(`[levels] 已知套件 ${knownKits.size} 个（来自 asset-manifest.json）`);
if (allProblems.length) {
  console.log(`结果：${allProblems.length} 个问题 ✗`);
  for (const p of allProblems) console.log('  ✗ ' + p);
  process.exit(1);
}
// ── Resources 镜像一致性（Unity 只能从 Resources/ 加载；Assets/Levels 是真源，Resources 是发布镜像）
{
  const RES = path.join(ROOT, 'unity/Assets/Resources/Levels');
  const srcFiles = fs.readdirSync(LEVELS_DIR).filter((f) => f.endsWith('.json'));
  let mirrorBad = 0;
  for (const f of srcFiles) {
    const a = fs.readFileSync(path.join(LEVELS_DIR, f));
    const bPath = path.join(RES, f);
    if (!fs.existsSync(bPath)) {
      console.log(`  ✗ Resources 镜像缺失：${f}（真源改动后未同步）`);
      mirrorBad++;
    } else if (!a.equals(fs.readFileSync(bPath))) {
      console.log(`  ✗ Resources 镜像与真源不一致：${f}`);
      mirrorBad++;
    }
  }
  if (mirrorBad) {
    console.log(`结果：Resources 镜像 ${mirrorBad} 处问题 ✗（同步命令：cp unity/Assets/Levels/*.json unity/Assets/Resources/Levels/）`);
    process.exit(1);
  }
  console.log(`  ✓ Resources 镜像与 Assets/Levels 真源逐字节一致（${srcFiles.length} 个关卡）`);
}

console.log('结果：schema + 引用完整性 + V9 结构要求 + Resources 镜像 全部通过 ✓');
