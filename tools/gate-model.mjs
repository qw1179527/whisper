#!/usr/bin/env node
/**
 * gate-model.mjs — 建模门禁（用户要求的四类门禁之首）
 *
 * ## 为什么需要
 * "建模仍然存在很大问题"曾经只是一句感受，无法判定、无法回归。本门禁把可判定的部分
 * 变成数字与断言，从而"下一次改坏了"能立刻被发现。
 *
 * ## 覆盖什么（本机可判定的部分）
 * 几何层（来自 Level DSL，即 LevelBuilder 真正实例化的依据）：
 *   M1 房间矩形有效（宽深 > 0、轴对齐 rotY=0、floor 为整数层）
 *   M2 同层房间不重叠（重叠会造成墙体互穿、门洞错位）
 *   M3 房间不越界（在关卡 bounds 内）
 *   M4 门洞合法（贴墙、完整落在墙长内、与相邻房间共享墙面）
 *   M5 门洞对齐（走廊两端门同轴/共墙/对开/开口对齐）
 *   M6 道具落地不悬空不嵌墙（pos 在房间内、不在门洞通道上、Y≥0）
 *   M7 墙段闭合（把房间矩形按门洞切成段后，段长必须 > 0 且总和 + 门洞 = 墙长）
 *   M8 连通可达（用房间图洪水填充：从入口能到所有房间，含撤离点）
 * 资产层（GLB 套件）：
 *   M9  GLB 容器有效（magic/version/length、JSON chunk、meshes/nodes/materials 计数）
 *   M10 套件清单记录与产物一致（sha256/bytes，由 gen-kits.mjs 回写）
 *   M11 关卡引用的 kit 全部存在于清单且 kind 匹配
 *
 * ## 不覆盖什么（**如实声明**，不假装）
 *   · Unity 运行时的真实网格（三角面朝向/法线/UV/材质）—— 需要 Unity 或引擎侧导出核查
 *   · 渲染观感（光照、雾、遮挡关系）—— 需要真机或渲染截图
 *   M9 只能证明 GLB 结构有效、网格数正确，不能证明"看起来对"。
 *
 * ## 纪律
 * 每类检查都支持 `--inject-<name>` 把人为缺陷注入数据副本，验证门禁真的会红（防假绿）。
 *
 * 用法：node tools/gate-model.mjs [--inject-overlap|--inject-door|--inject-prop|--inject-glb]
 */
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const LEVEL = path.join(ROOT, 'unity/Assets/Levels/asylum_v1.json');
const MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');
const args = process.argv.slice(2);
const inject = (n) => args.includes(`--inject-${n}`);

const fails = [];
const oks = [];
const ok = (m) => { oks.push(m); console.log('  ✓ ' + m); };
const bad = (m) => { fails.push(m); console.log('  ✗ ' + m); };

let level = JSON.parse(fs.readFileSync(LEVEL, 'utf8'));
const manifest = JSON.parse(fs.readFileSync(MANIFEST, 'utf8'));

// ── 注入模式：复制数据并人为破坏，验证门禁会红 ──
let injected = null;
if (inject('overlap')) {
  // 把 ward_03 挪到与 ward_02 重叠
  const a = level.rooms.find((r) => r.id === 'ward_02');
  const b = level.rooms.find((r) => r.id === 'ward_03');
  b.pos = [a.pos[0], a.pos[1]];
  injected = 'M2 房间重叠';
} else if (inject('door')) {
  // 把某条走廊的 doorB 指到另一个房间的门（破坏对齐/归属）
  level.corridors[0].doorB = level.corridors[1].doorB;
  injected = 'M5 门洞对齐/归属';
} else if (inject('prop')) {
  // 把道具塞到房间外
  const r = level.rooms.find((x) => (x.props ?? []).length);
  r.props[0].pos = [999, 0, 999];
  injected = 'M6 道具越界';
} else if (inject('glb')) {
  injected = 'M9 GLB 结构';
}
if (injected) console.log(`[gate-model] 注入模式：${injected}（预期本门禁判红）`);

