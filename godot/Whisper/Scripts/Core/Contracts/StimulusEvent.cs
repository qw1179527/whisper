namespace Whisper.Core.Contracts
{
    /// <summary>
    /// 声纹刺激事件（V9 §13.4：瞬时 RPC，不走状态同步；§13.5：判定完全在本地）。
    /// 强度与半径的数值**必须**来自配置表（Assets/Data/config.json → stimulusSources），代码内不得硬编码。
    /// </summary>
    public readonly struct StimulusEvent
    {
        /// <summary>配置键，如 voice_whisper / voice_normal / voice_shout / run_footstep。</summary>
        public readonly string SourceKey;
        public readonly float Intensity;
        public readonly float RadiusMeters;
        /// <summary>true = 全图广播（对讲机 PTT 等）。</summary>
        public readonly bool GlobalBroadcast;
        public readonly float X;
        public readonly float Z;
        public readonly long Tick;

        public StimulusEvent(string sourceKey, float intensity, float radiusMeters, bool globalBroadcast, float x, float z, long tick)
        {
            SourceKey = sourceKey;
            Intensity = intensity;
            RadiusMeters = radiusMeters;
            GlobalBroadcast = globalBroadcast;
            X = x;
            Z = z;
            Tick = tick;
        }
    }
}
