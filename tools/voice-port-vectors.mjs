#!/usr/bin/env node
/**
 * voice-port-vectors.mjs — 声纹判定链的移植等价性锁定
 *
 * 做法：
 *   ① 读 data/vectors/voice-classification.inputs.json（两端共用的确定性输入）
 *   ② 用**灰盒 JS 实现**（src/modules/__m1.js，已在 WebView 侧验证）跑出参考输出
 *   ③ 调本机 C# 跑手（native/csharp-verify --emit-voice-vectors）跑出移植件输出
 *   ④ 逐帧比对（数值容差 1e-3，档位必须完全一致），差异即移植缺陷
 *
 * 为什么这样做：手抄期望值会漏掉边界；让两端吃同一份输入才真正锁得住"行为等价"。
 *
 * 用法：node tools/voice-port-vectors.mjs [--update-reference]
 */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const INPUTS = path.join(ROOT, 'data/vectors/voice-classification.inputs.json');
const REF = path.join(ROOT, 'data/vectors/voice-classification.reference.json');
const spec = JSON.parse(fs.readFileSync(INPUTS, 'utf8'));
const updateRef = process.argv.includes('--update-reference');

// ── ① 从 baseline 产物里取 cfg（配置真源），在最小语境下装配 __m1 工厂 ──
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

/** 把 __m1 模块单独跑起来（只喂它需要的 __m0.cfg / stimulusSources） */
function loadVoiceModule() {
  const raw = baseline.slice(baseline.indexOf('__tables["__m1"]'), baseline.indexOf('__tables["__m2"]'));
  // 把 `__tables["__m1"] = function (mod) { ... };` 改写为可独立调用的形式：
  // 直接跑原文本会在赋值时解引用未定义的 __tables。
  const seg = raw.replace('__tables["__m1"] = function (mod) {', 'function (mod) {');
  const mod = { exports: {} };
  const req = () => ({ cfg: (p, fb) => {
    const parts = String(p).split('.');
    let cur = CFG;
    for (const k of parts) { if (cur == null || typeof cur !== 'object' || !(k in cur)) return fb; cur = cur[k]; }
    return cur;
  } });
  const fn = vm.runInThisContext('(function (mod, __req) {\nvar f = ' + seg + ';\nf(mod);\n})');
  fn(mod, req);
  return mod.exports;
}
const V = loadVoiceModule();

// ── ② 跑参考输出 ──
const cal = spec.calibration;
const out = { _note: '由 tools/voice-port-vectors.mjs 生成：灰盒 JS 实现（src/modules/__m1.js）在 inputs 上的逐帧输出', frameMs: spec.frameMs, devices: {} };

for (const dev of spec.devices) {
  const anchors = { whisper: dev.whisper, normal: dev.normal, shout: dev.shout, ambientDb: dev.ambient };

  // 校准：把三档各采 sampleFrames 帧（严格等于锚点值 + 轻微抖动，抖动用确定性公式）
  const calib = new V.VoiceCalibrator({ samplesPerPrompt: cal.samplesPerPrompt, sampleMs: cal.sampleFrames * spec.frameMs });
  for (let i = 0; i < cal.ambientFrames; i++) calib.pushAmbientFrame(dev.ambient + ((i % 5) - 2) * 0.5);
  for (const p of cal.prompts) {
    calib.startPrompt(p);
    const base = p === 'whisper' ? dev.whisper : p === 'normal' ? dev.normal : dev.shout;
    for (let i = 0; i < cal.sampleFrames; i++) calib.pushFrame(base + ((i % 7) - 3) * 0.4);
  }
  const calRes = calib.anchors ? JSON.parse(JSON.stringify(calib.anchors)) : null;

  const clf = new V.VoiceBandClassifier({ anchors });
  const device = { anchors, calibration: calRes, sequences: {} };
  for (const seq of spec.resolved.filter((r) => r.device === dev.id)) {
    const dec = [];
    for (const dbfs of seq.dBFS) {
      const r = clf.push(dbfs);
      dec.push({
        band: r.band === undefined ? null : r.band,
        reason: r.reason === undefined ? null : r.reason,
        norm: r.levelNorm === undefined || r.levelNorm === null ? null : Number(r.levelNorm.toFixed(6)),
        relPeakNorm: r.relPeakNorm === undefined || r.relPeakNorm === null ? null : Number(r.relPeakNorm.toFixed(6)),
        snrDb: r.snrDb === undefined ? null : Number(r.snrDb.toFixed(6)),
        // 注意：灰盒成功路径**不返回** snrPeakDb（只有不可辨路径有），故统一归一为 null
        snrPeakDb: r.snrPeakDb === undefined || r.snrPeakDb === null ? null : Number(r.snrPeakDb.toFixed(6)),
      });
    }
    device.sequences[seq.sequence] = dec;
  }
  out.devices[dev.id] = device;
}

