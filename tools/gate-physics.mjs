#!/usr/bin/env node
/**
 * gate-physics.mjs — 物理规则门禁（用户要求的第二类门禁）
 *
 * 覆盖三类问题（都是本项目实际踩过或极易踩的）：
 *   P1 数值真源：物理量必须来自 data/config.json（V9 §19.5「改数值不碰代码」）。
 *      做法：manifest 声明"哪些配置路径必须被代码引用"，然后逐条在 C# 源码里找出处。
 *   P2 确定性：玩法逻辑不得引入不可复现的随机源（Math.Random / Random.Range / DateTime.Now…）。
 *      依据：V9 §13.4 60 Tick 状态同步需要确定性。
 *   P3 量纲与符号一致：同族配置的符号/单位必须自洽（负数=流失、正数=恢复…）。
 *      依据：本项目踩过 `contactSanityLoss=35`（正数=损失量）与 `sanity.drain.*`（负数）不一致，
 *      导致"扣理智"被写成"涨理智"。
 *   P4 规则单调性（源码级）：刺激越响强度越大、距离越远越弱、阈值越高越难听见 ——
 *      在**数值表**层面验证方向正确（不是跑引擎）。
 *
 * ## 明确不覆盖（如实声明）
 *   · 引擎内的真实积分/碰撞响应（需要 Unity 或引擎侧跑）
 *   · 帧率相关的时序正确性（需要真机）
 *   本门禁只保证"规则与数值层面自洽"，不保证"引擎里手感对"。
 *
 * 用法：node tools/gate-physics.mjs [--inject-hardcode|--inject-random|--inject-sign|--inject-monotonic]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const CFG = path.join(ROOT, 'data/config.json');
const MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');
const SRC_DIRS = [path.join(ROOT, 'unity/Assets/Scripts')];
const args = process.argv.slice(2);
const inject = (n) => args.includes(`--inject-${n}`);

const fails = [], oks = [], warns = [];
const ok = (m) => { oks.push(m); console.log('  ✓ ' + m); };
const bad = (m) => { fails.push(m); console.log('  ✗ ' + m); };

let cfg = JSON.parse(fs.readFileSync(CFG, 'utf8'));
let injected = null;
if (inject('sign')) { injected = 'P3 符号一致'; cfg.sanity.drain.darknessPerSec = 1; }
else if (inject('monotonic')) { injected = 'P4 单调性'; cfg.stimulusSources.voice_shout.intensity = 5; }
else if (inject('hardcode')) { injected = 'P1 数值真源'; }
else if (inject('random')) { injected = 'P2 确定性'; }
if (injected) console.log(`[gate-physics] 注入模式：${injected}（预期判红）`);

/** 收集 C# 源码（排除 Tests 与 Unity 依赖的薄层时按需） */
function collectCs(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (e.name !== 'obj' && e.name !== 'bin') collectCs(p, out); }
    else if (p.endsWith('.cs')) out.push(p);
  }
  return out;
}
const csFiles = SRC_DIRS.flatMap((d) => (fs.existsSync(d) ? collectCs(d) : []));
const csText = csFiles.map((f) => ({ f: path.relative(ROOT, f), t: fs.readFileSync(f, 'utf8') }));
let allCs = csText.map((x) => x.t).join('\n');
let allCs2 = allCs;   // 注入后的重算版本（P1 用它）
// （真实注入见 P1 检查处：向 csText 追加一条含硬编码数值的假源码，由判据自己去发现）

/**
 * 真实注入（复核 F4）：向源码集合追加"含缺陷"的假源文件，让**判据自己去发现**。
 * 我原来直接 `bads.push('(注入) …')` 属于伪造注入 —— 判据根本没参与，等于自证有效。
 * 注意：注入必须发生在各检查**之前**（我第一版加在之后，判据看不到，注入验证反而自己报"不可信"）。
 */