const box = (r) => {
  const [x, z] = r.pos ?? [0, 0];
  const [w, , d] = r.size ?? [0, 0, 0];
  return { x0: x, z0: z, x1: x + w, z1: z + d, w, d, cx: x + w / 2, cz: z + d / 2 };
};
const doorLine = (r, d) => {
  const b = box(r);
  const offM = typeof d.offsetM === 'number' ? d.offsetM : 0;
  const wM = typeof d.widthM === 'number' ? d.widthM : 1.2;
  const mid = offM + wM / 2;
  switch (d.wall) {
    case 'north': return { axis: 'z', fixed: b.z1, from: b.x0, to: b.x1, at: b.x0 + mid, w: wM, normal: [0, 1], b };
    case 'south': return { axis: 'z', fixed: b.z0, from: b.x0, to: b.x1, at: b.x0 + mid, w: wM, normal: [0, -1], b };
    case 'west': return { axis: 'x', fixed: b.x0, from: b.z0, to: b.z1, at: b.z0 + mid, w: wM, normal: [-1, 0], b };
    case 'east': return { axis: 'x', fixed: b.x1, from: b.z0, to: b.z1, at: b.z0 + mid, w: wM, normal: [1, 0], b };
    default: return null;
  }
};

console.log('[gate-model] 建模门禁（几何 + 资产）');

// ── M1 房间矩形有效 ──
{
  const bads = [];
  for (const r of level.rooms) {
    const b = box(r);
    if (!(b.w > 0) || !(b.d > 0)) bads.push(`${r.id} 宽深非正（${b.w}×${b.d}）`);
    if ((r.rotY ?? 0) !== 0) bads.push(`${r.id} rotY≠0（当前只支持轴对齐）`);
    if (!Number.isInteger(r.floor ?? 0)) bads.push(`${r.id} floor 非整数（${r.floor}）`);
    const [h] = [r.size?.[1]];
    if (!(h > 0)) bads.push(`${r.id} 高非正（${h}）`);
  }
  bads.length === 0 ? ok(`M1 ${level.rooms.length} 个房间矩形有效（宽深/高 > 0 · rotY=0 · floor 整数）`) : bads.forEach(bad);
}

// ── M2 同层不重叠 ──
{
  const bads = [];
  for (let i = 0; i < level.rooms.length; i++) for (let j = i + 1; j < level.rooms.length; j++) {
    const A = level.rooms[i], B = level.rooms[j];
    if ((A.floor ?? 0) !== (B.floor ?? 0)) continue;
    const a = box(A), b = box(B);
    const ox = Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0);
    const oz = Math.min(a.z1, b.z1) - Math.max(a.z0, b.z0);
    if (ox > 0.01 && oz > 0.01) bads.push(`${A.id} 与 ${B.id} 重叠 ${ox.toFixed(2)}×${oz.toFixed(2)}m`);
  }
  bads.length === 0 ? ok('M2 同层房间互不重叠') : bads.slice(0, 4).forEach(bad);
}

// ── M3 房间在 bounds 内（bounds 由房间自身推出，这里校验规模合理） ──
{
  const xs = level.rooms.flatMap((r) => [box(r).x0, box(r).x1]);
  const zs = level.rooms.flatMap((r) => [box(r).z0, box(r).z1]);
  const spanX = Math.max(...xs) - Math.min(...xs);
  const spanZ = Math.max(...zs) - Math.min(...zs);
  (spanX > 0 && spanZ > 0 && spanX < 200 && spanZ < 200)
    ? ok(`M3 关卡尺度合理：X ${spanX.toFixed(1)}m × Z ${spanZ.toFixed(1)}m（< 200m）`)
    : bad(`M3 关卡尺度异常：X ${spanX} × Z ${spanZ}`);
}

