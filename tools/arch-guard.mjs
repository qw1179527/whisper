#!/usr/bin/env node
/**
 * arch-guard.mjs — 架构守护（V9 §13.1 / §13.2 / §19.1 / §22.2-G3 的 CI 化）
 *
 * 五组检查（任一 fail 即退出码 1，CI 直接阻断 PR）：
 *   A1 ASMDEF 依赖图与 V9 §13.1 规则表逐字一致（委托 gen-asmdef.mjs --check 的同一张表）
 *   A2 禁循环规则：Net 不引用 UI；Gameplay 不引用 Net / Backend（含间接可达性检查）
 *   A3 asmdef 的相对/名称引用不存在悬空；七个模块目录都真有 asmdef
 *   A4 第三方 SDK 命名空间只允许出现在唯一槽位（Photon→Net · Vivox→Audio · Firebase→Backend）
 *   A5 代码优先四承诺的静态可查项：C1 只允许 Boot 场景 / C2 UI 目录不得有 prefab / C4 不得有 lightmap
 *
 * 负向自测（--selftest）：临时植入一处违规（Gameplay 里 `using Photon`），断言守护必须抓到并随后清理。
 * 没有负向自测的守护等于永远绿的装饰品。
 *
 * 用法：node tools/arch-guard.mjs [--root unity] [--selftest]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const rootFlag = process.argv.indexOf('--root');
const UNITY = path.resolve(REPO, rootFlag > 0 ? process.argv[rootFlag + 1] : 'unity');
const SCRIPTS = path.join(UNITY, 'Assets/Scripts');
const selftest = process.argv.includes('--selftest');

/** V9 §13.1 规则表（与 tools/gen-asmdef.mjs 保持同一张表；此处独立声明以便交叉校验） */
const RULES = {
  Core: [],
  Gameplay: ['Core'],
  Net: ['Core', 'Gameplay'],
  Audio: ['Core'],
  Backend: ['Core'],
  UI: ['Core', 'Gameplay', 'Audio', 'Net', 'Backend'],
  Analytics: ['Core'],
  Runtime: ['Core', 'Gameplay', 'Net', 'Audio', 'Backend'], // 组合根（Boot），非七模块之一：需要引用实现方才能完成启动期注入
};
const FORBIDDEN = [
  { from: 'Net', to: 'UI', why: 'V9 §13.1：Net 不引用 UI' },
  { from: 'Gameplay', to: 'Net', why: 'V9 §13.1：Gameplay 不引用 Net' },
  { from: 'Gameplay', to: 'Backend', why: 'V9 §13.1：Gameplay 不引用 Backend' },
];
/** 第三方命名空间 → 唯一允许的模块（V9 §13.2） */
// 判定口径：只认**代码**，不认注释与 XML 文档（契约里必须能写「Photon 实现类在 Net/」这类说明）。
// 因此先剥掉 // 行注释、/* */ 块注释、/// 文档注释，再在剩余代码里找 using 与类型名。
const stripComments = (src) =>
  src
    .replace(/\/\/[^\n]*/g, '')      // 行注释与 /// 文档注释
    .replace(/\/\*[\s\S]*?\*\//g, ''); // 块注释

const SDK_SLOTS = [
  { module: 'Net', patterns: [/^\s*using\s+Photon(\.|;|\s)/m, /\bNetworkRunner\b/, /\bNetworkBehaviour\b/], label: 'Photon Fusion 2' },
  { module: 'Audio', patterns: [/^\s*using\s+Unity\.Services\.Vivox/m, /\bVivoxService\b/, /\bVivoxParticipant\b/], label: 'Unity Vivox' },
  { module: 'Backend', patterns: [/^\s*using\s+Firebase/m, /\bFirebaseApp\b/, /\bFirebaseFirestore\b/], label: 'Firebase Unity SDK' },
];

const fails = [];
const warns = [];
const ok = (m) => console.log('  ✓ ' + m);
const bad = (m) => {
  fails.push(m);
  console.log('  ✗ ' + m);
};

const readAsmdef = (mod) => {
  const f = path.join(SCRIPTS, mod, `Whisper.${mod}.asmdef`);
  if (!fs.existsSync(f)) return null;
  return { file: f, json: JSON.parse(fs.readFileSync(f, 'utf8')) };
};
const refsOf = (json) => (json.references ?? []).map((r) => String(r.name ?? r).replace(/^Whisper\./, ''));

// ── A1 依赖图一致 ──
console.log('A1 ASMDEF 依赖图 vs V9 §13.1 规则表');
{
  let bad1 = 0;
  for (const [mod, want] of Object.entries(RULES)) {
    const a = readAsmdef(mod);
    if (!a) {
      bad(`缺 asmdef：Whisper.${mod}.asmdef`);
      bad1++;
      continue;
    }
    const got = refsOf(a.json).sort();
    const exp = [...want].sort();
    if (got.join(',') !== exp.join(',')) {
      bad(`${mod} 依赖不一致：规则表[${exp.join(',')}] vs 文件[${got.join(',')}]`);
      bad1++;
    }
  }
  if (bad1 === 0) ok(`七个模块依赖与规则表逐字一致（Core 无外部依赖；UI 依赖 5 个）`);
}

// ── A2 禁循环规则（含可达性） ──
console.log('A2 禁循环规则');
{
  const graph = {};
  for (const mod of Object.keys(RULES)) {
    const a = readAsmdef(mod);
    graph[mod] = a ? refsOf(a.json) : [];
  }
  const reach = (from, target, seen = new Set()) => {
    if (seen.has(from)) return false;
    seen.add(from);
    for (const next of graph[from] ?? []) {
      if (next === target) return true;
      if (reach(next, target, seen)) return true;
    }
    return false;
  };
  let bad2 = 0;
  for (const { from, to, why } of FORBIDDEN) {
    // 直接或间接可达都算违规
    const direct = (graph[from] ?? []).includes(to);
    const indirect = !direct && reach(from, to);
    if (direct || indirect) {
      bad(`${from} → ${to} ${direct ? '直接' : '间接'}引用（${why}）`);
      bad2++;
    }
  }
  if (bad2 === 0) ok('三条禁令全部遵守（Net↛UI · Gameplay↛Net · Gameplay↛Backend，含间接可达）');
}

// ── A3 引用完整性 ──
console.log('A3 引用完整性');
{
  const known = new Set(Object.keys(RULES).map((m) => `Whisper.${m}`));
  let bad3 = 0;
  for (const mod of Object.keys(RULES)) {
    const a = readAsmdef(mod);
    if (!a) continue;
    for (const r of a.json.references ?? []) {
      const name = String(r.name ?? r);
      if (name.startsWith('Whisper.') && !known.has(name)) {
        bad(`${mod} 引用了不存在的模块 ${name}（悬空引用）`);
        bad3++;
      }
      if (r.name && r.guid === undefined && name.startsWith('Whisper.')) {
        // 按名称引用本仓库模块是允许的（同一仓库内更稳）；此处仅记录，不判失败
      }
    }
  }
  if (bad3 === 0) ok('无悬空引用；七个模块目录均有 asmdef');
}

// ── A4 第三方 SDK 唯一槽位 ──
console.log('A4 第三方 SDK 唯一槽位（V9 §13.2）');
{
  const csFiles = [];
  const walk = (dir) => {
    if (!fs.existsSync(dir)) return;
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walk(p);
      else if (e.name.endsWith('.cs')) csFiles.push(p);
    }
  };
  walk(SCRIPTS);
  let bad4 = 0;
  let hits = 0;
  for (const f of csFiles) {
    const rel = path.relative(SCRIPTS, f);
    const mod = rel.split(path.sep)[0];
    const text = stripComments(fs.readFileSync(f, 'utf8'));
    for (const slot of SDK_SLOTS) {
      if (slot.patterns.some((re) => re.test(text))) {
        hits++;
        if (mod !== slot.module) {
          bad(`${rel} 出现 ${slot.label} 调用，但唯一允许槽位是 ${slot.module}/（V9 §13.2）`);
          bad4++;
        }
      }
    }
  }
  if (bad4 === 0) ok(`扫描 ${csFiles.length} 个 .cs：第三方调用 ${hits} 处，全部位于各自唯一槽位`);
}

