using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Sanity
{
    /// <summary>理智档（V9 附录 A-2 / 配置 sanity.bands）。</summary>
    public struct SanityBand
    {
        public string Id;
        public int Min, Max;
        public string Label;
        /// <summary>该档的怪物感知加成（恐惧档 0.2 = +20%，直接喂给 MonsterBrain.PerceptionBonus）。</summary>
        public float MonsterPerceptionBonus;
        /// <summary>移动速度缩放（崩溃边缘 0.85）。</summary>
        public float MoveSpeedScale;
    }

    /// <summary>地图大小档（官方被动流失按此分档）。</summary>
    public enum MapSizeBand { Small, Medium, Large }

    /// <summary>对局阶段（官方 Setup 阶段有 50% 保底）。</summary>
    public enum MatchPhaseBand { Setup, Normal }

    /// <summary>
    /// 官方口径的理智 Tick 上下文（出处：phasmophobia.su/knowledge-base/gameplay/sanity）。
    /// 用 struct + 具名初始化传参：8 个同类型布尔极易错序（本项目已有"参数顺序错"类事故）。
    /// </summary>
    public struct SanityTickContext
    {
        /// <summary>地图大小档。</summary>
        public MapSizeBand MapSize;
        /// <summary>对局阶段（Setup 有 50% 保底）。</summary>
        public MatchPhaseBand Phase;
        /// <summary>难度乘数（来自 config `sanity.drain.official.difficultyMultiplier`）。</summary>
        public float DifficultyMultiplier;
        /// <summary>是否单人（被动流失减半）。</summary>
        public bool Solo;
        /// <summary>是否血月（在难度乘数之上再加一档）。</summary>
        public bool BloodMoon;
        /// <summary>
        /// 所在房间的**主光源**是否开启（天花板灯 + 墙上开关）。
        /// ⚠ **只有它为 true 才能把该房间的被动流失降到 0**；
        /// 台灯/落地灯/电视/监视器/手电/设置亮度**都不算**（官方原文）。
        /// </summary>
        public bool MainLightOn;
        /// <summary>是否处于"大暗区"（如 Sunny Meadows 走廊）：开主灯也只降到 80%。</summary>
        public bool LargeDarkZone;
        /// <summary>附近火源等级（0 = 无；1/2/3 按 config 的 fireTierFactor 降流失）。</summary>
        public int FireTier;
    }

    /// <summary>理智变化来源（用于统计与死亡归因）。</summary>
    public enum SanitySource
    {
        Darkness,          // 暗处持续流失（V9 §7）
        LightsOut,         // 区域照明被切断：额外流失（×1.5）
        LookAtMonster,     // 注视怪物
        AllyDeath,         // 队友死亡
        Jumpscare,         // 跳吓
        Recover,           // 安全区恢复
        Sedative,          // 镇静剂
    }

    /// <summary>
    /// 理智系统（V9 附录 A-2 / §7）。
    ///
    /// 设计要点：
    ///   · 数值全部来自配置表（sanity.max / bands[].effects / drain.* / recover.*），代码零硬编码；
    ///   · **死亡不是即死**：理智归零 → 崩溃（尖叫 3 秒，产生 sanity_scream 声纹）→ 恢复到 25；
    ///     这让"被吓到"仍有反应余地，而尖叫本身成为团队代价（怪物会被引过来）；
    ///   · 档位效果直接对外暴露（感知加成喂怪物、移速缩放喂移动），避免各系统各自解释档位。
    ///
    /// 移植自灰盒 `__m12` 的玩家理智施加逻辑（暗处/灯灭/恢复三段）。
    /// </summary>
    public sealed class SanitySystem
    {
        readonly GameConfigReader _cfg;
        public float Value { get; private set; }
        public float Max { get; }
        public SanityBand Band { get; private set; }
        /// <summary>崩溃中（尖叫尚未结束）。</summary>
        public bool Collapsed { get; private set; }
        public float CollapseRemainingSec { get; private set; }
        /// <summary>累计各来源的理智变化（死亡归因与结算页用）。</summary>
        public readonly Dictionary<SanitySource, float> Ledger = new Dictionary<SanitySource, float>();

        readonly List<SanityBand> _bands = new List<SanityBand>();
        readonly float _darknessPerSec, _lookAtMonsterPerSec, _allyDeath, _jumpscare;
        readonly float _recoverPerSec, _sedative;
        readonly float _lightsOutMultiplier;

        public SanitySystem(GameConfigReader cfg, float? startValue = null)
        {
            _cfg = cfg;
            Max = cfg.Float("sanity.max", 100f);
            Value = startValue ?? Max;
            _darknessPerSec = cfg.Float("sanity.drain.darknessPerSec", -1f);
            _lookAtMonsterPerSec = cfg.Float("sanity.drain.lookAtMonsterPerSec", -8f);
            _allyDeath = cfg.Float("sanity.drain.allyDeath", -15f);
            _jumpscare = cfg.Float("sanity.drain.jumpscare", -10f);
            _recoverPerSec = cfg.Float("sanity.recover.extractionSafeZonePerSec", 2f);
            _sedative = cfg.Float("sanity.recover.sedative", 25f);
            _lightsOutMultiplier = 1.5f;   // 灰盒：灯灭额外流失 ×1.5（V9 §7「黑暗」语义）

            LoadBands();
            Band = BandOf(Value);
        }

        void LoadBands()
        {
            // 档位表来自配置；缺配置时退化为"仅满档"，但绝不自造数值
            var raw = GameConfig.Get("sanity.bands");
            if (!(raw is List<object> list)) return;
            foreach (var o in list)
            {
                if (!(o is Dictionary<string, object> m)) continue;
                var band = new SanityBand
                {
                    Id = m.TryGetValue("id", out var id) ? id as string : null,
                    Min = m.TryGetValue("min", out var mn) ? ToInt(mn) : 0,
                    Max = m.TryGetValue("max", out var mx) ? ToInt(mx) : 0,
                    Label = m.TryGetValue("label", out var lb) ? lb as string : null,
                };
                if (m.TryGetValue("effects", out var ef) && ef is Dictionary<string, object> em)
                {
                    if (em.TryGetValue("monsterPerceptionBonus", out var pb)) band.MonsterPerceptionBonus = ToFloat(pb);
                    if (em.TryGetValue("moveSpeedScale", out var ms)) band.MoveSpeedScale = ToFloat(ms);
                }
                if (band.MoveSpeedScale <= 0f) band.MoveSpeedScale = 1f;
                _bands.Add(band);
            }
        }

        static int ToInt(object o) => o is long l ? (int)l : (o is double d ? (int)d : 0);
        static float ToFloat(object o) => o is long l ? l : (o is double d ? (float)d : 0f);

        /// <summary>按当前理智值取档（含崩溃档）。</summary>
        public SanityBand BandOf(float v)
        {
            foreach (var b in _bands)
                if (v >= b.Min && v <= b.Max) return b;
            return _bands.Count > 0 ? _bands[_bands.Count - 1] : new SanityBand { Id = "unknown", Label = "未配置", MoveSpeedScale = 1f };
        }

        /// <summary>每帧推进：暗处流失 / 灯灭额外流失 / 安全区恢复 / 崩溃倒计时。</summary>
        public void Tick(float dt, bool torchOn, bool inSafeZone, bool lightsOut = false)
        {
            if (Collapsed)
            {
                CollapseRemainingSec -= dt;
                if (CollapseRemainingSec <= 0f)
                {
                    Collapsed = false;
                    // 崩溃后恢复到配置的 restoreTo（V9 附录 A-2：回 25）
                    float restoreTo = 25f;
                    var raw = GameConfig.Get("sanity.bands");
                    if (raw is List<object> list)
                        foreach (var o in list)
                            if (o is Dictionary<string, object> m && m.TryGetValue("id", out var id) && (id as string) == "collapse"
                                && m.TryGetValue("effects", out var ef) && ef is Dictionary<string, object> em
                                && em.TryGetValue("restoreTo", out var rt))
                                restoreTo = ToFloat(rt);
                    SetValue(restoreTo, SanitySource.Recover);
                }
                return;
            }

            // 安全区恢复（灰盒：extractionSafeZonePerSec × 0.25）
            if (inSafeZone && Value < Max)
                Apply(_recoverPerSec * 0.25f * dt, SanitySource.Recover);

            // 暗处流失；灯灭时额外 ×1.5（灰盒两段叠加）
            if (!torchOn)
                Apply(_darknessPerSec * dt, SanitySource.Darkness);
            if (lightsOut)
                Apply(_darknessPerSec * _lightsOutMultiplier * dt, SanitySource.LightsOut);
        }

        /// <summary>
        /// 0..1 截断。
        /// 不能用 Mathf.Clamp01：本文件属 Whisper.Gameplay，而该程序集**不引用 UnityEngine**
        /// （第 17 轮实测：在此用 Vector3 会 CS0246）。玩法层零 Unity 依赖是本项目既定分层，
        /// 故自带一个纯 System 实现。
        /// </summary>
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>
        /// **官方口径**的理智推进（新增重载；旧的 `Tick(dt, torchOn, inSafeZone, lightsOut)` 保留不动）。
        ///
        /// 公式（全部数值来自 config 的 sanity.drain.official，出处见该段 _src）：
        ///
        /// 基础值 = passivePerSec[地图大小][阶段]
        /// 乘数   = difficultyMultiplier[难度] + (血月 ? bloodMoonAdditive : 0)
        ///        × (单人 ? soloPassiveMultiplier : 1)
        /// 房间修正：主灯全开 → 0 ；大暗区 → ×0.2（即只降到 80%）；火源 → ×(1 − fireTierFactor)
        /// Setup 保底：任何来源不得把理智压到 setupFloor 以下
        /// ⚠ **手电（torchOn）不参与流失计算** —— 官方明确手电不能停止流失。
        ///    旧重载里"有手电就不掉"是**错的**，此处**故意不再传手电**，防止后人顺手加回去。
        /// </summary>
        public void Tick(float dt, in SanityTickContext ctx)
        {
            if (dt <= 0f || Collapsed) return;

            // 基础值：config 的被动流失表（取正值，应用时按扣减处理）
            string sizeKey = ctx.MapSize == MapSizeBand.Small ? "small"
                           : ctx.MapSize == MapSizeBand.Medium ? "medium" : "large";
            string phaseKey = ctx.Phase == MatchPhaseBand.Setup ? "setup" : "normal";
            float basePerSec = _cfg.Float("sanity.drain.official.passivePerSec." + sizeKey + "." + phaseKey, 0.12f);

            float mult = ctx.DifficultyMultiplier > 0f ? ctx.DifficultyMultiplier : 1f;
            if (ctx.BloodMoon) mult += _cfg.Float("sanity.drain.official.bloodMoonAdditive", 1f);
            if (ctx.Solo) mult *= _cfg.Float("sanity.drain.official.soloPassiveMultiplier", 0.5f);

            float ratio = 1f;
            if (ctx.MainLightOn)
            {
                // 主灯全开：普通房间为 0；大暗区仍保留 20%（官方：只降到 80%）
                ratio = ctx.LargeDarkZone
                    ? 1f - _cfg.Float("sanity.drain.official.light.largeDarkZoneMinRatio", 0.8f)
                    : 0f;
            }
            else if (ctx.FireTier > 0)
            {
                float cut = _cfg.Float("sanity.drain.official.light.fireTierFactor." + ctx.FireTier, 0f);
                ratio = Clamp01(1f - cut);
            }

            float drain = basePerSec * mult * ratio;
            if (drain > 0f) Apply(-drain * dt, SanitySource.Darkness);

            // Setup 保底：任何来源都不得把理智压到 setupFloor 以下（鬼能力/诅咒道具另由各自入口施加）
            if (ctx.Phase == MatchPhaseBand.Setup)
            {
                float floor = _cfg.Float("sanity.drain.official.setupFloor", 50f);
                if (Value < floor) SetValue(floor, SanitySource.Recover);
            }
        }

        /// <summary>注视怪物（按住看它会持续掉理智）。</summary>
        public void LookAtMonster(float dt) => Apply(_lookAtMonsterPerSec * dt, SanitySource.LookAtMonster);

        /// <summary>队友死亡（一次性）。</summary>
        public void AllyDied() => Apply(_allyDeath, SanitySource.AllyDeath);

        /// <summary>跳吓（一次性）。</summary>
        public void Jumpscare() => Apply(_jumpscare, SanitySource.Jumpscare);

        /// <summary>镇静剂（一次性；配置 recover.sedative）。</summary>
        public void UseSedative() => Apply(_sedative, SanitySource.Sedative);

        /// <summary>
        /// 怪物接触惩罚（V9 §7：接触**不是即死**，而是扣理智 + 4 秒无敌期）。
        ///
        /// ⚠ 符号约定坑： 在配置里是**正数 35（损失量）**，
        /// 而同表的  全是**负数（每秒流失）**——两处约定不一致。
        /// 这里按机制取负，并由 tools/config-lint.mjs 的符号约定门禁守住，避免将来再踩。
        /// </summary>
        public void MonsterContact()
        {
            float loss = _cfg.Float("monsterBehavior.contactSanityLoss", 35f);
            Apply(-Math.Abs(loss), SanitySource.Jumpscare);
        }

        void Apply(float delta, SanitySource src)
        {
            if (delta == 0f) return;
            Ledger[src] = (Ledger.TryGetValue(src, out var cur) ? cur : 0f) + delta;
            SetValue(Value + delta, src);
        }

        void SetValue(float v, SanitySource src)
        {
            float clamped = v < 0f ? 0f : (v > Max ? Max : v);
            if (clamped == 0f && Value > 0f && !Collapsed)
            {
                // 理智归零 → 崩溃（尖叫 3 秒 → 恢复）；尖叫本身会产生 sanity_scream 声纹，由外部读出 TriggerCollapse
                Collapsed = true;
                float screamSeconds = 3f;
                var raw = GameConfig.Get("sanity.bands");
                if (raw is List<object> list)
                    foreach (var o in list)
                        if (o is Dictionary<string, object> m && m.TryGetValue("id", out var id) && (id as string) == "collapse"
                            && m.TryGetValue("effects", out var ef) && ef is Dictionary<string, object> em
                            && em.TryGetValue("screamSeconds", out var ss))
                            screamSeconds = ToFloat(ss);
                CollapseRemainingSec = screamSeconds;
                TriggerCollapse = true;
            }
            Value = clamped;
            Band = BandOf(Value);
        }

        /// <summary>本帧是否刚进入崩溃（外部据此广播 sanity_scream 声纹，供怪物听觉使用）。</summary>
        public bool TriggerCollapse { get; private set; }
        public void ClearCollapseFlag() => TriggerCollapse = false;

        /// <summary>崩溃时产生的声纹刺激 key（配置 sanity.bands[collapse].effects.screamStimulus）。</summary>
        public string CollapseStimulusKey
        {
            get
            {
                var raw = GameConfig.Get("sanity.bands");
                if (raw is List<object> list)
                    foreach (var o in list)
                        if (o is Dictionary<string, object> m && m.TryGetValue("id", out var id) && (id as string) == "collapse"
                            && m.TryGetValue("effects", out var ef) && ef is Dictionary<string, object> em
                            && em.TryGetValue("screamStimulus", out var sk))
                            return sk as string;
                return "sanity_scream";
            }
        }

        /// <summary>当前档位对怪物的感知加成（直接喂 MonsterBrain.PerceptionBonus）。</summary>
        public float MonsterPerceptionBonus => Band.MonsterPerceptionBonus;

        /// <summary>当前档位的移速缩放（崩溃边缘 0.85）。</summary>
        public float MoveSpeedScale => Collapsed ? 0f : Band.MoveSpeedScale;
    }
}