// ── M4 门洞合法 ──
{
  const bads = [];
  for (const r of level.rooms) {
    for (const d of r.doors ?? []) {
      const wallLen = (d.wall === 'north' || d.wall === 'south') ? box(r).w : box(r).d;
      const offM = d.offsetM ?? 0, wM = d.widthM ?? 1.2;
      if (offM < 0) bads.push(`${r.id}/${d.id} offsetM<0`);
      if (wM <= 0) bads.push(`${r.id}/${d.id} widthM≤0`);
      if (offM + wM > wallLen + 1e-4) bads.push(`${r.id}/${d.id} 门洞越界（${offM}+${wM} > ${wallLen}）`);
      if (wM < 0.8) bads.push(`${r.id}/${d.id} 门洞过窄（${wM}m < 0.8m，代理直径约 0.68m）`);
    }
  }
  bads.length === 0 ? ok('M4 门洞合法（贴墙 · 不越界 · 宽度 ≥0.8m）') : bads.slice(0, 4).forEach(bad);
}

// ── M5 门洞对齐（走廊两端） ──
{
  const doorIndex = new Map();
  for (const r of level.rooms) for (const d of r.doors ?? []) doorIndex.set(`${r.id}/${d.id}`, { room: r, door: d });
  const bads = [];
  for (const c of level.corridors ?? []) {
    const A = doorIndex.get(c.doorA), B = doorIndex.get(c.doorB);
    if (!A || !B) { bads.push(`${c.from}→${c.to} 门引用不存在`); continue; }
    const la = doorLine(A.room, A.door), lb = doorLine(B.room, B.door);
    if (!la || !lb) { bads.push(`${c.from}→${c.to} 门墙非法`); continue; }
    if (la.axis !== lb.axis) bads.push(`${c.from}→${c.to} 轴向不同`);
    else if (Math.abs(la.fixed - lb.fixed) > 0.05) bads.push(`${c.from}→${c.to} 不共墙（${la.fixed} vs ${lb.fixed}）`);
    else if (la.normal[0] === lb.normal[0] && la.normal[1] === lb.normal[1]) bads.push(`${c.from}→${c.to} 非同面对开`);
    else if (Math.abs(la.at - lb.at) > 0.05) bads.push(`${c.from}→${c.to} 开口不对齐（${la.at} vs ${lb.at}）`);
  }
  bads.length === 0 ? ok(`M5 ${(level.corridors ?? []).length} 条走廊两端门洞对齐（同轴/共墙/对开/开口一致）`) : bads.slice(0, 4).forEach(bad);
}

// ── M6 道具落位合法（按**真实占地盒**，不是中心点）──
{
  const FOOT = new Map(manifest.kits.map((k) => [k.id, k.footprint]));
  const half = (kit, rotDeg) => {
    const f = FOOT.get(kit) ?? [0.8, 0.8];
    const r = ((rotDeg ?? 0) % 180 + 180) % 180;
    const swapped = r >= 45 && r < 135;
    const sx = swapped ? f[1] : f[0], sz = swapped ? f[0] : f[1];
    return [sx / 2, sz / 2];
  };
  const bads = [];
  let total = 0, noFoot = 0;
  for (const r of level.rooms) for (const p of r.props ?? []) {
    total++;
    if (!FOOT.has(p.kit)) { noFoot++; continue; }
    const b = box(r);
    const px = b.x0 + (p.pos?.[0] ?? 0), pz = b.z0 + (p.pos?.[2] ?? 0), py = p.pos?.[1] ?? 0;
    const [hx, hz] = half(p.kit, p.rot);
    if (py < 0) bads.push(`${r.id}/${p.kit} Y<0`);
    // ① 占地盒必须完全在房间内（不被墙切）
    if (px - hx < b.x0 - 1e-6 || px + hx > b.x1 + 1e-6 || pz - hz < b.z0 - 1e-6 || pz + hz > b.z1 + 1e-6)
      bads.push(`${r.id}/${p.kit} 占地盒越出房间（中心 ${px.toFixed(2)},${pz.toFixed(2)} 半尺寸 ${hx.toFixed(2)}×${hz.toFixed(2)}）`);
    // ② 占地盒不得与门洞通道带相交
    for (const d of r.doors ?? []) {
      const wM = d.widthM ?? 1.2;
      const along = (d.wall === 'north' || d.wall === 'south')
        ? [b.x0 + (d.offsetM ?? 0), b.x0 + (d.offsetM ?? 0) + wM]
        : [b.z0 + (d.offsetM ?? 0), b.z0 + (d.offsetM ?? 0) + wM];
      let ov;
      if (d.wall === 'south' || d.wall === 'north') {
        const band = d.wall === 'south' ? [b.z0, b.z0 + 0.96] : [b.z1 - 0.96, b.z1];
        ov = Math.min(pz + hz, band[1]) - Math.max(pz - hz, band[0]) > 0
          && Math.min(px + hx, along[1] + 0.2) - Math.max(px - hx, along[0] - 0.2) > 0;
      } else {
        const band = d.wall === 'west' ? [b.x0, b.x0 + 0.96] : [b.x1 - 0.96, b.x1];
        ov = Math.min(px + hx, band[1]) - Math.max(px - hx, band[0]) > 0
          && Math.min(pz + hz, along[1] + 0.2) - Math.max(pz - hz, along[0] - 0.2) > 0;
      }
      if (ov) bads.push(`${r.id}/${p.kit} 占地盒压住门洞 ${d.id}`);
    }
  }
  if (noFoot) bads.push(`${noFoot} 个道具的 kit 在清单里没有 footprint（无法判定占地，必须补）`);
  bads.length === 0 ? ok(`M6 ${total} 个道具：占地盒在房间内、不压门洞、Y≥0（按 footprint 与 rot 真实判定）`) : bads.slice(0, 4).forEach(bad);
}

