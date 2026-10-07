namespace Whisper.Gameplay.Hearing
{
    /// <summary>
    /// 声纹刺激（V9 §6 / 灰盒 `__m2.makeStimulus` 的数据形态）。
    /// 强度与半径**必须**来自配置表 stimulusSources，代码内不得硬编码数值（§19.5）。
    /// </summary>
    public struct Stimulus
    {
        public string SourceKey;    // voice_whisper / voice_normal / voice_shout / run_footstep ...
        public string Type;         // voice | footstep | device | prop
        public float Intensity;
        /// <summary>这一声能传多远；null = 无限（仅 globalBroadcast 场景）。</summary>
        public float? RadiusM;
        public bool GlobalBroadcast;
        public float X, Z;
        public long Tick;

        public Stimulus(string sourceKey, string type, float intensity, float? radiusM, bool globalBroadcast, float x, float z, long tick)
        {
            SourceKey = sourceKey; Type = type; Intensity = intensity; RadiusM = radiusM;
            GlobalBroadcast = globalBroadcast; X = x; Z = z; Tick = tick;
        }
    }

    /// <summary>听觉判定的上下文修正（V9 §7 表7-1 / §8 人格包）。</summary>
    public struct HearingContext
    {
        /// <summary>怪物感知加成（0.2 = +20%）。玩家处于"恐惧"及以上理智档时由玩法层注入。</summary>
        public float PerceptionBonus;
        /// <summary>定位惩罚（0.3 = 精度 -30%）；「溺水者」人格效果，表现为"听得到但找不准"。</summary>
        public float LocalizationPenalty;
    }

    /// <summary>可听性判定结果（对应灰盒 canHear 的返回）。</summary>
    public struct HearingResult
    {
        public bool Audible;
        public float DistanceM;
        public float EffectiveThreshold;
        public float Margin;
        public float AttenuationDb;
        /// <summary>intensity_below_threshold | out_of_radius | ok</summary>
        public string Reason;
    }
}
