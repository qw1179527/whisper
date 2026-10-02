#!/usr/bin/env node
/**
 * nav-probe.mjs — 怪物导航仿真探针（无渲染、确定性）
 *
 * 为什么要它：用户反馈"鬼的寻路有问题"。灰盒的导航散在 `__m12` 的 updateMonsters 里
 * （房间级 A* → 门中心路点 → resolve 碰撞滑行），靠读代码判断"到底卡不卡"不可靠。
 * 这里把真实模块（__m4 关卡/图/路径、__m5 碰撞、__m3 状态机）在 Node 里跑起来，
 * 直接观察"怪能不能从 A 房走到 B 房"，并输出卡住位置与原因。
 *
 * 用法：node tools/nav-probe.mjs [--from 房间id] [--to 房间id] [--seconds 60]
 */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const argOf = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };

const baseline = fs.readFileSync(path.join(ROOT, 'baseline/game-0.6.0.js'), 'utf8');
const cfgLiteral = (() => {
  const at = baseline.indexOf('var __CFG = ');
  let i = baseline.indexOf('{', at), d = 0, mode = null;
  for (; i < baseline.length; i++) {
    const c = baseline[i], n = baseline[i + 1];
    if (mode) { if (c === '\\') { i++; continue; } if (c === mode) mode = null; continue; }
    if (c === '/' && n === '/') { mode = '//'; i++; continue; }
    if (c === '/' && n === '*') { mode = '/*'; i++; continue; }
    if (c === '"' || c === "'" || c === '`') { mode = c; continue; }
    if (c === '{') d++; else if (c === '}') { d--; if (!d) break; }
  }
  return baseline.slice(baseline.indexOf('{', at), i + 1);
})();
const CFG = vm.runInNewContext('(' + cfgLiteral + ')', Object.create(null));
const cfgFn = (p, fb) => { const parts = String(p).split('.'); let cur = CFG; for (const k of parts) { if (cur == null || typeof cur !== 'object' || !(k in cur)) return fb; cur = cur[k]; } return cur; };

/**
 * 装载灰盒模块：直接读仓库里已切分好的 `src/modules/*.js`（构建产物，与 baseline 逐字节可逆），
 * 用自定义 require 满足它们的 `require("__mN")` 依赖。
 *
 * 为什么不从 baseline/game-0.6.0.js 手工切片：那需要精确的大括号配对与字符串/注释跳过，
 * 我在第一版里就切错了（报"模块体未闭合"）。既然 split-modules.mjs 已经做过这件事并逐字节验证过，
 * 直接复用才是正解 —— 不重复造易错的轮子。
 */
function loadModules(want) {
  const cache = {};
  const load = (id) => {
    if (cache[id]) return cache[id];
    const file = path.join(ROOT, 'src/modules', id + '.js');
    if (!fs.existsSync(file)) throw new Error('缺模块文件 ' + file);
    const src = fs.readFileSync(file, 'utf8');
    const mod = { exports: {} };
    cache[id] = mod.exports;              // 先占位，容忍循环依赖
    const fn = vm.runInThisContext('(function (module, exports, require) {\n' + src + '\n})', { filename: file });
    fn(mod, mod.exports, (dep) => (dep === '__m0' ? { cfg: cfgFn } : load(dep)));
    cache[id] = mod.exports;
    return mod.exports;
  };
  for (const m of want) load(m);
  return cache;
}

const M = loadModules(['__m1', '__m2', '__m3', '__m4', '__m5']);

/**
 * 关卡数据来源：`baseline/index-0.6.0.html` 里内联的 `__WHISPER_BOOTSTRAP__`
 * —— 这就是游戏运行时真正吃进去的那一份（不是仓库里的 Unity 关卡文件，两者结构不同）。
 */
const html = fs.readFileSync(path.join(ROOT, 'baseline/index-0.6.0.html'), 'utf8');
const bootAt = html.indexOf('__WHISPER_BOOTSTRAP__');
if (bootAt < 0) { console.error('index.html 未内联 __WHISPER_BOOTSTRAP__'); process.exit(1); }
const jsonStart = html.indexOf('{', bootAt);
let d = 0, mode = null, end = -1;
for (let i = jsonStart; i < html.length; i++) {
  const c = html[i], n = html[i + 1];
  if (mode) { if (c === '\\') { i++; continue; } if (c === mode) mode = null; continue; }
  if (c === '"') { mode = '"'; continue; }
  if (c === '{') d++; else if (c === '}') { d--; if (!d) { end = i; break; } }
}
const BOOT = JSON.parse(html.slice(jsonStart, end + 1));
const levelId = BOOT.levelId ?? 'asylum_v1';
/**
 * 关卡对象 = **原始 DSL**（不是 buildLevel 的产物）。
 * 依据：`__m12` 第 79 行直接取 `globalThis.__WHISPER_LEVEL__`（由 __m13 从内联 bootstrap 赋值），
 * 随后就用 `level.gridSize` / `level.grid` / 房间的 `rect` —— 这些字段只在原始 DSL 上存在。
 * 我第一版按 buildLevel 产物来跑，结果 `gridSize` undefined 直接抛错。
 * 房间的 `rect` 由 DSL 自带；门是格子形式 `{id, cell}`。
 */
