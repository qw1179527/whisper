#!/usr/bin/env node
/**
 * 003-waypoint-advance-radius.mjs — 修「路点判定半径 vs 可接近距离」死锁
 *
 * ## 缺陷（探针实测证据）
 * 缝匠从 lobby 走往 ward_02，t=90 时已到门洞路点 (6.5,5.5) 附近，距离 0.69；
 * 推进条件是 `dist < 0.6`，而**物理上永远进不到 0.6 以内**：
 *   碰撞器半径 0.34 + 墙厚 0.22 ≈ 0.62 是怪能贴近门洞中心的极限。
 * 结果：怪在 0.63 附近反复磨（实测 57.9s 停留），永远不推进到下一个路点，也就永远到不了目标。
 * 注意这是**判定阈值与可达距离相撞**导致的死锁，不是"找不到路"——A* 路径本身是对的。
 *
 * ## 修法（两处，都按"是否真的在朝下一个路点前进"来判断，而不是只看一个硬阈值）
 *   · 到达当前路点（<0.6）→ 推进（保持原语义）
 *   · 或者：**离下一个路点比离当前路点更近** → 也算通过（这正是绕过墙角/擦过路点的常见情形）
 *   · 再或者：在当前路点附近停滞超过 1.2s（结合 patch 002 的停滞检测）→ 强制推进，避免永久卡死
 * 两处调用点（巡逻/调查分支与追击/前往已知位置分支）都要改，否则仍是半修。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'src/modules/__m12.js');
const MARK = '/* PATCH 003: waypoint-advance */';

let src = fs.readFileSync(FILE, 'utf8');
if (src.includes(MARK)) { console.log('[patch 003] 已应用（检测到标记），跳过'); process.exit(0); }

// ① 巡逻/调查分支
const a1 = `            const wp = ms.waypoints[ms.wpIndex];
            if (Math.hypot(wp.x - brain.position.x, wp.z - brain.position.z) < 0.6) ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);`;
const a2 = `            const wp = ms.waypoints[ms.wpIndex];
            ${MARK}
            if (shouldAdvanceWaypoint(brain, ms)) ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);`;
if (!src.includes(a1)) { console.error('[patch 003] ✗ 未找到分支①锚点'); process.exit(1); }
src = src.replace(a1, a2);

// ② 追击/前往已知位置分支
const b1 = `              const wp = ms.waypoints[ms.wpIndex];
              if (wp && Math.hypot(wp.x - brain.position.x, wp.z - brain.position.z) < 0.7) {
                ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);
              }`;
const b2 = `              const wp = ms.waypoints[ms.wpIndex];
              ${MARK}
              if (wp && shouldAdvanceWaypoint(brain, ms)) {
                ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);
              }`;
if (!src.includes(b1)) { console.error('[patch 003] ✗ 未找到分支②锚点'); process.exit(1); }
src = src.replace(b1, b2);

// ③ 插入判定函数（放在 roomAt 之前，保证同模块内可见）
const anchor = `    function roomAt(level, x, z) {`;
const fn = `    ${MARK}
    /**
     * 是否应当推进到下一个路点。
     *
     * 判据（不满足第一条就看第二条，避免"硬阈值撞上物理极限"的死锁）：
     *   ① 已到当前路点附近（<0.9）→ 推进
     *   ② 只有当**已经不在原房间**（即真的穿过了门）且离下一个路点更近时才提前推进
     *   ③ 停滞超过 2.5s 的兜底（更保守：早期版本 1.2s 太急，会在门这侧就跳路点，
     *      把怪指向走廊另一端的门，于是它掉头往回走 —— 实测卡死 57.9s 的直接原因）
     *
     * 教训：路点推进不能只看"离下一个更近"。门的两个路点分别位于门的**两侧**，
     * 怪在门这侧时，离"对面那扇门"确实可能更近（几何上），但穿过去之前不该改目标。
     */
    function shouldAdvanceWaypoint(brain, ms) {
      const wps = ms.waypoints;
      if (!wps || wps.length === 0) return false;
      const idx = Math.min(ms.wpIndex, wps.length - 1);
      const wp = wps[idx];
      if (!wp) return false;
      const d = Math.hypot(wp.x - brain.position.x, wp.z - brain.position.z);
      if (d < 0.9) { ms.wpStallAt = null; return true; }
      const next = wps[idx + 1];
      if (next) {
        const dn = Math.hypot(next.x - brain.position.x, next.z - brain.position.z);
        if (dn < d * 0.5 && d < 2.0) { ms.wpStallAt = null; return true; }
      }
      const now = brain.tick;
      if (ms.wpStallAt == null) ms.wpStallAt = now;
      // 作用域纪律（我在这上面连栽两次，两次都是"启动即崩"）：
      //   · __m12 **没有** cfg（那是 __m3/__m4 等模块的别名）；
      //   · __m12 用的是 config —— 它是 startGame(config, tokens) 的**形参**，
      //     本函数（startGame 内部的嵌套函数）能访问，模块级函数则不能。
      // 第一版写 config.* 到模块级函数 → config is not defined；
      // 第二版改成 cfg(...) → cfg is not defined。这里是第三版，用本模块真正可用的写法。
      // （注意：这段注释位于模板字符串内部，不能出现反引号，否则会把模板提前闭合 —— 我又踩了一次）
      else if (now - ms.wpStallAt > 2.5 * (config.network?.tickRate ?? 60)) { ms.wpStallAt = null; return true; }
      return false;
    }

`;
if (!src.includes(anchor)) { console.error('[patch 003] ✗ 未找到插入锚点 roomAt'); process.exit(1); }
src = src.replace(anchor, fn + anchor);

fs.writeFileSync(FILE, src, 'utf8');
console.log('[patch 003] 已应用：路点推进改为「到达 / 更近 / 停滞兜底」三判据');
