#!/usr/bin/env node
/**
 * 002-monster-unstick.mjs — 怪物撞墙卡死时的**侧向绕行**（unstick）
 *
 * ## 缺陷（探针实测证据）
 * `tools/nav-probe.mjs` 让缝匠从 lobby 走向门洞 (6.5,5.5)，实测轨迹：
 *   0s(8.0,3.5) → 2s(7.6,5.0) → 3s 起永久停在 (8.7,5.0)，最终距离目标 4.3m，卡死 57.9s。
 * 逐轴实测该点的碰撞响应：
 *   西(-x) 可走 ✓ ｜ 东(+x) 被挡 ｜ 北(-z) 被挡 ｜ 南(+z) 被挡
 * 原因：lobby 南墙段（x∈[7,14], z=5.5）把怪的 z 推向「探针半径 0.34」的边界，
 * 于是**朝斜后方目标移动时 z 分量恒为 0**；怪只会沿 x 慢慢滑，
 * 却永远不会"意识到"自己没在靠近目标，也就永远到不了路点 → 卡死。
 *
 * ## 修法
 * 在 `MonsterBrain._moveToward` 里加**停滞检测 + 侧向绕行**：
 *   · 记录"到目标的历史最近距离"；若 0.35s 内没能再靠近 2cm，判定为停滞
 *   · 停滞时改用**垂直方向**位移（左右交替，确定性），持续 0.6s，帮助它绕过墙角
 *   · mover 照旧做碰撞解析：绕行方向若被挡就自然无效，不会穿墙
 * 为什么不直接在游戏循环里改：导航纪律属于状态机自身的职责；
 * 放在这里对玩家使用同一 mover 的所有调用方都生效，且仍可被纯逻辑测试覆盖。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'src/modules/__m3.js');
const MARK = '/* PATCH 002: monster-unstick */';

let src = fs.readFileSync(FILE, 'utf8');
if (src.includes(MARK)) { console.log('[patch 002] 已应用（检测到标记），跳过'); process.exit(0); }

// ── ① 构造器里初始化停滞状态 ──
const ctorOld = `        this.aggroLockUntil = null;      // 挑衅者人格：持续吸引仇恨 5 秒
      }`;
const ctorNew = `        this.aggroLockUntil = null;      // 挑衅者人格：持续吸引仇恨 5 秒
        ${MARK}
        // 停滞检测（见 _moveToward）：记录到目标的历史最近距离与停滞起始 tick
        this._nav = { side: 1, lastSidestepTick: -1e9, lastPos: null, stuckTicks: 0 };
      }`;
if (!src.includes(ctorOld)) { console.error('[patch 002] ✗ 未找到构造器锚点'); process.exit(1); }
src = src.replace(ctorOld, ctorNew);

// ── ② _moveToward：**先直推，真卡住才绕行** ──
const moveOld = `      _moveToward(target, maxStep) {
        if (typeof this.mover === 'function') {
          const res = this.mover({ x: this.position.x, z: this.position.z }, target, maxStep);`;
const moveNew = `      _moveToward(target, maxStep) {
        ${MARK}
        // ── 绕行策略（按实测教训定的顺序）──
        // 教训：第一版一遇到"停滞"（含缓慢但持续的接近）就垂直推离目标，
        // 结果怪在离门洞 1.5m 处被反复推歪、来回震荡，反而永远到不了。
        // 实测：只要一路直推，40 帧即可走到门洞 0.135m 以内 —— 所以**默认永远先直推**。
        //
        // 真正需要绕行的情形是"直推完全无效"（分离轴滑行把其中一个轴卡死，位移≈0）。
        // 且绕行只短暂执行，随后立刻回到直推（避免把"接近"误判成"卡住"）。
        const nav = (this._nav = this._nav ?? { side: 1, lastSidestepTick: -1e9, lastPos: null, stuckTicks: 0 });
        const dtTick = cfg('network.tickRate', 60);
        const tX = target.x - this.position.x, tZ = target.z - this.position.z;
        const tD = Math.hypot(tX, tZ) || 1;
        if (nav.lastPos) {
          const movedSince = Math.hypot(this.position.x - nav.lastPos.x, this.position.z - nav.lastPos.z);
          if (movedSince < maxStep * 0.15) nav.stuckTicks++; else nav.stuckTicks = 0;
        }
        nav.lastPos = { x: this.position.x, z: this.position.z };

        // 连续 0.25s 位移不足 → 判定真卡住，执行一次绕行（之后立刻回到直推）
        const reallyStuck = nav.stuckTicks > 0.25 * dtTick && tD > 0.5;
        const cooling = this.tick - nav.lastSidestepTick < 0.6 * dtTick;
        if (reallyStuck && !cooling && typeof this.mover === 'function') {
          nav.stuckTicks = 0;
          const s = nav.side;
          const ux = tX / tD, uz = tZ / tD;
          const px = -uz, pz = ux;
          const dirs = [
            [ux * 0.35 + px * 0.94 * s, uz * 0.35 + pz * 0.94 * s],
            [ux * 0.35 - px * 0.94 * s, uz * 0.35 - pz * 0.94 * s],
            [px * s, pz * s],
            [-ux, -uz],
          ];
          for (const [dx, dz] of dirs) {
            const len = Math.hypot(dx, dz) || 1;
            const res = this.mover({ x: this.position.x, z: this.position.z },
              { x: this.position.x + (dx / len) * maxStep * 10, z: this.position.z + (dz / len) * maxStep * 10 }, maxStep);
            if (!res || !Number.isFinite(res.x) || !Number.isFinite(res.z)) continue;
            const moved = Math.hypot(res.x - this.position.x, res.z - this.position.z);
            if (moved > maxStep * 0.5) {
              this.position.x = res.x; this.position.z = res.z;
              nav.lastSidestepTick = this.tick;
              return { x: round3(this.position.x), z: round3(this.position.z), blocked: false, sidestep: true };
            }
          }
          nav.side = -nav.side;
          nav.lastSidestepTick = this.tick;
        }
        if (typeof this.mover === 'function') {
          const res = this.mover({ x: this.position.x, z: this.position.z }, target, maxStep);`;
if (!src.includes(moveOld)) { console.error('[patch 002] ✗ 未找到 _moveToward 锚点'); process.exit(1); }
src = src.replace(moveOld, moveNew);

fs.writeFileSync(FILE, src, 'utf8');
console.log('[patch 002] 已应用：怪物停滞检测 + 侧向绕行');