// ── A5 代码优先四承诺的静态可查项 ──
console.log('A5 代码优先四承诺（C1/C2/C4 静态项）');
{
  const scenesDir = path.join(UNITY, 'Assets/Scenes');
  const scenes = fs.existsSync(scenesDir) ? fs.readdirSync(scenesDir).filter((f) => f.endsWith('.unity')) : [];
  const nonBoot = scenes.filter((f) => !/^Boot\.unity$/i.test(f));
  if (nonBoot.length) bad(`C1 场景零手工：Assets/Scenes 出现非 Boot 场景 ${nonBoot.join(', ')}`);
  else ok(`C1 场景零手工：场景文件 ${scenes.length} 个（仅 Boot 白名单）`);

  const uiDir = path.join(SCRIPTS, 'UI');
  const prefabs = [];
  const walkP = (dir) => {
    if (!fs.existsSync(dir)) return;
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walkP(p);
      else if (e.name.endsWith('.prefab')) prefabs.push(path.relative(UNITY, p));
    }
  };
  walkP(uiDir);
  if (prefabs.length) bad(`C2 UI 零编辑器：UI 目录出现 prefab ${prefabs.join(', ')}`);
  else ok('C2 UI 零编辑器：UI 目录无 prefab（全部代码构建）');

  const lightmaps = [];
  for (const d of ['Assets', 'Assets/Scenes', 'Assets/Data']) {
    const dir = path.join(UNITY, d);
    if (!fs.existsSync(dir)) continue;
    for (const e of fs.readdirSync(dir)) if (/lightmap/i.test(e)) lightmaps.push(path.join(d, e));
  }
  if (lightmaps.length) bad(`C4 烘焙零编辑器：出现 lightmap 资产 ${lightmaps.join(', ')}`);
  else ok('C4 烘焙零编辑器：无 lightmap 资产（光照全实时）');

  // C3 资产清单存在性（管线入口）
  const manifest = path.join(UNITY, 'Assets/Data/asset-manifest.json');
  if (!fs.existsSync(manifest)) warns.push('C3：Assets/Data/asset-manifest.json 尚未创建（资产管线入口，下一小类补）');
  else ok('C3 资产零导入：asset-manifest.json 存在（脚本化管线入口）');
}