const level = BOOT.level;
console.log(`[nav] 关卡数据：内联 __WHISPER_BOOTSTRAP__.level（${levelId}，与游戏运行时同一份）`);

console.log(`[nav] 关卡 ${levelId} · 房间 ${level.rooms.length} · 门 ${level.doors.length} · 房间图边 ${level.graph.links.length}`);
{
  const gs = level.gridSize ?? BOOT.level.gridSize;
  const gc = level.grid ?? BOOT.level.grid;
  const walkable = gc ? gc.flat().filter((c) => c !== '#').length : -1;
  console.log(`[nav] 网格 ${gs?.w ?? '?'}×${gs?.h ?? '?'} · 可走格 ${walkable}`);
}

// 碰撞解析：必须用**游戏真正在用的那个**函数。
// 我曾误用 makeCollider（它会读 doors[].cell 组成 locked 门障，且早期实测在此抛错），
// 而 __m12 第 98 行实际用的是 makeRoomCollider（由墙段 + 门洞集合构造）。
const resolve = M.__m5.makeRoomCollider(level, 0.34);

const fromId = argOf('--from', null);
const toId = argOf('--to', null);
const seconds = Number(argOf('--seconds', '60'));

/** 单次导航试验：从 from 房间中心走到 to 房间中心，走房间级 A* + 门中心路点 */
function trial(from, to, id = 'stitcher') {
  const a = level.rooms.find((r) => r.id === from);
  const b = level.rooms.find((r) => r.id === to);
  if (!a || !b) return { ok: false, reason: '房间不存在' };
  const start = { x: (a.rect.x0 + a.rect.x1) / 2, z: (a.rect.z0 + a.rect.z1) / 2 };
  const goal = { x: (b.rect.x0 + b.rect.x1) / 2, z: (b.rect.z0 + b.rect.z1) / 2 };

  const p = M.__m4.findRoomPath(level, from, to);
  if (!p) return { ok: false, reason: '房间图无路径（A* 失败）' };
  const wps = M.__m4.roomPathToWaypoints(level, p);

  const cfg = CFG.monsters[id];
  let waypoints = wps;
  const brain = new M.__m3.MonsterBrain(id, { position: start, patrolPoints: [wps[0] ?? goal], chaseSpeedScale: cfg.chaseSpeedScale });
  brain.mover = (f, t, step) => {
    const dx = t.x - f.x, dz = t.z - f.z;
    const d = Math.hypot(dx, dz) || 1;
    const want = { x: (dx / d) * step, z: (dz / d) * step };
    const got = resolve(f, want);
    const moved = Math.hypot(got.x - f.x, got.z - f.z);
    return { ...got, blocked: moved < step * 0.5 };
  };

  const tickRate = cfgFn('network.tickRate', 60);
  const dt = 1 / tickRate;
  const trail = [];
  let wpIndex = 0;
  let stuckTicks = 0, maxStuck = 0;
  let reached = false;
  const total = Math.round(seconds * tickRate);

  for (let tick = 0; tick < total; tick++) {
    // 与游戏一致：每 0.6s 依当前所在房间重算路径（walk 途中跨房后要换下一段）
    if (tick === 0 || tick % Math.round(0.6 * tickRate) === 0) {
      const here = M.__m4.roomAt(level, brain.position.x, brain.position.z);
      if (here && here.id !== to) {
        const p2 = M.__m4.findRoomPath(level, here.id, to);
        const w2 = M.__m4.roomPathToWaypoints(level, p2 ?? [here.id]);
        if (w2.length) { waypoints = w2; }
        else waypoints = wps;
      } else if (here && here.id === to) waypoints = [{ x: goal.x, z: goal.z }];
    }
    const wp = waypoints[Math.min(wpIndex, waypoints.length - 1)];
    if (Math.hypot(wp.x - brain.position.x, wp.z - brain.position.z) < 0.65) {
      if (process.env.NAV_WINDOW) console.log(`      [adv] t=${tick} 到达 wp${wpIndex} → 推进`);
      wpIndex = Math.min(wpIndex + 1, waypoints.length - 1);
    }
    brain.patrolPoints = [waypoints[Math.min(wpIndex, waypoints.length - 1)]];
    const before = { x: brain.position.x, z: brain.position.z };
    brain.state = 'investigate';           // 强制"朝目标走"，与服务端调查态一致
    brain.target = { x: waypoints[Math.min(wpIndex, waypoints.length - 1)].x, z: waypoints[Math.min(wpIndex, waypoints.length - 1)].z };
    const r = brain.step({ tick, seenPlayer: false });
    if (process.env.NAV_DEBUG === '1' && process.env.NAV_WINDOW) {
      const [lo, hi] = process.env.NAV_WINDOW.split('-').map(Number);
      if (tick >= lo && tick <= hi) {
        const w = waypoints[Math.min(wpIndex, waypoints.length - 1)];
        const dWp = Math.hypot(w.x - brain.position.x, w.z - brain.position.z);
        const next = waypoints[Math.min(wpIndex + 1, waypoints.length - 1)];
        const dNext = Math.hypot(next.x - brain.position.x, next.z - brain.position.z);
        console.log(`      [win] t=${tick} pos=(${brain.position.x.toFixed(2)},${brain.position.z.toFixed(2)}) wpIndex=${wpIndex} dWp=${dWp.toFixed(2)} dNext=${dNext.toFixed(2)} stallAt=${brain._nav ? 'n/a' : 'n/a'}`);
      }
    }
    if (process.env.NAV_DEBUG === '1' && tick % 30 === 0 && tick < 600) {
      const nav = brain._nav ?? {};
      const here = M.__m4.roomAt(level, brain.position.x, brain.position.z);
      console.log(`      [dbg] t=${tick} pos=(${brain.position.x.toFixed(2)},${brain.position.z.toFixed(2)}) room=${here ? here.id : '(空)'} wpIndex=${wpIndex}/${waypoints.length} wp=(${waypoints[Math.min(wpIndex, waypoints.length-1)].x},${waypoints[Math.min(wpIndex, waypoints.length-1)].z}) sidestep=${!!r.sidestep}`);
    }
    const moved = Math.hypot(r.position.x - before.x, r.position.z - before.z);
    if (moved < 0.005) { stuckTicks++; maxStuck = Math.max(maxStuck, stuckTicks); } else stuckTicks = 0;
    if (tick % 60 === 0) trail.push(`${(tick / tickRate).toFixed(0)}s(${r.position.x.toFixed(1)},${r.position.z.toFixed(1)})`);
    if (Math.hypot(goal.x - brain.position.x, goal.z - brain.position.z) < 0.8) { reached = true; break; }
  }
  return {
    ok: reached && maxStuck < 3 * tickRate,   // 验收：到达 **且** 不存在超过 3 秒的停滞
    stuckFail: maxStuck >= 3 * tickRate,
    reached,
    path: p, waypoints: wps.map((w) => `(${w.x.toFixed(1)},${w.z.toFixed(1)})`),
    final: `(${brain.position.x.toFixed(2)},${brain.position.z.toFixed(2)})`,
    goal: `(${goal.x.toFixed(2)},${goal.z.toFixed(2)})`,
    distToGoal: Math.hypot(goal.x - brain.position.x, goal.z - brain.position.z).toFixed(2),
    maxStuckSeconds: (maxStuck / tickRate).toFixed(1),
    trail: trail.length > 14 ? trail.slice(0, 8).join(' → ') + ' … ' + trail.slice(-4).join(' → ') : trail.join(' → '),
  };
}

