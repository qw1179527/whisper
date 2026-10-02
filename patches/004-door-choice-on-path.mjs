#!/usr/bin/env node
/**
 * 004-door-choice-on-path.mjs — 多门相邻时选**几何最近的门**，而不是第一条匹配
 *
 * ## 缺陷（探针实测证据）
 * `roomPathToWaypoints` 用 `graph.links.find(...)` 取**第一条**匹配的连边来定位门。
 * 实测 lobby → corridor_main 时选出的是 ward_02 的门 (14.5,5.5)，
 * 而 lobby 自己的门在 (6.5,5.5) —— 怪被送去走廊另一端，路径方向完全错。
 * 根因：同两个房间之间可能有多扇门（本关 13 门 / 13 房间，存在多门相邻），
 * `find` 的"第一条"与几何上该走哪扇门无关。
 *
 * ## 修法
 * 对该段所有候选连边求「门中心到两端房间中心连线」的垂距，取最小者。
 * 这是纯几何判据、确定性，且不改变 A* 的房间级路径（只改"从哪扇门穿过去"）。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'src/modules/__m4.js');
const MARK = '/* PATCH 004: door-choice-on-path */';

let src = fs.readFileSync(FILE, 'utf8');
if (src.includes(MARK)) { console.log('[patch 004] 已应用（检测到标记），跳过'); process.exit(0); }

const oldFn = `    function roomPathToWaypoints(level, path) {
      if (!path) return [];
      const pts = [];
      for (let i = 0; i < path.length - 1; i++) {
        const [a, b] = [path[i], path[i + 1]];
        const link = level.graph.links.find((l) => (l.a === a && l.b === b) || (l.a === b && l.b === a));
        if (link) {
          const door = level.doors.find((d) => d.id === link.door);
          if (door) pts.push({ x: door.pos.x, z: door.pos.z });
        }
      }
      pts.push(roomCenter(level, path[path.length - 1]));
      return pts;
    }`;

const newFn = `    function roomPathToWaypoints(level, path) {
      ${MARK}
      if (!path) return [];
      const pts = [];
      const ca = (id) => roomCenter(level, id);
      // 点到线段的垂距（用于判断哪扇门"在这条路上"）
      const perpDist = (p, s, e) => {
        const vx = e.x - s.x, vz = e.z - s.z;
        const len2 = vx * vx + vz * vz;
        if (len2 === 0) return Math.hypot(p.x - s.x, p.z - s.z);
        let t = ((p.x - s.x) * vx + (p.z - s.z) * vz) / len2;
        t = Math.max(0, Math.min(1, t));
        return Math.hypot(p.x - (s.x + vx * t), p.z - (s.z + vz * t));
      };
      for (let i = 0; i < path.length - 1; i++) {
        const [a, b] = [path[i], path[i + 1]];
        // 同两房之间可能有多扇门：取「门中心离两房中心连线最近」的那扇。
        // 原来的 .find() 取第一条匹配 —— 实测把怪送去走廊另一端（lobby 的门在 6.5，却选到 14.5）。
        const cands = level.graph.links.filter((l) => (l.a === a && l.b === b) || (l.a === b && l.b === a));
        const sa = ca(a), sb = ca(b);
        let bestDoor = null, bestD = Infinity;
        for (const l of cands) {
          const door = level.doors.find((d) => d.id === l.door);
          if (!door) continue;
          const d = perpDist(door.pos, sa, sb);
          if (d < bestD) { bestD = d; bestDoor = door; }
        }
        if (bestDoor) pts.push({ x: bestDoor.pos.x, z: bestDoor.pos.z });
      }
      pts.push(roomCenter(level, path[path.length - 1]));
      return pts;
    }`;

if (!src.includes(oldFn)) { console.error('[patch 004] ✗ 未找到 roomPathToWaypoints 原文'); process.exit(1); }
src = src.replace(oldFn, newFn);
fs.writeFileSync(FILE, src, 'utf8');
console.log('[patch 004] 已应用：多门相邻时按几何最近选门');
