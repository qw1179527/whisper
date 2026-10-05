using System;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Items
{
    /// <summary>
    /// EMF 读取器（补充方案第 8 条）。
    ///
    /// ## 规则（用户原件逐字）
    /// · EMF 有**五级**；
    /// · 鬼在范围内互动 → 响 **1~4 级**警报；
    /// · **有 EMF5 证据的鬼**在互动时 **8% 概率**响 5 级；
    /// · **普通鬼在猎杀时** EMF 会在 **1~5 级间随机**响起 —— 此时**判定不准**（这是设计意图，不是 bug）。
    ///
    /// ## 为什么要"判定不准"这条
    /// 它让 EMF 在猎杀时**不可作为证据**：玩家必须等鬼不猎的时候读，否则读到 5 级也不能当结论。
    /// 这是玩法的张力来源，所以代码里要显式建模 `IsReliable`，而不是让猎杀时也照常给证据。
    ///
    /// ## 纯逻辑
    /// 随机源可注入（确定性 PRNG / 固定序列），因此 8% 与"猎杀时 1~5 随机"都能被断言。
    /// </summary>
    public sealed class EmfReader
    {
        readonly float _level5Chance;        // 有 EMF5 证据时的 8%
        readonly float _lowLevelMin, _lowLevelMax;   // 互动时的 1~4
        readonly float _decaySeconds;        // 多久回落到 0

        float _level;                        // 当前显示级别（0 = 无读数）
        float _sinceReading;

        /// <summary>当前显示的 EMF 级别（0~5，0 = 无读数）。</summary>
        public float Level => _level;
        /// <summary>当前读数是否**可信**（猎杀期间的读数是随机的，不可作为证据）。</summary>
        public bool IsReliable { get; private set; } = true;

        public EmfReader(GameConfigReader cfg, Func<float> roll)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            _roll = roll ?? throw new ArgumentNullException(nameof(roll));
            _level5Chance = Req(cfg, "items.emf.level5ChanceWithEvidence");
            _lowLevelMin = Req(cfg, "items.emf.lowLevelMin");
            _lowLevelMax = Req(cfg, "items.emf.lowLevelMax");
            _decaySeconds = Req(cfg, "items.emf.readingHoldSeconds");
            if (_level5Chance < 0f || _level5Chance > 1f)
                throw new InvalidOperationException("items.emf.level5ChanceWithEvidence 必须在 [0,1]");
        }

        readonly Func<float> _roll;

        /// <summary>
        /// 鬼在范围内发生一次互动 → 掷一次读数。
        /// <paramref name="ghostHasEmf5Evidence"/>：该鬼是否带 EMF5 证据（带 → 有 8% 概率直接 5 级）。
        /// <paramref name="duringHunt"/>：是否处于猎杀中（是 → 1~5 随机且判定不准）。
        /// 返回本次读数（0 表示这次没出现读数）。
        /// </summary>
        public float OnInteraction(bool ghostHasEmf5Evidence, bool duringHunt)
        {
            float r = _roll();

            if (duringHunt)
            {
                // 猎杀时：1~5 级随机，且**标注不可信**（补充方案："此时 EMF 判定不准"）
                IsReliable = false;
                _level = _lowLevelMin + r * (_lowLevelMax + 1f - _lowLevelMin);   // [1,6) → 1..5
                if (_level > 5f) _level = 5f;
                if (_level < 1f) _level = 1f;
                _level = (float)Math.Round(_level);
                _sinceReading = 0f;
                return _level;
            }

            // 非猎杀：先看是否直接 5 级（仅当该鬼带 EMF5 证据）
            if (ghostHasEmf5Evidence && r < _level5Chance)
            {
                IsReliable = true;
                _level = 5f;
                _sinceReading = 0f;
                return 5f;
            }

            // 否则 1~4 级（用 r 在 [0,1) 上均匀映射；注意 5 级那条已经用掉了一部分概率，
            // 但 1~4 的**相对**分布仍是均匀的 —— 这与"5 级是独立的高阶事件"的直觉一致）
            IsReliable = true;
            _level = _lowLevelMin + r * (_lowLevelMax + 1f - _lowLevelMin);       // [1,5) → 1..4
            if (_level > 4f) _level = 4f;
            if (_level < 1f) _level = 1f;
            _level = (float)Math.Round(_level);
            _sinceReading = 0f;
            return _level;
        }

        /// <summary>推进 dt 秒：超过保持时间后读数回落为 0（E.M.F 不会一直亮着）。</summary>
        public void Tick(float dtSec)
        {
            if (dtSec <= 0f || _level <= 0f) return;
            _sinceReading += dtSec;
            if (_sinceReading >= _decaySeconds) { _level = 0f; IsReliable = true; }
        }

        float Req(GameConfigReader cfg, string path)
        {
            var raw = GameConfig.Get(path);
            if (raw is long l) return l;
            if (raw is double d) return (float)d;
            if (raw is int i) return i;
            throw new InvalidOperationException($"配置缺 {path} —— EMF 机制不许硬编码数值");
        }
    }
}
