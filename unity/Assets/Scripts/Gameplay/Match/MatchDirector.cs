using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Extraction;
using Whisper.Gameplay.Sanity;

namespace Whisper.Gameplay.Match
{
    /// <summary>对局阶段（V9 §7；与 Core.Contracts.MatchPhase 对齐，此处是玩法侧的细分态）。</summary>
    public enum MatchStage
    {
        Grace,        // 开局保护期（20s）：不判接触、怪不因"看见"入追击
        Main,         // 主阶段
        FinalRage,    // 终局狂暴窗口（撤离倒计时最后 60s：怪物狂暴、寻路代理上限提升）
        Extraction,   // 撤离中
        Ended,        // 已结束
    }

    /// <summary>一条已被调度的事件（由 <see cref="MatchDirector"/> 决定何时触发）。</summary>
    public struct ScheduledEvent
    {
        public string Type;
        public float AtSecond;
        public float DurationSec;
        /// <summary>该事件在 eventPool 中的来源下标（便于复现与调试）。</summary>
        public int PoolIndex;
    }

    /// <summary>
    /// 对局调度器（V9 §7）：把「保护期 → 主阶段 → 终局狂暴 → 撤离 → 结算」串成一条**确定性**时间线，
    /// 并在局内注入 2~3 个动态事件（V9 §19.2）。
    ///
    /// 为什么单独成类：灰盒把这些判断散在 `__m12` 的 tick 里（graceDone / elapsed &gt; ...），
    /// 于是"保护期到底管了什么"只能靠读代码猜。这里显式建模，并让规则可由断言逐条钉住。
    ///
    /// 确定性：事件时间与类型由 **(matchSeed, 事件序号)** 派生，不用随机数生成器状态 ——
    /// 同一个种子必然产生同一条事件时间线，从而可复现、可对拍、可写进回放。
    /// </summary>
    public sealed class MatchDirector
    {
        readonly GameConfigReader _cfg;
        public float ElapsedSeconds { get; private set; }
        public MatchStage Stage { get; private set; } = MatchStage.Grace;
        public float GraceSeconds { get; }
        public float MatchMaxSeconds { get; }
        public float FinalRageWindowSec { get; }
        public int MinEvents { get; }
        public int MaxEvents { get; }
        public readonly List<ScheduledEvent> Schedule = new List<ScheduledEvent>();
        /// <summary>已触发的事件（按顺序）。</summary>
        public readonly List<ScheduledEvent> Fired = new List<ScheduledEvent>();

        readonly List<string> _pool = new List<string>();

        public MatchDirector(GameConfigReader cfg, int matchSeed = 0, float? matchSecondsOverride = null)
        {
            _cfg = cfg;
            GraceSeconds = Settlement.StartGraceSeconds(cfg);
            MatchMaxSeconds = matchSecondsOverride ?? cfg.Float("level.matchSeconds.0", 600f);
            FinalRageWindowSec = cfg.Float("monsterBehavior.finalRageWindowBeforeExtractionSec", 60f);
            Settlement.DynamicEventRange(cfg, out int mn, out int mx);
            MinEvents = mn; MaxEvents = mx;
            LoadPool();
            BuildSchedule(matchSeed);
        }

        void LoadPool()
        {
            var raw = GameConfig.Get("level.eventPool");
            if (raw is List<object> list)
                foreach (var o in list) if (o is string s) _pool.Add(s);
        }

