#!/usr/bin/env node
/**
 * 001-door-cell-support.mjs — 让 __m4 支持**格子形式的门**（`{id, cell}`）
 *
 * ## 缺陷（实测证据）
 * 关卡 DSL 的门是格子形式：`{ "id": "door_lobby_main", "cell": [6,5], "locked": false }`。
 * 而 `__m4.compileDoors` 只认 `{wall, offsetM, widthM}`：
 *   · `d.wall === undefined` → 落进 `else` 分支（按东墙摆放）
 *   · `mid = undefined + 0.6 = NaN` → `pos = { x: r.x1, z: NaN }`
 * 实测 lobby 的门：cell 中心应为 (6.5,5.5)，实际编译成 (10, NaN)。
 * 后果链：门位置全错 → `doorConnections` 在门两侧 0.6m 处探不到房间 →
 *   **房间图 0 条边** → `findRoomPath` 恒返回 null → 路点为空 →
 *   怪物只能直线奔向目标 → 撞墙卡住（用户反馈"鬼的寻路有问题"）。
 * 另外 `rect(room)` 只读 `pos/size`，遇到已编译房间（直接带 `rect`）会解构出错。
 *
 * ## 修法
 * 1. `compileDoors` 增加格子形式分支：由 cell 相对房间矩形的边推导 wall / pos / normal
 * 2. `rect(room)` 增加 `room.rect` 直通分支
 * 3. 修不了的门（位置 NaN）直接跳过 —— 宁可少一扇门，也不要一扇位置是 NaN 的门
 *
 * ## 纪律
 * · **不改 baseline/**（那是 0.6.0 的已验证产物，对拍基准）
 * · 补丁打在 `src/modules/*.js`（构建产物，可重生成），幂等（重复应用会检测标记）
 * · 打补丁后必须重跑 `tools/verify-sourcetree.mjs`，它会把与 baseline 的差异逐条列出来
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'src/modules/__m4.js');
const MARK = '/* PATCH 001: door-cell-support */';

let src = fs.readFileSync(FILE, 'utf8');
if (src.includes(MARK)) {
  console.log('[patch 001] 已应用（检测到标记），跳过');
  process.exit(0);
}

// ── ① rect(room)：兼容已编译房间（带 rect） ──
const rectOld = `/** AABB 房间矩形 */
    function rect(room) {
      const [w, , d] = room.size;
      const [x, , z] = room.pos;
      return { x0: x, z0: z, x1: x + w, z1: z + d, w, d };
    }`;
const rectNew = `/** AABB 房间矩形 */
    function rect(room) {
      ${MARK}
      // 已编译房间直接带 rect（buildLevel 会给每个房间补 rect）；只有原始 DSL 才需要由 pos/size 推导。
      if (room.rect && typeof room.rect.x0 === 'number') return room.rect;
      const [w, , d] = room.size;
      const [x, , z] = room.pos;
      return { x0: x, z0: z, x1: x + w, z1: z + d, w, d };
    }`;
if (!src.includes(rectOld)) { console.error('[patch 001] ✗ 未找到 rect(room) 原文（结构变了？）'); process.exit(1); }
src = src.replace(rectOld, rectNew);

// ── ② compileDoors：增加格子形式分支 ──
const doorOld = `        for (const d of room.doors ?? []) {
          const w = d.widthM ?? 1.2;
          const mid = d.offsetM + w / 2;
          let pos;
          let normal;
          if (d.wall === 'north') { pos = { x: r.x0 + mid, z: r.z0 }; normal = { x: 0, z: -1 }; }
          else if (d.wall === 'south') { pos = { x: r.x0 + mid, z: r.z1 }; normal = { x: 0, z: 1 }; }
          else if (d.wall === 'west') { pos = { x: r.x0, z: r.z0 + mid }; normal = { x: -1, z: 0 }; }
          else { pos = { x: r.x1, z: r.z0 + mid }; normal = { x: 1, z: 0 }; }`;
const doorNew = `        for (const d of room.doors ?? []) {
          ${MARK}
          // 门有两种写法：格子形式 {id, cell}（关卡 DSL 实际用法）与墙面形式 {wall, offsetM, widthM}。
          // 只认后者时，格子门会落进 else 分支被摆到东墙且 z=NaN —— 这正是"房间图零边、怪直线撞墙"的根因。
          let w = d.widthM ?? 1.2;
          let pos;
          let normal;
          let wall = d.wall;
          if (Array.isArray(d.cell)) {
            const [cx, cz] = d.cell;
            pos = { x: cx + 0.5, z: cz + 0.5 };
            // 门格本身是 1×1；用「格中心相对房间矩形的位置」判断贴在哪面墙上
            if (Math.abs(pos.x - r.x0) <= 0.6) { wall = 'west'; normal = { x: -1, z: 0 }; }
            else if (Math.abs(pos.x - r.x1) <= 0.6) { wall = 'east'; normal = { x: 1, z: 0 }; }
            else if (Math.abs(pos.z - r.z0) <= 0.6) { wall = 'north'; normal = { x: 0, z: -1 }; }
            else if (Math.abs(pos.z - r.z1) <= 0.6) { wall = 'south'; normal = { x: 0, z: 1 }; }
            else { wall = null; normal = null; }
            w = 1.0;   // 格子门洞口宽 = 1 格
          } else {
            const mid = d.offsetM + w / 2;
            if (d.wall === 'north') { pos = { x: r.x0 + mid, z: r.z0 }; normal = { x: 0, z: -1 }; }
            else if (d.wall === 'south') { pos = { x: r.x0 + mid, z: r.z1 }; normal = { x: 0, z: 1 }; }
            else if (d.wall === 'west') { pos = { x: r.x0, z: r.z0 + mid }; normal = { x: -1, z: 0 }; }
            else { pos = { x: r.x1, z: r.z0 + mid }; normal = { x: 1, z: 0 }; }
          }
          // 位置不可解（NaN / 无法判断贴墙）→ 跳过：宁可少一扇门，也不要一扇位置是 NaN 的门
          if (!normal || !Number.isFinite(pos.x) || !Number.isFinite(pos.z)) continue;`;
if (!src.includes(doorOld)) { console.error('[patch 001] ✗ 未找到 compileDoors 原文（结构变了？）'); process.exit(1); }
src = src.replace(doorOld, doorNew);

// ── ③ push 里 wall 用局部变量，并**透传 cell** ──
// cell 为什么必须带上：`__m5.makeRoomCollider` 用 `level.doors[].cell` 组成门洞集合，
// 再由 `roomWalls` 在墙上切出缺口。编译后的门若丢了这个字段，
// 门洞集合就是空的 → **墙全封死** → 玩家与怪都走不出房间（实测探针在此抛错）。
src = src.replace(`            room: room.id,
            wall: d.wall,
            widthM: w,`, `            room: room.id,
            wall: wall,
            cell: Array.isArray(d.cell) ? d.cell : null,
            widthM: w,`);

fs.writeFileSync(FILE, src, 'utf8');
console.log('[patch 001] 已应用：compileDoors 支持格子门 + rect 兼容已编译房间 + 弃用 NaN 门');