const pairs = [];
if (fromId && toId) pairs.push([fromId, toId]);
else {
  // 默认：横跨整层的几组（含最远的入口↔太平间）
  const ids = level.rooms.map((r) => r.id);
  pairs.push([ids[0], ids[ids.length - 1]], [ids[0], ids[Math.floor(ids.length / 2)]], [ids[Math.floor(ids.length / 2)], ids[ids.length - 1]]);
}

let pass = 0, fail = 0;
for (const [f, t] of pairs) {
  const r = trial(f, t);
  const tag = r.ok ? '✓ 到达' : (r.stuckFail ? '✗ 到达但曾卡死' : '✗ 未到达');
  console.log(`\n[nav] ${f} → ${t}: ${tag} · 终点距离 ${r.distToGoal ?? '-'}m · 最长停滞 ${r.maxStuckSeconds ?? '-'}s`);
  if (r.reason) console.log(`      原因：${r.reason}`);
  if (r.path) console.log(`      A* 路径：${r.path.join(' → ')}`);
  if (r.waypoints) console.log(`      路点：${r.waypoints.join(' ')}`);
  if (r.trail) console.log(`      轨迹：${r.trail}`);
  if (r.final) console.log(`      终点 ${r.final} · 目标 ${r.goal}`);
  r.ok ? pass++ : fail++;
}
console.log(`\n[nav] 结果：到达 ${pass} · 未到达 ${fail}`);
process.exit(fail === 0 ? 0 : 1);