if (updateRef || !fs.existsSync(REF)) {
  fs.writeFileSync(REF, JSON.stringify(out, null, 1) + '\n', 'utf8');
  console.log(`[voice-vectors] 参考输出已写入 ${path.relative(ROOT, REF)}`);
}
const reference = JSON.parse(fs.readFileSync(REF, 'utf8'));

// ── ③ 调 C# 跑手产出移植件输出 ──
let csOut;
try {
  // 注意：native/dotnet.sh 是 bash 脚本。不能用 process.execPath 起（会变成 node 解析 bash），
  // 也不能直接当可执行文件起 —— Windows 没有 shebang 支持，会静默失败且 stderr 为空。
  const res = execFileSync('bash', [path.join(ROOT, 'native/dotnet.sh'), 'run', '--project', path.join(ROOT, 'native/csharp-verify'), '--nologo', '-c', 'Release', '--', '--emit-voice-vectors'], { encoding: 'utf8', cwd: ROOT, stdio: 'pipe' });
  const start = res.indexOf('{');
  csOut = JSON.parse(res.slice(start));
} catch (e) {
  console.log('[voice-vectors] C# 跑手执行失败：');
  console.log(String(e.stdout ?? '').slice(-800));
  console.log(String(e.stderr ?? '').slice(-400));
  process.exit(1);
}

// ── ④ 逐帧比对 ──
const diffs = [];
let frames = 0;
const near = (a, b, tol = 1e-3) => (a == null && b == null) || (a != null && b != null && Math.abs(a - b) <= tol);
for (const dev of spec.devices) {
  const r = reference.devices[dev.id];
  const c = csOut.devices[dev.id];
  if (!c) { diffs.push(`设备 ${dev.id} 在 C# 输出中缺失`); continue; }
  if (!near(r.calibration?.normal, c.calibration?.normal, 1e-2)) {
    diffs.push(`设备 ${dev.id} 校准锚点 normal 不同：JS ${r.calibration?.normal} vs C# ${c.calibration?.normal}`);
  }
  for (const seqId of Object.keys(r.sequences)) {
    const rs = r.sequences[seqId], cs = c.sequences?.[seqId];
    if (!cs) { diffs.push(`${dev.id}/${seqId} 在 C# 输出中缺失`); continue; }
    if (rs.length !== cs.length) { diffs.push(`${dev.id}/${seqId} 帧数不同：JS ${rs.length} vs C# ${cs.length}`); continue; }
    for (let i = 0; i < rs.length; i++) {
      frames++;
      const a = rs[i], b = cs[i];
      if ((a.band ?? null) !== (b.band ?? null)) { diffs.push(`${dev.id}/${seqId} 帧 ${i} 档位不同：JS ${a.band} vs C# ${b.band}（norm ${a.norm} vs ${b.norm}）`); }
      else if ((a.reason ?? null) !== (b.reason ?? null)) { diffs.push(`${dev.id}/${seqId} 帧 ${i} reason 不同：JS ${a.reason} vs C# ${b.reason}`); }
      else if (!near(a.norm ?? null, b.norm ?? null) || !near(a.relPeakNorm ?? null, b.relPeakNorm ?? null) || !near(a.snrPeakDb ?? null, b.snrPeakDb ?? null, 5e-3)) {
        diffs.push(`${dev.id}/${seqId} 帧 ${i} 数值差异：norm ${a.norm}/${b.norm} · relPeak ${a.relPeakNorm}/${b.relPeakNorm} · snrPeak ${a.snrPeakDb}/${b.snrPeakDb}`);
      }
    }
  }
}
console.log(`[voice-vectors] 比对 ${spec.devices.length} 设备 × ${spec.resolved.length / spec.devices.length} 序列 · ${frames} 帧`);
console.log(`[voice-vectors] 差异 ${diffs.length} 处${diffs.length === 0 ? ' ✅ C# 移植与灰盒实现行为一致' : ' ❌'}`);
for (const d of diffs.slice(0, 12)) console.log('  ✗ ' + d);
process.exit(diffs.length === 0 ? 0 : 1);
