using System;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Hearing
{
    /// <summary>
    /// 声纹 → 听觉索敌（V9 §7 表7-2 / 灰盒 `__m2`）。
    ///
    /// 判定链：刺激强度 → 与怪的有效听觉阈值比较 → 半径衰减 → 半径外判不可听。
    /// 有效阈值 = 配置阈值 × (1 - 感知加成) ÷ (1 - 定位惩罚)。
    ///
    /// 三怪的阈值梯度（配置真源）：低语者 10（语音猎手，安静时几乎失明）、
    /// 缝匠 30（教学怪）、收殓人 55（视野为主、无视柜子）。
    /// </summary>
    public sealed class Hearing
    {
        readonly GameConfigReader _cfg;
        public Hearing(GameConfigReader cfg) => _cfg = cfg;

        /// <summary>有效听觉阈值（配置阈值 × 感知加成 ÷ 定位惩罚）。</summary>
        public float EffectiveThreshold(string monsterId, in HearingContext ctx)
        {
            float t = _cfg.Float($"monsters.{monsterId}.hearingThreshold", float.NaN);
            if (float.IsNaN(t)) throw new ArgumentException($"unknown monster: {monsterId}");
            t *= 1f - ctx.PerceptionBonus;                 // 恐惧档：感知 +20%
            if (ctx.LocalizationPenalty > 0f) t /= 1f - ctx.LocalizationPenalty;   // 溺水者：听得到但找不准
            return t;
        }

        /// <summary>某怪能否听见某条刺激（对应灰盒 canHear）。</summary>
        public HearingResult CanHear(string monsterId, in Stimulus stim, float monsterX, float monsterZ, in HearingContext ctx)
        {
            var m = _cfg.Float($"monsters.{monsterId}.hearingThreshold", float.NaN);
            if (float.IsNaN(m)) throw new ArgumentException($"unknown monster: {monsterId}");

            float dx = stim.X - monsterX;
            float dz = stim.Z - monsterZ;
            float distanceM = (float)Math.Sqrt(dx * dx + dz * dz);
            float threshold = EffectiveThreshold(monsterId, ctx);

            if (stim.Intensity < threshold)
            {
                return new HearingResult
                {
                    Audible = false, DistanceM = distanceM, EffectiveThreshold = threshold,
                    Margin = stim.Intensity - threshold, AttenuationDb = 0f,
                    Reason = "intensity_below_threshold",
                };
            }

            if (!stim.GlobalBroadcast && stim.RadiusM.HasValue)
            {
                // 半径即"这一声传多远"：按 20log10 距离比衰减（距离<半径时衰减为负、即仍有余量）
                float attenuationDb = 20f * (float)Math.Log10(Math.Max(distanceM, 1f) / Math.Max(stim.RadiusM.Value, 1f));
                float effective = stim.Intensity + attenuationDb;
                bool outOfRadius = distanceM > stim.RadiusM.Value;
                return new HearingResult
                {
                    Audible = !outOfRadius,
                    DistanceM = distanceM,
                    EffectiveThreshold = threshold,
                    Margin = effective - threshold,
                    AttenuationDb = Round4(attenuationDb),
                    // 与灰盒一致：可听时**不设** reason（只有不可听/超半径才有）
                    Reason = outOfRadius ? "out_of_radius" : null,
                };
            }

            return new HearingResult
            {
                Audible = true, DistanceM = distanceM, EffectiveThreshold = threshold,
                Margin = stim.Intensity - threshold, AttenuationDb = 0f, Reason = null,
            };
        }

        static float Round4(float v) => (float)Math.Round(v * 1e4) / 1e4f;
    }
}
