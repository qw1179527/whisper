#!/usr/bin/env node
/**
 * 005-door-clearance.mjs — 门洞两侧让出「代理半径」的通行余量
 *
 * ## 缺陷（探针逐轴实测）
 * 门洞按**整格**切（1.0m 宽），而碰撞用「胶囊半径 0.34 + 墙厚 0.22」判定：
 *   实际可通行宽度 = 1.0 − 2×0.34 = 0.32m，可用窗口只剩 x∈[6.34, 6.66]（**3cm 级刀锋**）。
 * 实测：怪在 (6.63,4.90) 朝门洞前进时，x 轴被门框挡住
 * （`6.63 + 0.34 = 6.97 < 7.00` —— 差 3cm 就能过），于是贴着门框"磨"而进不去，
 * 表现就是"怪卡在门口/寻路像坏的"。
 *
 * ## 修法
 * 把与门格相邻的墙段端部各**让出 `doorMargin`（默认 0.34 = 代理半径）**，
 * 使门洞的实际可通行宽度 ≈ 1.0 + 2×0.34 − 2×0.34 = 1.0m，与视觉上的门洞一致。
 * 只对"与门格相邻的那一端"收缩，普通墙角不受影响（不会把实体墙缩短）。
 * 余量可通过 opts.doorMargin 调（玩家/怪共用同一碰撞器与同一余量，保持一致）。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'src/modules/__m5.js');
const MARK = '/* PATCH 005: door-clearance */';

let src = fs.readFileSync(FILE, 'utf8');
if (src.includes(MARK)) { console.log('[patch 005] 已应用（检测到标记），跳过'); process.exit(0); }

const oldCut = `        const cut = (orientation, line, from, to, doorCoord) => {
          let runStart = null;
          for (let t = from; t <= to; t++) {
            const cellIsDoor = t < to && isDoor(
              orientation === 'h' ? t : doorCoord,
              orientation === 'h' ? doorCoord : t,
            );
            if (cellIsDoor) {
              if (runStart !== null) { spans.push(mkSpan(orientation, line, runStart, t)); runStart = null; }
            } else if (runStart === null) {
              runStart = t;
            }
          }
          if (runStart !== null) spans.push(mkSpan(orientation, line, runStart, to));
        };`;
const newCut = `        ${MARK}
        // 门洞余量：与门格相邻的墙段端部各让出 doorMargin，否则胶囊半径会把 1m 门洞吃成 0.32m
        // （实测可用窗口只剩 3cm，怪贴门框磨而进不去）。
        const doorMargin = Math.max(0, opts.doorMargin ?? 0.34);
        const cut = (orientation, line, from, to, doorCoord) => {
          const doorAt = (t) => isDoor(
            orientation === 'h' ? t : doorCoord,
            orientation === 'h' ? doorCoord : t,
          );
          let runStart = null;
          const flush = (endT, startT) => {
            // 仅当该段端部紧邻门格时才收缩
            const shrinkStart = startT > from && doorAt(startT - 1) ? doorMargin : 0;
            const shrinkEnd = doorAt(endT) ? doorMargin : 0;
            const a = startT + shrinkStart;
            const b = endT - shrinkEnd;
            if (b - a > 0.02) spans.push(mkSpan(orientation, line, a, b));
          };
          for (let t = from; t <= to; t++) {
            const cellIsDoor = t < to && doorAt(t);
            if (cellIsDoor) {
              if (runStart !== null) { flush(t, runStart); runStart = null; }
            } else if (runStart === null) {
              runStart = t;
            }
          }
          if (runStart !== null) flush(to, runStart);
        };`;
if (!src.includes(oldCut)) { console.error('[patch 005] ✗ 未找到 cut 锚点'); process.exit(1); }
src = src.replace(oldCut, newCut);
fs.writeFileSync(FILE, src, 'utf8');
console.log('[patch 005] 已应用：门洞两侧让出通行余量');