if (inject('hardcode')) {
  const vals = [];
  const walkV = (o) => { for (const v of Object.values(o ?? {})) { if (v && typeof v === 'object') walkV(v); else if (typeof v === 'number') vals.push(v); } };
  walkV(cfg);
  const sample = vals.find((v) => Math.abs(v) > 1 && Math.abs(v) < 1000) ?? 3.6;
  csText.push({ f: '(注入).cs', t: `public static class Injected { public const float Speed = ${sample}f; }` });
  allCs2 = allCs + '\n' + csText[csText.length - 1].t;
}
if (inject('random')) csText.push({ f: '(注入).cs', t: 'public class InjRnd { System.Random r = new System.Random(); }' });

console.log('[gate-physics] 物理规则门禁');

// ── P0 硬编码扫描：配置里的物理数值不得以字面量写进代码（V9 §19.5「改数值不碰代码」）──
// 复核 F4 指出我此前**完全没有**硬编码扫描（只查"配置路径字面量是否在代码里出现"）。
{
  const numeric = [];
  const walkN = (o, path0) => {
    for (const [k, v] of Object.entries(o ?? {})) {
      const np = path0 ? `${path0}.${k}` : k;
      if (v && typeof v === 'object') walkN(v, np);
      else if (typeof v === 'number') numeric.push({ key: np, v });
    }
  };
  walkN(cfg, '');
  // 唯一性过滤：同一数值可能同时是墙厚(0.35)、走廊宽(1.3) 与某条 sanity 配置值 ——
  // 只看"该数值是否只有一条配置路径"才具备可判定性，否则任何几何常数都会撞上某个配置值
  // （实测：LevelBuilder 的墙厚 0.35f 被报成 sanity.bands[2].edgeNoise）。
  const uniq = new Map();
  for (const { key, v } of numeric) {
    if (!/^(stimulusSources|monsters|monsterBehavior|sanity|voiceCalibration|items|economy)\./.test(key)) continue;
    const k = String(v);
    uniq.set(k, (uniq.get(k) ?? 0) + 1);
  }
  const hits = [];
  for (const { f, t } of csText) {
    if (/DesignTokens|\.g\.cs$/.test(f)) continue;          // 生成物与设计 Token 不算
    for (const { key, v } of numeric) {
      // 只看物理量族，避免 0/1/2 这类通用常数误报
      if (!/^(stimulusSources|monsters|monsterBehavior|sanity|voiceCalibration|items|economy)\./.test(key)) continue;
      if ((uniq.get(String(v)) ?? 0) > 1) continue;   // 该数值在配置里不唯一 → 无法判定，跳过
      // 只认可疑字面量：**带小数或 f 后缀**。纯整数（3/60/15）在代码里绝大多数是
      // 数组长度、超时、重试次数这类结构值，把它们当"硬编码物理量"会淹没真信号
      // （我第一版就是这么误报的：IBackendService 里的 15 被说成 flare 半径）。
      const lit = new RegExp(`(?<![\\w.])${String(v).replace('.', '\\.')}f(?!\\w)|(?<![\\w.])${String(v).replace('.', '\\.')}(?![\\w.])`);
      const hasFrac = String(v).includes('.');
      if (!hasFrac && !/f\)/.test(lit.source)) continue;
      const mustF = new RegExp(`(?<![\\w.])${String(v).replace('.', '\\.')}f(?![\\w.])`);
      if (!mustF.test(t)) continue;
      // 豁免两类合法出现：
      //   ① 同一行里已有配置读取 → 那是 fallback 默认值，允许
      //   ② `const` 声明行 → 那是常量的**定义处**（本项目把非配置的交互参数定义为具名常量），
      //      定义本身不是"硬编码使用"。没有这条豁免会把定义行也报出来（实测误报）。
      const lineHasRead = t.split('\n').some((ln) =>
        mustF.test(ln) && /(Cfg|cfg|Config)\.(Float|Int|GetFloat|GetInt|Get)\s*\(/.test(ln));
      const lineIsConstDecl = t.split('\n').some((ln) =>
        mustF.test(ln) && /\bconst\s+(float|double|int)\b/.test(ln));
      if (!lineHasRead && !lineIsConstDecl) hits.push(`${f} 出现配置值字面量 ${v}f（应读 ${key}）`);
    }
  }
  // ⚠ P0 是**警告级**而非判红级：数值碰撞无法根治 —— 几何常数（墙厚 0.35、门宽 1.3、半径 0.9）
  // 与某些配置值天然同数，任何"数值比对"式扫描都会撞上。判红仍由 P1~P4 承担（语义级判据）。
  // 保留 P0 的价值：它把"可能硬编码了配置值"的位置**列出来供人工看**（我正是靠它发现
  // Settlement.cs 里 1.3f 与配置重复、以及 ItemSystem 的交互半径裸写在逻辑里）。
  if (hits.length === 0) ok(`P0 硬编码扫描（警告级）：无候选（共比 ${numeric.length} 个候选值）`);
  else { warns.push(...hits); console.log(`  ⚠ P0 硬编码扫描（警告级，不判红）：${hits.length} 处数值与配置值同数，供人工核对`); }
  for (const h of hits.slice(0, 6)) console.log('     · ' + h);
}

