#!/usr/bin/env node
/**
 * sanity-official-tick.mjs — 新增按**官方口径**的理智 Tick
 *
 * ## 冲突（现有实现与官方不符）
 * 现有 `SanitySystem.Tick(dt, torchOn, inSafeZone, lightsOut)` 的逻辑：
 * ```csharp
 * if (!torchOn) Apply(_darknessPerSec * dt, SanitySource.Darkness);   // 有手电 → 不掉
 * ```
 * 而官方（phasmophobia.su/gameplay/sanity）明确：
 * > 流失取决于房间**主光源**（天花板灯 + 墙上开关）；
 * > **台灯 / 落地灯 / 电视 / 监视器 / 手电 / 设置里的亮度 都不能停止流失**。
 * ⇒ **"有手电就不掉理智"是错的**；正确判据是"该房间主灯是否开启"。
 *
 * ## 做法：**新增重载，不动旧重载**
 * 旧 `Tick(...)` 保留（既有调用方与测试不受影响，行为不变）；
 * 新增 `Tick(dt, in SanityTickContext ctx)` 按官方公式计算：
 * ```
 * 基础值 = passivePerSec[地图大小][阶段]
 * 乘数   = difficultyMultiplier[难度] + (bloodMoon ? bloodMoonAdditive : 0)
 *        × (solo ? soloPassiveMultiplier : 1)
 * 修正   = ctx.MainLightOn ? 0                    // 主灯全开 → 该房间为 0
 *        : ctx.LargeDarkZone ? 基础值×0.2          // 大暗区开主灯也只降到 80%
 *        : 火源? 基础值×(1 − fireTierFactor) : 基础值
 * 最后   = 若处于 Setup 阶段，任何来源不得把理智压到 setupFloor 以下
 * ```
 * ⚠ **手电（TorchOn）在新口径里完全不参与流失计算** —— 这一点写进注释，防止后人"顺手加回去"。
 *
 * ## 为什么用 struct 传参而不是再加 8 个布尔
 * 8 个同类型布尔参数极易调用错序（本项目已有"参数顺序错"类事故）。struct + 具名初始化可读且不易错。
 *
 * 用法：node tools/sanity-official-tick.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Gameplay/Sanity/SanitySystem.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[sanity-tick] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('SanityTickContext')) { console.log('[sanity-tick] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 新增上下文结构（放在 enum SanitySource 之后）────────────────────
sub('    /// <summary>理智变化来源（用于统计与死亡归因）。</summary>\n    public enum SanitySource',
  [
    '    /// <summary>地图大小档（官方被动流失按此分档）。</summary>',
    '    public enum MapSizeBand { Small, Medium, Large }',
    '',
    '    /// <summary>对局阶段（官方 Setup 阶段有 50% 保底）。</summary>',
    '    public enum MatchPhaseBand { Setup, Normal }',
    '',
    '    /// <summary>',
    '    /// 官方口径的理智 Tick 上下文（出处：phasmophobia.su/knowledge-base/gameplay/sanity）。',
    '    /// 用 struct + 具名初始化传参：8 个同类型布尔极易错序（本项目已有"参数顺序错"类事故）。',
    '    /// </summary>',
    '    public struct SanityTickContext',
    '    {',
    '        /// <summary>地图大小档。</summary>',
    '        public MapSizeBand MapSize;',
    '        /// <summary>对局阶段（Setup 有 50% 保底）。</summary>',
    '        public MatchPhaseBand Phase;',
    '        /// <summary>难度乘数（来自 config `sanity.drain.official.difficultyMultiplier`）。</summary>',
    '        public float DifficultyMultiplier;',
    '        /// <summary>是否单人（被动流失减半）。</summary>',
    '        public bool Solo;',
    '        /// <summary>是否血月（在难度乘数之上再加一档）。</summary>',
    '        public bool BloodMoon;',
    '        /// <summary>',
    '        /// 所在房间的**主光源**是否开启（天花板灯 + 墙上开关）。',
    '        /// ⚠ **只有它为 true 才能把该房间的被动流失降到 0**；',
    '        /// 台灯/落地灯/电视/监视器/手电/设置亮度**都不算**（官方原文）。',
    '        /// </summary>',
    '        public bool MainLightOn;',
    '        /// <summary>是否处于"大暗区"（如 Sunny Meadows 走廊）：开主灯也只降到 80%。</summary>',
    '        public bool LargeDarkZone;',
    '        /// <summary>附近火源等级（0 = 无；1/2/3 按 config 的 fireTierFactor 降流失）。</summary>',
    '        public int FireTier;',
    '    }',
    '',
    '    /// <summary>理智变化来源（用于统计与死亡归因）。</summary>',
    '    public enum SanitySource',
  ].join('\n'),
  '① 新增 SanityTickContext（含 MapSizeBand/MatchPhaseBand）');

// ── ② 新增官方口径 Tick 重载 ─────────────────────────────────────────
sub('        /// <summary>注视怪物（按住看它会持续掉理智）。</summary>',
  [
    '        /// <summary>',
    '        /// 0..1 截断。',
    '        /// 不能用 Mathf.Clamp01：本文件属 Whisper.Gameplay，而该程序集**不引用 UnityEngine**',
    '        /// （第 17 轮实测：在此用 Vector3 会 CS0246）。玩法层零 Unity 依赖是本项目既定分层，',
    '        /// 故自带一个纯 System 实现。',
    '        /// </summary>',
    '        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);',
    '',
    '        /// <summary>',
    '        /// **官方口径**的理智推进（新增重载；旧的 `Tick(dt, torchOn, inSafeZone, lightsOut)` 保留不动）。',
    '        ///',
    '        /// 公式（全部数值来自 config 的 sanity.drain.official，出处见该段 _src）：',
    '        ///',
    '        /// 基础值 = passivePerSec[地图大小][阶段]',
    '        /// 乘数   = difficultyMultiplier[难度] + (血月 ? bloodMoonAdditive : 0)',
    '        ///        × (单人 ? soloPassiveMultiplier : 1)',
    '        /// 房间修正：主灯全开 → 0 ；大暗区 → ×0.2（即只降到 80%）；火源 → ×(1 − fireTierFactor)',
    '        /// Setup 保底：任何来源不得把理智压到 setupFloor 以下',
    '        /// ⚠ **手电（torchOn）不参与流失计算** —— 官方明确手电不能停止流失。',
    '        ///    旧重载里"有手电就不掉"是**错的**，此处**故意不再传手电**，防止后人顺手加回去。',
    '        /// </summary>',
    '        public void Tick(float dt, in SanityTickContext ctx)',
    '        {',
    '            if (dt <= 0f || Collapsed) return;',
    '',
    '            // 基础值：config 的被动流失表（取正值，应用时按扣减处理）',
    '            string sizeKey = ctx.MapSize == MapSizeBand.Small ? "small"',
    '                           : ctx.MapSize == MapSizeBand.Medium ? "medium" : "large";',
    '            string phaseKey = ctx.Phase == MatchPhaseBand.Setup ? "setup" : "normal";',
    '            float basePerSec = _cfg.Float("sanity.drain.official.passivePerSec." + sizeKey + "." + phaseKey, 0.12f);',
    '',
    '            float mult = ctx.DifficultyMultiplier > 0f ? ctx.DifficultyMultiplier : 1f;',
    '            if (ctx.BloodMoon) mult += _cfg.Float("sanity.drain.official.bloodMoonAdditive", 1f);',
    '            if (ctx.Solo) mult *= _cfg.Float("sanity.drain.official.soloPassiveMultiplier", 0.5f);',
    '',
    '            float ratio = 1f;',
    '            if (ctx.MainLightOn)',
    '            {',
    '                // 主灯全开：普通房间为 0；大暗区仍保留 20%（官方：只降到 80%）',
    '                ratio = ctx.LargeDarkZone',
    '                    ? 1f - _cfg.Float("sanity.drain.official.light.largeDarkZoneMinRatio", 0.8f)',
    '                    : 0f;',
    '            }',
    '            else if (ctx.FireTier > 0)',
    '            {',
    '                float cut = _cfg.Float("sanity.drain.official.light.fireTierFactor." + ctx.FireTier, 0f);',
    '                ratio = Clamp01(1f - cut);',
    '            }',
    '',
    '            float drain = basePerSec * mult * ratio;',
    '            if (drain > 0f) Apply(-drain * dt, SanitySource.Darkness);',
    '',
    '            // Setup 保底：任何来源都不得把理智压到 setupFloor 以下（鬼能力/诅咒道具另由各自入口施加）',
    '            if (ctx.Phase == MatchPhaseBand.Setup)',
    '            {',
    '                float floor = _cfg.Float("sanity.drain.official.setupFloor", 50f);',
    '                if (Value < floor) SetValue(floor, SanitySource.Recover);',
    '            }',
    '        }',
    '',
    '        /// <summary>注视怪物（按住看它会持续掉理智）。</summary>',
  ].join('\n'),
  '② 新增官方口径 Tick 重载');

if (!checkOnly) {
  const bak = FILE + '.bak-sanTick';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[sanity-tick] 官方口径 Tick' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