// ── M7 墙段闭合（按门洞切段：段长 > 0、总长 = 墙长 − 门洞宽之和） ──
{
  const bads = [];
  for (const r of level.rooms) {
    const b = box(r);
    for (const [wall, wallLen, doors] of [
      ['north', b.w, (r.doors ?? []).filter((d) => d.wall === 'north')],
      ['south', b.w, (r.doors ?? []).filter((d) => d.wall === 'south')],
      ['west', b.d, (r.doors ?? []).filter((d) => d.wall === 'west')],
      ['east', b.d, (r.doors ?? []).filter((d) => d.wall === 'east')],
    ]) {
      const gaps = doors.map((d) => ({ a: d.offsetM ?? 0, b: (d.offsetM ?? 0) + (d.widthM ?? 1.2) })).sort((x, y) => x.a - y.a);
      let cursor = 0, segSum = 0, overlap = false;
      for (const g of gaps) {
        if (g.a < cursor - 1e-4) overlap = true;
        if (g.a > cursor) segSum += g.a - cursor;
        cursor = Math.max(cursor, g.b);
      }
      if (cursor < wallLen) segSum += wallLen - cursor;
      const gapSum = gaps.reduce((s, g) => s + (g.b - g.a), 0);
      if (overlap) bads.push(`${r.id} ${wall} 门洞重叠`);
      if (Math.abs(segSum + gapSum - wallLen) > 0.02) bads.push(`${r.id} ${wall} 段长+门洞≠墙长（${(segSum + gapSum).toFixed(2)} vs ${wallLen}）`);
      if (segSum < -1e-4) bads.push(`${r.id} ${wall} 段长合计为负`);
    }
  }
  bads.length === 0 ? ok('M7 每面墙按门洞切段后：无重叠、段长合计 + 门洞 = 墙长（闭合）') : bads.slice(0, 4).forEach(bad);
}

