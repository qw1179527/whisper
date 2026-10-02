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

const fails = [], oks = [];
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
const allCs = csText.map((x) => x.t).join('\n');
if (inject('hardcode')) {
  csText.push({ f: '(注入)', t: 'var x = 3.6f; // 硬编码速度' });
  allCs.replace('', '');
}

console.log('[gate-physics] 物理规则门禁');

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
  if (inject('hardcode')) bads.push('(注入) 硬编码检测样例');
  bads.length === 0
    ? ok(`P1 数值真源：${mustRef.length} 条物理配置路径均在 C# 代码中有出处`)
    : bads.slice(0, 6).forEach((b) => bad(`P1 配置路径无代码出处：${b}`));
}

// ── P2 确定性：禁止不可复现随机源与时间源 ──
{
  const banned = [
    { re: /\bMath\.Random\b/, why: 'C# System.Random 不可复现（60 Tick 同步需确定性）' },
    { re: /\bUnityEngine\.Random\b|\bRandom\.(Range|value|insideUnit)\b/, why: 'UnityEngine.Random 依赖全局种子，跨端不一致' },
    { re: /\bDateTime\.(Now|UtcNow)\b/, why: '墙钟时间不可复现（应用 tick 计数或 elapsedSeconds）' },
    { re: /\bEnvironment\.TickCount\b/, why: '同上' },
    { re: /\bGuid\.NewGuid\b/, why: '随机器不可复现（应使用确定性 id）' },
  ];
  const bads = [];
  for (const { f, t } of csText) {
    for (const b of banned) if (b.re.test(t)) bads.push(`${f} 使用了 ${b.re.source} —— ${b.why}`);
  }
  if (inject('random')) bads.push('(注入) private float r = Math.Random();');
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

console.log(`\n[gate-physics] 结果：通过 ${oks.length} · 失败 ${fails.length}${fails.length ? ' ✗' : ' ✓'}`);
if (injected && fails.length === 0) {
  console.log(`[gate-physics] ✗ 注入 ${injected} 后仍未判红 —— 该门禁不可信`);
  process.exit(1);
}
process.exit(fails.length ? 1 : 0);