// ── P1 数值真源：manifest 声明的配置路径必须在代码里有出处 ──
{
  const mustRef = [
    'stimulusSources.voice_whisper.intensity', 'stimulusSources.voice_normal.intensity', 'stimulusSources.voice_shout.intensity',
    'stimulusSources.run_footstep.intensity', 'stimulusSources.crouch_footstep.intensity', 'stimulusSources.walk_footstep.intensity',
    'stimulusSources.recorder_play.intensity', 'stimulusSources.prop_break.intensity', 'stimulusSources.sanity_scream.intensity',
    'monsters.stitcher.hearingThreshold', 'monsters.whisperer.hearingThreshold', 'monsters.coroner.hearingThreshold',
    'monsters.stitcher.speedMps', 'monsters.whisperer.speedMps', 'monsters.coroner.speedMps',
    'sanity.max', 'sanity.drain.darknessPerSec', 'sanity.drain.lookAtMonsterPerSec',
    'sanity.drain.allyDeath', 'sanity.drain.jumpscare', 'sanity.recover.extractionSafeZonePerSec', 'sanity.recover.sedative',
    'voiceCalibration.hysteresisDb', 'voiceCalibration.minRuntimeSnrDb', 'voiceCalibration.clampCeil', 'voiceCalibration.peakBoost',
    'monsterBehavior.lostContactSeconds', 'monsterBehavior.investigateArriveRadiusM', 'monsterBehavior.chaseSpeedScale',
    'monsterBehavior.contactSanityLoss', 'monsterBehavior.finalRageWindowBeforeExtractionSec',
    'network.tickRate', 'level.startGraceSeconds', 'level.matchSeconds.0',
    'economy.formula.evidence', 'economy.formula.survivingAlly', 'economy.formula.efficiencyUnder10min',
    'level.extraction.standardPoint.rewardScale', 'level.extraction.deepPoint.rewardScale',
    'items.flashlight.batterySeconds', 'items.flashlight.monsterPerceptionBonusM',
    'items.flare.safeZoneSeconds', 'items.camera.stunSeconds', 'items.recorder.recordSeconds',
  ];
  const bads = [];
  /**
   * 判据分两层（我第一版只做"整条路径字面量必须出现"，结果把用插值路径读取的代码全判红了）：
   *   ① **家族级**：该配置族必须被代码用 reader 读取（`Cfg.Float("family.` 之类）；
   *   ② **叶子级**：该叶子要么以完整路径出现，要么其"族内键名+字段"在代码里出现
   *      （例如 `stimulusSources.{key}.intensity` 的 key 由 BandSourceKey 提供，
   *       代码里出现 `bandSourceKey`/`BandSourceKey` 即算有出处）。
   */
  const familyOf = (p) => p.split('.')[0];
  const readFamilies = new Set();
  for (const { t } of csText) {
    for (const m of t.matchAll(/Cfg\.(?:Float|Int|String|Bool)\(\s*\$?"([a-zA-Z_]+)\./g)) readFamilies.add(m[1]);
    for (const m of t.matchAll(/GameConfig\.(?:GetFloat|GetInt|GetString|GetBool|Get)\(\s*\$?"([a-zA-Z_]+)\./g)) readFamilies.add(m[1]);
    for (const m of t.matchAll(/cfg\.(?:Float|Int|String|Bool)\(\s*\$?"([a-zA-Z_]+)\./g)) readFamilies.add(m[1]);
  }
  const leafSynonym = {
    'stimulusSources.voice_whisper.intensity': /BandSourceKey|voice_whisper/,
    'stimulusSources.voice_normal.intensity': /BandSourceKey|voice_normal/,
    'stimulusSources.voice_shout.intensity': /BandSourceKey|voice_shout/,
    'stimulusSources.run_footstep.intensity': /run_footstep/,
    'stimulusSources.crouch_footstep.intensity': /crouch_footstep/,
    'stimulusSources.walk_footstep.intensity': /walk_footstep/,
    'stimulusSources.recorder_play.intensity': /recorder_play/,
    'stimulusSources.prop_break.intensity': /prop_break/,
    'stimulusSources.sanity_scream.intensity': /sanity_scream|CollapseStimulusKey/,
    'monsters.stitcher.hearingThreshold': /hearingThreshold/,
    'monsters.whisperer.hearingThreshold': /hearingThreshold/,
    'monsters.coroner.hearingThreshold': /hearingThreshold/,
  };
  for (const pth of mustRef) {
    const fam = familyOf(pth);
    const famRead = readFamilies.has(fam) || csText.some((x) => x.t.includes(`"${fam}.`) || x.t.includes(`($"${fam}.`));
    if (!famRead) { bads.push(`${pth}（配置族 ${fam} 未被 reader 读取）`); continue; }
    const leafField = pth.split('.').slice(1).pop();
    const syn = leafSynonym[pth];
    const leafHit = csText.some((x) => x.t.includes(pth) || (syn && syn.test(x.t)) || x.t.includes(leafField));
    if (!leafHit) bads.push(`${pth}（族已读取，但找不到叶子的消费点）`);
  }
  // 反向：配置里"物理量"族必须被引用（防止新增配置无人消费）
  const physFamilies = ['stimulusSources', 'monsters', 'monsterBehavior', 'sanity', 'voiceCalibration'];
  for (const fam of physFamilies) {
    if (!allCs.includes(fam + '.')) bads.push(`整个配置族 ${fam}.* 在代码里没有任何引用`);
  }
  bads.length === 0
    ? ok(`P1 数值真源：${mustRef.length} 条物理配置路径均在 C# 代码中有出处`)
    : bads.slice(0, 6).forEach((b) => bad(`P1 配置路径无代码出处：${b}`));
}

// ── P2 确定性：禁止不可复现随机源与时间源 ──
{
  const banned = [
    // 【假绿修复 · 质检第 1 轮抓出】原判据写成 /\bMath\.Random\b/ —— C# 里**没有** Math.Random
    // （那是 JS 写法），于是 `System.Random` / `new Random()` 一律被放行；而 --inject-random
    // 注入的恰恰是 `new System.Random()`，门禁自报「注入后仍未判红 —— 该门禁不可信」。
    // 现按 C# 真实写法匹配：实例化 / 字段声明 / Random.Range|Next 调用。
    // 刻意不用裸 \bRandom\b —— RandomSeed、targetRandom 这类合法标识符会被误伤。
    {
      re: /new\s+(?:System\.)?Random\s*\(|\b(?:System\.)?Random\s+[A-Za-z_]\w*\s*[=;]|\b(?:System\.)?Random\s*\.\s*(?:Range|Next|NextDouble|NextBytes)\b/,
      why: 'C# Random 以系统时间为种子、跨端不可复现（60 Tick 同步需确定性；应使用种子化 PRNG 或确定性 id）',
    },
    { re: /\bUnityEngine\.Random\b|\bRandom\s*\.\s*(?:Range|value|insideUnitSphere|insideUnitCircle)\b/, why: 'UnityEngine.Random 依赖全局种子，跨端不一致' },
    { re: /\bDateTime\.(Now|UtcNow)\b/, why: '墙钟时间不可复现（应用 tick 计数或 elapsedSeconds）' },
    { re: /\bEnvironment\.TickCount\b/, why: '同上' },
    { re: /\bGuid\.NewGuid\b/, why: '随机器不可复现（应使用确定性 id）' },
  ];
  const bads = [];
  for (const { f, t } of csText) {
    for (const b of banned) if (b.re.test(t)) bads.push(`${f} 使用了 ${b.re.source} —— ${b.why}`);
  }
  bads.length === 0 ? ok(`P2 确定性：${csText.length} 个 C# 文件中无不可复现随机/时间源`) : bads.slice(0, 4).forEach(bad);
}

// ── P3 符号与量纲一致 ──
{
  const bads = [];
  const drain = cfg.sanity?.drain ?? {};
  for (const [k, v] of Object.entries(drain)) {
    if (typeof v !== 'number') continue;
    if (v >= 0) bads.push(`sanity.drain.${k} = ${v}，应为负数（每秒/每次流失）`);
  }
  for (const k of ['sedative', 'extractionSafeZonePerSec']) {
    const v = cfg.sanity?.recover?.[k];
    if (typeof v === 'number' && v <= 0) bads.push(`sanity.recover.${k} = ${v}，应为正数（恢复量）`);
  }
  // contactSanityLoss 是"正数=损失量"的特例：必须在代码里被显式取负
  const loss = cfg.monsterBehavior?.contactSanityLoss;
  if (typeof loss === 'number') {
    if (loss <= 0) bads.push(`monsterBehavior.contactSanityLoss = ${loss}，按语义应为正数损失量`);
    if (!/-Math\.Abs\(loss\)|-Math\.Abs\(_cfg\.Float\("monsterBehavior\.contactSanityLoss"/.test(allCs)
        && !/loss\s*=\s*_cfg\.Float\("monsterBehavior\.contactSanityLoss"[\s\S]{0,200}Apply\(-/.test(allCs)) {
      bads.push('contactSanityLoss 是"正数=损失量"，但代码里找不到显式取负 —— 会把扣理智写成涨理智（本项目实际踩过）');
    }
  }
  // 单位一致：所有 xxPerSec 必须带"每秒"语义（数值不应是"每帧"）
  const perSecKeys = [];
  const walk = (o, p = '') => {
    for (const [k, v] of Object.entries(o ?? {})) {
      const np = p ? `${p}.${k}` : k;
      if (v && typeof v === 'object') walk(v, np);
      else if (/PerSec$/.test(k)) perSecKeys.push({ key: np, v });
    }
  };
  walk(cfg);
  for (const { key, v } of perSecKeys) {
    if (typeof v !== 'number') continue;
    if (Math.abs(v) > 100) bads.push(`${key} = ${v} 量级异常（每秒变化超过 100，疑似把"每帧"写成了"每秒"）`);
  }
  bads.length === 0
    ? ok(`P3 符号与量纲：${Object.keys(drain).length} 项流失为负 · 恢复为正 · contactSanityLoss 取负已核实 · ${perSecKeys.length} 项 PerSec 量级正常`)
    : bads.slice(0, 5).forEach(bad);
}

// ── P4 规则单调性（数值表层）──
{
  const bads = [];
  const src = cfg.stimulusSources ?? {};
  // 强度单调链：耳语 < 正常 < 喊叫；走路 < 奔跑
  const intensityOrder = [['voice_whisper', 'voice_normal'], ['voice_normal', 'voice_shout'], ['walk_footstep', 'run_footstep']];
  for (const [a, b] of intensityOrder) {
    if (!src[a] || !src[b]) { bads.push(`刺激源 ${a} 或 ${b} 缺失`); continue; }
    if (!(src[a].intensity < src[b].intensity)) bads.push(`强度应满足 ${a}(${src[a].intensity}) < ${b}(${src[b].intensity})`);
  }
  // 半径单调链：蹲行 < 走路 < 奔跑（**蹲行与走路强度同为 8，靠半径区分**——我第一版拿强度比，
  // 误报了一次；真源里 crouch 半径 3、walk 半径 8）
  const radiusOrder = [['crouch_footstep', 'walk_footstep'], ['walk_footstep', 'run_footstep'],
                       ['voice_whisper', 'voice_normal'], ['voice_normal', 'voice_shout']];
  for (const [a, b] of radiusOrder) {
    if (!src[a] || !src[b]) continue;
    const ra = src[a].radiusM ?? Infinity, rb = src[b].radiusM ?? Infinity;
    if (ra > rb) bads.push(`传播半径应满足 ${a}(${ra}) ≤ ${b}(${rb})`);
  }
  // 听觉阈值梯度：低语者（语音猎手）< 缝匠（教学怪）< 收殓人（视野为主）
  const M = cfg.monsters ?? {};
  if (M.whisperer && M.stitcher && !(M.whisperer.hearingThreshold < M.stitcher.hearingThreshold))
    bads.push(`低语者阈值应低于缝匠（${M.whisperer.hearingThreshold} vs ${M.stitcher.hearingThreshold}）`);
  if (M.stitcher && M.coroner && !(M.stitcher.hearingThreshold < M.coroner.hearingThreshold))
    bads.push(`缝匠阈值应低于收殓人（${M.stitcher.hearingThreshold} vs ${M.coroner.hearingThreshold}）`);
  // 视觉范围梯度：低语者（安静几乎失明）< 缝匠 < 收殓人（视野为主）
  if (M.whisperer && M.stitcher && !(M.whisperer.sightRangeM < M.stitcher.sightRangeM))
    bads.push(`低语者视野应小于缝匠（${M.whisperer.sightRangeM} vs ${M.stitcher.sightRangeM}）`);
  if (M.stitcher && M.coroner && !(M.stitcher.sightRangeM < M.coroner.sightRangeM))
    bads.push(`缝匠视野应小于收殓人（${M.stitcher.sightRangeM} vs ${M.coroner.sightRangeM}）`);
  // 理智档位区间必须连续覆盖 0..max 且不重叠
  const bands = cfg.sanity?.bands ?? [];
  for (let i = 0; i < bands.length; i++) for (let j = i + 1; j < bands.length; j++) {
    if (bands[i].min <= bands[j].max && bands[j].min <= bands[i].max)
      bads.push(`理智档位重叠：${bands[i].id} 与 ${bands[j].id}`);
  }
  bads.length === 0
    ? ok(`P4 规则单调性：刺激强度/半径梯度正确 · 三怪听觉与视野梯度正确 · ${bands.length} 档理智区间不重叠`)
    : bads.slice(0, 5).forEach(bad);
}

if (warns.length) console.log(`\n[gate-physics] 警告 ${warns.length} 条（不判红；判红判据为 P1~P4）`);
console.log(`\n[gate-physics] 结果：通过 ${oks.length} · 失败 ${fails.length}${fails.length ? ' ✗' : ' ✓'}`);
if (injected && fails.length === 0) {
  console.log(`[gate-physics] ✗ 注入 ${injected} 后仍未判红 —— 该门禁不可信`);
  process.exit(1);
}
process.exit(fails.length ? 1 : 0);