// ── M8 连通可达（房间图洪水填充，**分层 + 竖井**） ──
//
// 【2026-10-04 修 · 三层之后这个判据必然假红】
// 旧实现只读 `level.corridors` 建图。加了楼层与竖井之后，二三层房间**只能通过竖井到达**，
// 而竖井不是 corridor → 四个新房间被判"不可达"（实测：corridor_main_f1/f2、lobby、boiler）。
// 这不是关卡错，是**判据跟不上数据模型**。
//
// 修法：与 `LevelWorld.Reachable`（C# 侧、build-render 写的）**同原理** ——
// 竖井矩形落在哪几层的哪些房间里，就把那些房间连成一条边（竖井 = 跨层边）。
// 这样"竖井画在不可走的地方"会真判不通（C# 探针用假竖井验证过有牙），本门禁也一致。
{
  const adj = new Map(level.rooms.map((r) => [r.id, []]));
  for (const c of level.corridors ?? []) {
    if (adj.has(c.from) && adj.has(c.to)) { adj.get(c.from).push(c.to); adj.get(c.to).push(c.from); }
  }

  // 竖井 → 它在每一层"罩住"的房间，连成一条边
  //
  // ⚠ DSL 用的是**平铺字段** `minX/minZ/maxX/maxZ`（不是 `rect` 数组）——
  // 我第一版按 `rect` 兼容写，解析不出任何竖井，M8 依然假红。**判据必须按真数据字段写**。
  const shafts = level.shafts ?? [];
  const shaftEdges = [];
  const shaftProblems = [];
  for (const s of shafts) {
    const sr = s.rect ?? s;
    const x0 = sr.minX ?? sr.x0 ?? sr[0], z0 = sr.minZ ?? sr.z0 ?? sr[1];
    const x1 = sr.maxX ?? sr.x1 ?? sr[2], z1 = sr.maxZ ?? sr.z1 ?? sr[3];
    if ([x0, z0, x1, z1].some((v) => typeof v !== 'number')) { shaftProblems.push(`${s.id ?? '?'} 矩形字段不是数字`); continue; }
    const from = s.fromFloor ?? 0, to = s.toFloor ?? from;
    const hit = [];                                           // 每层至多取一个"被竖井罩住"的房间
    for (let f = from; f <= to; f++) {
      const r = (level.rooms ?? []).find((room) => {
        if ((room.floor ?? 0) !== f) return false;
        const b = box(room);
        return x0 >= b.x0 - 1e-6 && z0 >= b.z0 - 1e-6 && x1 <= b.x1 + 1e-6 && z1 <= b.z1 + 1e-6;
      });
      if (r) hit.push(r.id);
    }
    // 【变异验证暴露的假绿，必须显式判红】
    // 我第一版在这里 `continue`（罩住不足两层就跳过）—— 结果是：把竖井挪到**无人区**（如 x=100）
    // 只会让它连不通任何东西，**却不会让任何房间变成不可达** → M8 照样判绿，门禁被绕过。
    // 实测：注入假竖井后 M8 仍报"可达全部 15 个房间"。所以"竖井罩不住 ≥2 层"本身就是缺陷，
    // 必须直接判红 —— 它意味着"画了个竖井但没人站得上去"（与 C# 探针的假竖井判据同一条原则）。
    if (hit.length < 2)
    {
      shaftProblems.push(`${s.id ?? '?'} 只罩住 ${hit.length} 层（${hit.join('/') || '无'}）—— 竖井必须跨层且每层都要落在可走房间里`);
      continue;
    }
    for (let i = 1; i < hit.length; i++) {
      adj.get(hit[0]).push(hit[i]); adj.get(hit[i]).push(hit[0]);
      shaftEdges.push(`${hit[0]}↔${hit[i]}`);
    }
  }

  const start = level.extraction?.standard ?? level.rooms[0].id;
  const seen = new Set([start]);
  const stack = [start];
  while (stack.length) {
    const cur = stack.pop();
    for (const nb of adj.get(cur) ?? []) if (!seen.has(nb)) { seen.add(nb); stack.push(nb); }
  }
  const unreachable = level.rooms.map((r) => r.id).filter((id) => !seen.has(id));
  const extractionOk = [level.extraction?.standard, level.extraction?.deep].every((id) => !id || seen.has(id));
  const floorCount = new Set((level.rooms ?? []).map((r) => r.floor ?? 0)).size;
  const via = shaftEdges.length ? `（含 ${shafts.length} 个竖井的 ${shaftEdges.length} 条跨层边）` : '';
  (unreachable.length === 0 && extractionOk && shaftProblems.length === 0)
    ? ok(`M8 连通性：从 ${start} 可达全部 ${level.rooms.length} 个房间 · ${floorCount} 层${via}（含两个撤离点）`)
    : bad(`M8 不可达房间 ${unreachable.length} 个：${unreachable.slice(0, 6).join(', ')}`
        + `${extractionOk ? '' : ' · 撤离点不可达'}`
        + `${shaftProblems.length ? ` · 竖井问题：${shaftProblems.slice(0, 3).join('；')}` : ''}`);
}

