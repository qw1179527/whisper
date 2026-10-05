#!/usr/bin/env node
/**
 * hearing-port-vectors.mjs — 听觉索敌（V9 §7 表7-2）的移植等价性锁定
 *
 * 方法同 voice-port-vectors：灰盒 `__m2.canHear` 与 C# 移植件吃同一份输入（1716 例），逐例比对。
 * 用法：node tools/hearing-port-vectors.mjs [--update-reference]
 */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const INPUTS = path.join(ROOT, 'data/vectors/hearing.inputs.json');
const REF = path.join(ROOT, 'data/vectors/hearing.reference.json');
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
const seg = baseline.slice(baseline.indexOf('__tables["__m2"]'), baseline.indexOf('__tables["__m3"]'))
  .replace('__tables["__m2"] = function (mod) {', 'function (mod) {');
const mod = { exports: {} };
const req = () => ({ cfg: (p, fb) => { const parts = String(p).split('.'); let cur = CFG; for (const k of parts) { if (cur == null || typeof cur !== 'object' || !(k in cur)) return fb; cur = cur[k]; } return cur; } });
vm.runInThisContext('(function (mod, __req) {\nvar f = ' + seg + ';\nf(mod);\n})')(mod, req);
const M2 = mod.exports;

const out = [];
for (const c of spec.cases) {
  const stim = {
    sourceKey: c.sourceKey, intensity: c.intensity, radiusM: c.radiusM,
    globalBroadcast: c.globalBroadcast, position: { x: c.stimPos[0], z: c.stimPos[1] },
  };
  const r = M2.canHear(c.monsterId, stim, { x: c.monsterPos[0], z: c.monsterPos[1] },
    { perceptionBonus: c.perceptionBonus, localizationPenalty: c.localizationPenalty });
  out.push({
    audible: r.audible, reason: r.reason === undefined ? null : r.reason,
    distanceM: Number(r.distanceM.toFixed(6)),
    threshold: Number(r.effectiveThreshold.toFixed(6)),
    margin: Number((r.margin ?? 0).toFixed(6)),
    // 灰盒在「阈值不足」路径不返回 attenuationDb（undefined）= 无衰减；C# 给字面 0，数值语义相同 → 统一为 0
    attenuationDb: r.attenuationDb === undefined || r.attenuationDb === null ? 0 : Number(r.attenuationDb.toFixed(6)),
  });
}

if (updateRef || !fs.existsSync(REF)) {
  fs.writeFileSync(REF, JSON.stringify({ _note: '灰盒 __m2.canHear 在 hearing.inputs.json 上的逐例输出', cases: out }, null, 1) + '\n', 'utf8');
  console.log(`[hearing-vectors] 参考输出已写入 ${path.relative(ROOT, REF)}`);
}
const ref = JSON.parse(fs.readFileSync(REF, 'utf8')).cases;

let csOut;
try {
  // 经 bash 调用：Windows 无 shebang 支持，直接 execFileSync 一个 .sh 会静默失败
  const res = execFileSync('bash', [path.join(ROOT, 'native/dotnet.sh'),
    'run', '--project', path.join(ROOT, 'native/csharp-verify'), '--nologo', '-c', 'Release', '--', '--emit-hearing-vectors'],
    { encoding: 'utf8', cwd: ROOT, stdio: 'pipe' });
  csOut = JSON.parse(res.slice(res.indexOf('{'))).cases;
} catch (e) {
  console.log('[hearing-vectors] C# 跑手执行失败：\n' + String(e.stdout ?? '').slice(-600) + String(e.stderr ?? '').slice(-300));
  process.exit(1);
}

const diffs = [];
const near = (a, b, tol = 1e-3) => (a == null && b == null) || (a != null && b != null && Math.abs(a - b) <= tol);
for (let i = 0; i < ref.length; i++) {
  const a = ref[i], b = csOut[i], c = spec.cases[i];
  if (!b) { diffs.push(`例 ${i} 在 C# 输出中缺失`); continue; }
  if (a.audible !== b.audible) diffs.push(`例 ${i}（${c.monsterId}/${c.sourceKey}/d=${c.stimPos[0]}/pb=${c.perceptionBonus}/lp=${c.localizationPenalty}）audible 不同：JS ${a.audible} vs C# ${b.audible}`);
  else if ((a.reason ?? null) !== (b.reason ?? null)) diffs.push(`例 ${i} reason 不同：JS ${a.reason} vs C# ${b.reason}`);
  else if (!near(a.threshold, b.threshold) || !near(a.margin, b.margin, 5e-3) || !near(a.attenuationDb, b.attenuationDb, 5e-3))
    diffs.push(`例 ${i} 数值不同：thr ${a.threshold}/${b.threshold} · margin ${a.margin}/${b.margin} · att ${a.attenuationDb}/${b.attenuationDb}`);
}
console.log(`[hearing-vectors] 比对 ${ref.length} 例`);
console.log(`[hearing-vectors] 差异 ${diffs.length} 处${diffs.length === 0 ? ' ✅ C# 听觉索敌与灰盒实现一致' : ' ❌'}`);
for (const d of diffs.slice(0, 10)) console.log('  ✗ ' + d);
process.exit(diffs.length === 0 ? 0 : 1);
