#!/usr/bin/env node
/**
 * monster-port-vectors.mjs — 三怪状态机（V9 §7）的移植等价性锁定
 *
 * 方法同 voice/hearing：灰盒 `__m3.MonsterBrain` 与 C# 移植件吃同一份场景（21 例），
 * 比对「状态迁移清单 + 每 50 tick 的位置/速度采样」。状态机是确定性的，因此必须逐点一致。
 *
 * 用法：node tools/monster-port-vectors.mjs [--update-reference]
 */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const INPUTS = path.join(ROOT, 'data/vectors/monster-brain.inputs.json');
const REF = path.join(ROOT, 'data/vectors/monster-brain.reference.json');
const spec = JSON.parse(fs.readFileSync(INPUTS, 'utf8'));
const updateRef = process.argv.includes('--update-reference');

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

/** 装配 __m3（它依赖 __m2 的 canHear 与 __m0 的 cfg） */
function loadModules() {
  const segs = {};
  for (const m of ['__m2', '__m3']) {
    const a = baseline.indexOf(`__tables["${m}"]`);
    const b = baseline.indexOf(`__tables["__m${Number(m.slice(3)) + 1}"]`);
    segs[m] = baseline.slice(a, b > 0 ? b : undefined).replace(`__tables["${m}"] = function (mod) {`, 'function (mod) {');
  }
  const cache = {};
  const req = (id) => {
    if (cache[id]) return cache[id];
    const mod = { exports: {} };
    cache[id] = mod.exports;
    const deps = { __m0: () => ({ cfg: cfgFn }), __m2: () => req('__m2') };
    const fn = vm.runInThisContext('(function (mod, __req) {\nvar f = ' + segs[id] + ';\nf(mod);\n})');
    fn(mod, (dep) => (deps[dep] ? deps[dep]() : { cfg: cfgFn }));
    cache[id] = mod.exports;
    return mod.exports;
  };
  return req('__m3');
}
const M3 = loadModules();

const out = [];
for (const c of spec.cases) {
  const brain = new M3.MonsterBrain(c.monsterId, {
    position: { x: c.position[0], z: c.position[1] },
    patrolPoints: c.patrolPoints.map(([x, z]) => ({ x, z })),
  });
  const stimByTick = new Map(c.stimuli.map((s) => [s.tick, s]));
  const sightByTick = new Map(c.sights.map((s) => [s.tick, s]));
  const samples = [];
  for (let tick = 0; tick <= c.ticks; tick++) {
    const st = stimByTick.get(tick);
    if (st) {
      brain.onStimulus({
        sourceKey: st.sourceKey, intensity: st.intensity, radiusM: st.radiusM,
        globalBroadcast: st.globalBroadcast, position: { x: st.x, z: st.z },
      }, tick);
    }
    const sight = sightByTick.get(tick);
    const r = brain.step({ tick, seenPlayer: !!sight, playerPos: sight ? { x: sight.x, z: sight.z } : undefined });
    if (tick % c.sampleEvery === 0) samples.push([tick, r.state, Number(r.position.x.toFixed(3)), Number(r.position.z.toFixed(3)), Number(r.speed.toFixed(3))]);
  }
  out.push({
    id: `${c.monsterId}/${c.id}`,
    transitions: brain.history.map((h) => `${h.tick}:${h.from}->${h.to}`),
    samples,
    finalState: brain.state,
  });
}

if (updateRef || !fs.existsSync(REF)) {
  fs.writeFileSync(REF, JSON.stringify({ _note: '灰盒 __m3.MonsterBrain 在 monster-brain.inputs.json 上的逐例输出', cases: out }, null, 1) + '\n', 'utf8');
  console.log(`[monster-vectors] 参考输出已写入 ${path.relative(ROOT, REF)}`);
}
const ref = JSON.parse(fs.readFileSync(REF, 'utf8')).cases;