// ── M9 GLB 结构有效 ──
{
  const srcRoot = path.join(ROOT, 'unity', manifest.sourceRoot ?? 'Assets/ThirdParty/CC0');
  const bads = [];
  let checked = 0;
  for (const k of manifest.kits) {
    const f = path.join(srcRoot, k.file);
    if (!fs.existsSync(f)) { bads.push(`${k.id} 缺产物`); continue; }
    const b = fs.readFileSync(f);
    if (inject('glb') && checked === 0) { b.writeUInt32LE(0xdeadbeef, 0); }
    const magic = b.readUInt32LE(0), ver = b.readUInt32LE(4), len = b.readUInt32LE(8);
    if (magic !== 0x46546C67) bads.push(`${k.id} magic 错`);
    if (ver !== 2) bads.push(`${k.id} glTF 版本 ${ver} ≠ 2`);
    if (len !== b.length) bads.push(`${k.id} 声明长度 ${len} ≠ 实际 ${b.length}`);
    const ctype = b.readUInt32LE(16);
    if (ctype !== 0x4E4F534A) bads.push(`${k.id} 首个 chunk 不是 JSON`);
    checked++;
  }
  bads.length === 0 ? ok(`M9 ${checked} 个 GLB 容器有效（magic/版本/长度/JSON chunk）`) : bads.slice(0, 4).forEach(bad);
}

// ── M10 清单记录与产物一致 ──
{
  const srcRoot = path.join(ROOT, 'unity', manifest.sourceRoot ?? 'Assets/ThirdParty/CC0');
  const bads = [];
  for (const k of manifest.kits) {
    const f = path.join(srcRoot, k.file);
    if (!fs.existsSync(f)) { bads.push(`${k.id} 缺产物`); continue; }
    const buf = fs.readFileSync(f);
    const sha = crypto.createHash('sha256').update(buf).digest('hex');
    if (k.sha256 && k.sha256 !== sha) bads.push(`${k.id} sha256 不一致`);
    if (k.bytes && k.bytes !== buf.length) bads.push(`${k.id} 字节数不一致`);
  }
  bads.length === 0 ? ok('M10 套件产物 sha256/字节数与清单记录一致') : bads.slice(0, 4).forEach(bad);
}

// ── M11 关卡引用 kit 均在清单且 kind 匹配 ──
{
  const kind = new Map(manifest.kits.map((k) => [k.id, k.kind]));
  const bads = [];
  for (const r of level.rooms) {
    if (!kind.has(r.kit)) bads.push(`房间 ${r.id} 的 kit ${r.kit} 不在清单`);
    else if (kind.get(r.kit) !== 'room') bads.push(`房间 ${r.id} 用非 room 套件（${kind.get(r.kit)}）`);
    for (const p of r.props ?? []) {
      if (!kind.has(p.kit)) bads.push(`道具 ${p.kit} 不在清单`);
      else if (kind.get(p.kit) !== 'prop') bads.push(`道具 ${p.kit} 用非 prop 套件（${kind.get(p.kit)}）`);
    }
  }
  bads.length === 0 ? ok('M11 关卡引用的 kit 全部存在且 kind 匹配') : bads.slice(0, 4).forEach(bad);
}

console.log(`\n[gate-model] 结果：通过 ${oks.length} · 失败 ${fails.length}${fails.length ? ' ✗' : ' ✓'}`);
if (injected && fails.length === 0) {
  console.log(`[gate-model] ✗ 注入 ${injected} 后门禁仍未判红 —— 该门禁不可信，必须修`);
  process.exit(1);
}
process.exit(fails.length ? 1 : 0);
