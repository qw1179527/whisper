#!/usr/bin/env node
/**
 * 003-waypoint-advance-radius.mjs — 修「路点判定半径 vs 可接近距离」死锁
 *
 * ## 缺陷（探针实测）
 * 路点推进条件是 `dist < 0.6`，而物理上永远进不到 0.6 以内
 * （碰撞半径 0.34 + 墙厚 0.22 ≈ 0.62 是贴门洞中心的极限）。
 * 实测：怪在 0.63 附近反复磨，57.9s 停在同一点，永远不推进到下一个路点。
 *
 * ## 修法
 * 改为「已到当前路点 / 明确更近 / 停滞兜底」三判据。
 *
 * ## ⚠ 作用域纪律（我在这一点上连崩三个版本，务必照做）
 * 这段判定**必须内联在 startGame 内部**，因为它要用 `config`。
 *   · `config` 是 `startGame(config, tokens)` 的**形参**，只有 startGame 及其**嵌套**函数可见；
 *   · `__m12` 里 `cfg` 根本不存在（那是 __m3/__m4 等模块从 require("__m0").cfg 取的别名）；
 *   · 我先后把这段判定写成「模块级函数里用 config」「模块级函数里用 cfg」——
 *     两种都炸（config is not defined / cfg is not defined），因为 `shouldAdvanceWaypoint`
 *     定义在模块级（4 空格缩进），**不是** startGame 的嵌套函数。
 * 所以：不要为它新建模块级函数；直接把表达式内联到两个调用点。
 * 若将来确实需要抽出函数，必须把 tickRate 之类的值**作为参数传进去**，而不是依赖外层名字。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'src/modules/__m12.js');
const MARK = '/* PATCH 003: waypoint-advance */';

let src = fs.readFileSync(FILE, 'utf8');
if (src.includes(MARK)) { console.log('[patch 003] 已应用（检测到标记），跳过'); process.exit(0); }

/** 停滞兜底表达式（内联用；tickRate 用 config —— 调用点都在 startGame 内，可见） */
const STALL_EXPR = "ms.wpStallAt != null && brain.tick - ms.wpStallAt > 2.5 * (config.network?.tickRate ?? 60)";
/** 到达判据：已到当前路点，或明确更近（擦过路点/绕墙角），或停滞兜底 */
const ADVANCE_EXPR = (wpsVar, idxVar, brainVar, msVar) => `(() => {
              const __wps = ${wpsVar};
              if (!__wps || !__wps.length) return false;
              const __idx = Math.min(${idxVar}, __wps.length - 1);
              const __wp = __wps[__idx];
              if (!__wp) return false;
              const __d = Math.hypot(__wp.x - ${brainVar}.position.x, __wp.z - ${brainVar}.position.z);
              if (__d < 0.9) { ${msVar}.wpStallAt = null; return true; }
              const __next = __wps[__idx + 1];
              if (__next) {
                const __dn = Math.hypot(__next.x - ${brainVar}.position.x, __next.z - ${brainVar}.position.z);
                if (__dn < __d * 0.5 && __d < 2.0) { ${msVar}.wpStallAt = null; return true; }
              }
              if (${msVar}.wpStallAt == null) ${msVar}.wpStallAt = ${brainVar}.tick;
              else if (${STALL_EXPR}) { ${msVar}.wpStallAt = null; return true; }
              return false;
            })()`;

// ① 巡逻/调查分支
const a1 = `            const wp = ms.waypoints[ms.wpIndex];
            if (Math.hypot(wp.x - brain.position.x, wp.z - brain.position.z) < 0.6) ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);`;
const a2 = `            const wp = ms.waypoints[ms.wpIndex];
            ${MARK}
            // 三判据推进（内联：本段在 startGame 内，config 可见）。注释必须独占一行 ——
            // 我把中文说明跟在块注释后面写过一次，直接成了非法 token（源树执行失败）。
            if (${ADVANCE_EXPR('ms.waypoints', 'ms.wpIndex', 'brain', 'ms')}) ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);`;
if (!src.includes(a1)) { console.error('[patch 003] ✗ 未找到分支①锚点'); process.exit(1); }
src = src.replace(a1, a2);

// ② 追击/前往已知位置分支
const b1 = `              const wp = ms.waypoints[ms.wpIndex];
              if (wp && Math.hypot(wp.x - brain.position.x, wp.z - brain.position.z) < 0.7) {
                ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);
              }`;
const b2 = `              const wp = ms.waypoints[ms.wpIndex];
              ${MARK}
              // 三判据推进（内联同上）
              if (wp && ${ADVANCE_EXPR('ms.waypoints', 'ms.wpIndex', 'brain', 'ms')}) {
                ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);
              }`;
if (!src.includes(b1)) { console.error('[patch 003] ✗ 未找到分支②锚点'); process.exit(1); }
src = src.replace(b1, b2);

fs.writeFileSync(FILE, src, 'utf8');
console.log('[patch 003] 已应用：路点推进三判据（内联在 startGame 内，不新建模块级函数）');