        /// <summary>确定性伪随机（splitmix64 风格，取自 matchSeed 与序号）——不用 RNG 对象状态。</summary>
        static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        /// <summary>局内事件时间线：在保护期之后、终局狂暴窗口之前铺开，避免事件与狂暴重叠。</summary>
        void BuildSchedule(int matchSeed)
        {
            if (_pool.Count == 0 || MaxEvents <= 0) return;
            int count = MinEvents + (int)(Mix((ulong)matchSeed + 1UL) % (ulong)Math.Max(1, MaxEvents - MinEvents + 1));
            float windowStart = GraceSeconds + 5f;
            float windowEnd = Math.Max(windowStart + 1f, MatchMaxSeconds - FinalRageWindowSec);
            for (int i = 0; i < count; i++)
            {
                ulong h = Mix((ulong)matchSeed * 1000003UL + (ulong)i);
                float t = windowStart + (windowEnd - windowStart) * ((h % 10000UL) / 10000f);
                int poolIdx = (int)((h >> 20) % (ulong)_pool.Count);
                Schedule.Add(new ScheduledEvent
                {
                    Type = _pool[poolIdx],
                    AtSecond = (float)Math.Round(t, 2),
                    DurationSec = (float)Math.Round(6f + (h >> 32) % 10UL, 2),
                    PoolIndex = poolIdx,
                });
            }
            Schedule.Sort((a, b) => a.AtSecond.CompareTo(b.AtSecond));
        }

        /// <summary>推进 dt 秒。返回本帧新触发的事件（无则空列表）。</summary>
        public List<ScheduledEvent> Tick(float dt)
        {
            var newly = new List<ScheduledEvent>();
            if (Stage == MatchStage.Ended) return newly;
            ElapsedSeconds += dt;

            // 阶段推进：保护期 → 主阶段 → 终局狂暴 → 撤离（越界即撤离）
            // 注意顺序：先判"越界撤离"，否则一旦进了 FinalRage 就永远切不到 Extraction
            // （早期实现就是这个缺陷：只有 Main 能转 FinalRage，而越界判定写在后面且被 FinalRage 吃掉）。
            if (Stage == MatchStage.Grace && ElapsedSeconds > GraceSeconds) Stage = MatchStage.Main;
            if (ElapsedSeconds >= MatchMaxSeconds)
            {
                if (Stage != MatchStage.Ended) Stage = MatchStage.Extraction;
            }
            else if (Stage != MatchStage.Ended && Stage != MatchStage.Extraction
                     && ElapsedSeconds >= MatchMaxSeconds - FinalRageWindowSec)
            {
                Stage = MatchStage.FinalRage;
            }

            for (int i = 0; i < Schedule.Count; i++)
            {
                var e = Schedule[i];
                if (Fired.Count > i) continue;
                if (ElapsedSeconds >= e.AtSecond)
                {
                    Fired.Add(e);
                    newly.Add(e);
                }
            }
            if (ElapsedSeconds >= MatchMaxSeconds) Stage = MatchStage.Extraction;
            return newly;
        }

        /// <summary>保护期是否仍在（期间：不判接触、怪不因"看见"入追击、巡逻不进入口区）。</summary>
        public bool InGrace => Stage == MatchStage.Grace;

        /// <summary>怪物可否因"看见玩家"进入追击（保护期内不允许——给玩家看清 UI 与校准的时间）。</summary>
        public bool ChaseBySightAllowed => Stage != MatchStage.Grace;

        /// <summary>接触判定是否生效（保护期内不判定）。</summary>
        public bool ContactEnabled => Stage != MatchStage.Grace;

        /// <summary>终局狂暴是否生效（撤离倒计时进入最后窗口）。</summary>
        public bool FinalRageActive => Stage == MatchStage.FinalRage;

        /// <summary>剩余秒数（负数表示已超时）。</summary>
        public float RemainingSeconds => MatchMaxSeconds - ElapsedSeconds;

        public void EndMatch() => Stage = MatchStage.Ended;

        /// <summary>把对局按当前阶段与理智档做一次一致性校正（理智档影响怪物感知，终局狂暴提升压迫感）。</summary>
        public void ApplyTo(SanitySystem sanity, Monsters.MonsterBrain brain)
        {
            if (sanity == null || brain == null) return;
            brain.PerceptionBonus = sanity.MonsterPerceptionBonus;
        }
    }
}