let csOut;
try {
  // 经 bash 调用：Windows 无 shebang 支持，直接 execFileSync 一个 .sh 会静默失败
  const res = execFileSync('bash', [path.join(ROOT, 'native/dotnet.sh'),
    'run', '--project', path.join(ROOT, 'native/csharp-verify'), '--nologo', '-c', 'Release', '--', '--emit-monster-vectors'],
    { encoding: 'utf8', cwd: ROOT, stdio: 'pipe' });
  csOut = JSON.parse(res.slice(res.indexOf('{'))).cases;
} catch (e) {
  console.log('[monster-vectors] C# 跑手执行失败：\n' + String(e.stdout ?? '').slice(-700) + String(e.stderr ?? '').slice(-300));
  process.exit(1);
}

// 比较口径（诚实说明）：
//   · 状态迁移清单与终态：必须**完全一致**（这是状态机的语义，不容忍差异）；
//   · 位置/速度采样：坐标是 3 位小数量化值，JS 用双精度、C# 用 float，
//     数百 tick 累积后第 4 位会有 ~1e-3 级抖动，落在量化边界上就会差 1 个千分位。
//     故对坐标用 0.002 容差并**报告观测到的最大偏差**——不假装 float 逐位相同，也不放过真实漂移。
const COORD_TOL = 0.002;
const diffs = [];
let maxDev = 0, maxDevAt = '';
for (let i = 0; i < ref.length; i++) {
  const a = ref[i], b = csOut[i];
  if (!b) { diffs.push(`例 ${i} 缺失`); continue; }
  if ((a.finalState ?? null) !== (b.finalState ?? null)) diffs.push(`${a.id} 终态不同：JS ${a.finalState} vs C# ${b.finalState}`);
  if (a.transitions.join('|') !== b.transitions.join('|')) {
    const ta = a.transitions, tb = b.transitions;
    let k = 0; while (k < Math.min(ta.length, tb.length) && ta[k] === tb[k]) k++;
    diffs.push(`${a.id} 状态迁移不同（第 ${k} 条起）：JS ${ta[k] ?? '(结束)'} vs C# ${tb[k] ?? '(结束)'}（迁移数 ${ta.length}/${tb.length}）`);
  }
  const sa = a.samples, sb = b.samples;
  if (sa.length !== sb.length) { diffs.push(`${a.id} 采样数不同：${sa.length}/${sb.length}`); continue; }
  for (let k = 0; k < sa.length; k++) {
    const [t1, st1, x1, z1, sp1] = sa[k];
    const [t2, st2, x2, z2, sp2] = sb[k];
    if (t1 !== t2 || st1 !== st2) { diffs.push(`${a.id} 采样点 ${k} 时刻/状态不同：JS ${sa[k].join(',')} vs C# ${sb[k].join(',')}`); break; }
    const dev = Math.max(Math.abs(x1 - x2), Math.abs(z1 - z2), Math.abs(sp1 - sp2));
    if (dev > maxDev) { maxDev = dev; maxDevAt = `${a.id}#${k}（JS ${x1},${z1},${sp1} / C# ${x2},${z2},${sp2}）`; }
    if (dev > COORD_TOL) { diffs.push(`${a.id} 采样点 ${k} 坐标偏差 ${dev.toFixed(4)} 超容差 ${COORD_TOL}：JS ${sa[k].join(',')} vs C# ${sb[k].join(',')}`); break; }
  }
}
console.log(`[monster-vectors] 比对 ${ref.length} 例（每例含状态迁移清单 + 采样点）`);
console.log(`[monster-vectors] 坐标最大偏差 ${maxDev.toFixed(5)}（${maxDevAt || '无'}），容差 ${COORD_TOL}`);
console.log(`[monster-vectors] 差异 ${diffs.length} 处${diffs.length === 0 ? ' ✅ C# 状态机与灰盒实现一致（状态迁移完全一致 · 坐标在浮点容差内）' : ' ❌'}`);
for (const d of diffs.slice(0, 10)) console.log('  ✗ ' + d);
process.exit(diffs.length === 0 ? 0 : 1);