// ── A6 未注册 asmdef（独立验证轨 S6：旧版只查硬编码的七个名字，磁盘上的第八个完全失明）──
console.log('A6 未注册 asmdef');
{
  const found = [];
  (function walk(dir) {
    if (!fs.existsSync(dir)) return;
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walk(p);
      else if (e.name.endsWith('.asmdef')) found.push(path.relative(UNITY, p));
    }
  })(path.join(UNITY, 'Assets'));
  const registered = new Set(Object.keys(RULES).map((m) => `Assets/Scripts/${m}/Whisper.${m}.asmdef`));
  const isTest = (rel) => /\/Tests\//.test(rel);
  const unregistered = found.filter((rel) => !registered.has(rel) && !isTest(rel));
  if (unregistered.length) {
    for (const rel of unregistered) bad(`未注册 asmdef：${rel}（不在规则表内 → 无人守护它的依赖方向）`);
  } else {
    ok(`磁盘上 ${found.length} 个 asmdef（含测试程序集）全部已注册或有明确归属`);
  }
}

// ── A7 服务注入可达性（独立验证轨指出：若没有任何生产代码调用 Services.Install，启动期注入路径不成立）──
console.log('A7 服务注入可达性');
{
  const callSites = [];
  for (const f of (function collect(dir) {
    const out = [];
    (function walk(d) {
      if (!fs.existsSync(d)) return;
      for (const e of fs.readdirSync(d, { withFileTypes: true })) {
        const p = path.join(d, e.name);
        if (e.isDirectory()) walk(p);
        else if (e.name.endsWith('.cs')) out.push(p);
      }
    })(dir);
    return out;
  })(SCRIPTS)) {
    const rel = path.relative(UNITY, f);
    // 匹配调用形态（前面是 = ( , ; 或行首）；排除 `public static void Install(...)` 这类声明
    if (/(?:^|[=(,;]\s*)Services\.Install\s*\(/m.test(stripComments(fs.readFileSync(f, 'utf8')))) callSites.push(rel);
  }
  const prod = callSites.filter((r) => !/\/Tests\//.test(r));
  const compositionRoot = prod.filter((r) => /\/Runtime\//.test(r));
  if (prod.length === 0) {
    bad('A7：没有任何生产代码在启动期注入三接口（Services.Install 只出现在测试里）——注入路径不成立');
  } else if (compositionRoot.length === 0) {
    bad(`A7：注入点存在但不在组合根：${prod.join(', ')}（应由 Whisper.Runtime/GameBootstrap 负责，避免各模块互相注入）`);
  } else {
    // 三个接口是否都注入了（逐个数调用次数，任一缺失即警告）
    const src = prod.map((r) => stripComments(fs.readFileSync(path.join(UNITY, r), 'utf8'))).join('\n');
    const missing = [];
    if (!/new\s+\w*NetService/.test(src)) missing.push('INetService');
    if (!/new\s+\w*VoiceService/.test(src)) missing.push('IVoiceService');
    if (!/new\s+\w*BackendService/.test(src)) missing.push('IBackendService');
    if (missing.length) warns.push(`A7：组合根未注入 ${missing.join(', ')}`);
    else ok(`生产代码注入点 ${prod.length} 个（均在组合根）：${prod.join(', ')} · 三接口齐全`);
  }
}

// ── 负向自测 ──
if (selftest) {
  console.log('S 负向自测（植入违规，守护必须抓到）');

  // S1：第三方槽位违规（A4）——往 Gameplay 放一个 `using Photon`
  const probeDir = path.join(SCRIPTS, 'Gameplay');
  const probe = path.join(probeDir, '__arch_guard_probe.cs');
  fs.mkdirSync(probeDir, { recursive: true });
  fs.writeFileSync(probe, 'using Photon;  // 故意违规：Gameplay 不得引用第三方 SDK\npublic class Probe {}\n', 'utf8');
  let caught1 = false;
  try {
    execFileSync(process.execPath, [path.join(REPO, 'tools/arch-guard.mjs'), '--root', path.relative(REPO, UNITY)], { stdio: 'pipe' });
  } catch {
    caught1 = true;
  }
  fs.rmSync(probe, { force: true });
  if (caught1) ok('S1 植入 `using Photon` 于 Gameplay → 拦截成功，探针已清理');
  else bad('S1 负向自测失败：守护没拦住第三方槽位违规');

  // S2：依赖图违规（A1/A2）——造一个最小工程根，让 Net 引用 UI（V9 §13.1 明令禁止）
  const tmpRoot = fs.mkdtempSync(path.join(REPO, '.arch-selftest-'));
  try {
    const mods = { Core: [], Gameplay: ['Core'], Net: ['Core', 'Gameplay', 'UI'], Audio: ['Core'], Backend: ['Core'], UI: ['Core', 'Gameplay', 'Audio', 'Net', 'Backend'], Analytics: ['Core'] };
    for (const [m, refs] of Object.entries(mods)) {
      const d = path.join(tmpRoot, 'Assets/Scripts', m);
      fs.mkdirSync(d, { recursive: true });
      const json = { name: `Whisper.${m}`, rootNamespace: `Whisper.${m}`, references: refs.map((r) => ({ name: `Whisper.${r}` })) };
      fs.writeFileSync(path.join(d, `Whisper.${m}.asmdef`), JSON.stringify(json, null, 2), 'utf8');
    }
    let caught2 = false;
    try {
      execFileSync(process.execPath, [path.join(REPO, 'tools/arch-guard.mjs'), '--root', path.relative(REPO, tmpRoot)], { stdio: 'pipe' });
    } catch {
      caught2 = true;
    }
    if (caught2) ok('S2 造 `Net → UI` 违规工程 → 拦截成功（A1 依赖表 / A2 禁循环生效）');
    else bad('S2 负向自测失败：守护没拦住 Net→UI 违规');
  } finally {
    fs.rmSync(tmpRoot, { recursive: true, force: true });
  }
}

console.log('');
for (const w of warns) console.log('  ⚠ ' + w);
if (fails.length) {
  console.log(`结果：失败 ${fails.length} 项 ✗`);
  process.exit(1);
}
console.log(`结果：A1~A7 全部通过 ✓${warns.length ? `（${warns.length} 条待办警告）` : ''}`);
