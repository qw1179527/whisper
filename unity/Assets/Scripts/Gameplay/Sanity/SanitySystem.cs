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
