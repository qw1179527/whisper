#!/usr/bin/env node
/**
 * compare-trajectory.mjs — 从两个 **bundle 产物文本**各自装配模块表，跑同一条刺激序列，
 * 比较三怪状态机的状态轨迹（这是"行为等价"里最硬的一层：不看源码、看产物本身跑出来的行为）。
 *
 * 为什么比源码级对拍更有力：源树可能与产物脱节（打包失误不会被源码对拍发现），
 * 而本脚本直接把 game.js 当输入——产物里 `__tables` 是自足闭包，可整段取出执行。
 *
 * 用法：node tools/compare-trajectory.mjs
 * 退出码：0 = 轨迹完全一致
 */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/module-manifest.json'), 'utf8'));
const TARGETS = [MANIFEST.meta.source, 'build/game.js'];

/** 从 bundle 文本里整段取出 「var __tables … 到入口引导之前」，在最小语境下执行，拿回模块表 */
function moduleTableOf(text) {
  const start = text.indexOf('var __tables = {}, __cache = {};');
  const end = text.indexOf('var __entry = __req(');
  if (start < 0 || end < 0) throw new Error('bundle 结构定位失败（找不到模块表或入口）');
  const seg = text.slice(start, end);
  const ctx = vm.createContext({ console: { log() {}, warn() {}, error() {} } });
  vm.runInContext(`${seg}\nglobalThis.__T = { __tables, __req };`, ctx);
  return ctx.__T;
}

/** 同一条确定性刺激序列（数值取 V9 附录 A-1：喊叫 80/25m、奔跑脚步 52/15m、耳语 10/4m） */
const SCRIPT = [
  { tick: 10, key: 'voice_shout', intensity: 80, position: { x: 8, z: 4 } },
  { tick: 40, key: null, intensity: 0, position: { x: 0, z: 0 } },
  { tick: 120, key: 'run_footstep', intensity: 52, position: { x: -6, z: 10 } },
  { tick: 400, key: null, intensity: 0, position: { x: 0, z: 0 } },
  { tick: 900, key: 'voice_whisper', intensity: 10, position: { x: 2, z: 2 } },
  { tick: 1500, key: 'voice_shout', intensity: 80, position: { x: 3, z: 3 } },
];

function replay(label) {
  const text = fs.readFileSync(path.join(ROOT, label), 'utf8');
  const { __tables, __req } = moduleTableOf(text);
  const m2 = __req('__m2');
  const m3 = __req('__m3');
  const cfgMod = __req('__m0');
  const cfg = cfgMod.loadConfig();

  const out = [];
  for (const id of Object.keys(cfg.monsters).filter((k) => !k.startsWith('_'))) {
    const brain = new m3.MonsterBrain(id, { position: { x: 0, z: 0 }, patrolPoints: [{ x: 0, z: 0 }] });
    const script = new Map(SCRIPT.map((s) => [s.tick, s]));
    const traj = [];
    for (let tick = 0; tick <= 1800; tick++) {
      const s = script.get(tick);
      if (s && s.key) {
        // makeStimulus(sourceKey, opts)：首参是配置里的 source 键（字符串）
        const stim = m2.makeStimulus(s.key, { position: s.position, tick });
        if (stim) brain.onStimulus(stim, tick);
      }
      brain.step({ tick });
      if (tick % 50 === 0) {
        const snap = brain.snapshot();
        traj.push(`${tick}:${snap.state}:${snap.position.x.toFixed(3)},${snap.position.z.toFixed(3)}`);
      }
    }
    out.push({ id, traj: traj.join('|') });
  }
  return { text, monsters: out, m2Exports: Object.keys(m2).sort(), m3Exports: Object.keys(m3).sort() };
}

const results = TARGETS.map((t) => ({ target: t, ...replay(t) }));
const [a, b] = results;

const diffs = [];
for (let i = 0; i < a.monsters.length; i++) {
  const ma = a.monsters[i];
  const mb = b.monsters[i];
  if (ma.id !== mb.id) diffs.push(`怪物顺序不同：${ma.id} vs ${mb.id}`);
  if (ma.traj !== mb.traj) {
    const pa = ma.traj.split('|');
    const pb = mb.traj.split('|');
    const first = pa.findIndex((v, k) => v !== pb[k]);
    diffs.push(`${ma.id} 轨迹在第 ${first} 个采样点起不同：baseline=${pa[first]} vs 重建=${pb[first]}`);
  }
}
if (a.m2Exports.join(',') !== b.m2Exports.join(',')) diffs.push('__m2 导出不同');
if (a.m3Exports.join(',') !== b.m3Exports.join(',')) diffs.push('__m3 导出不同');

const line = (m) => `${m.id}：采样 ${m.traj.split('|').length} 点，轨迹 sha256 ${crypto.createHash('sha256').update(m.traj).digest('hex').slice(0, 12)}`;
console.log('[traj] 从产物文本装配并回放刺激序列（1800 tick / 3 怪 / 6 次刺激）');
for (const m of a.monsters) console.log('  baseline ' + line(m));
for (const m of b.monsters) console.log('  重建     ' + line(m));
console.log(`[traj] 对齐结果：差异 ${diffs.length} 处${diffs.length === 0 ? ' ✅ 轨迹完全一致' : ' ❌'}`);
for (const d of diffs) console.log('  ✗ ' + d);
process.exit(diffs.length === 0 ? 0 : 1);
