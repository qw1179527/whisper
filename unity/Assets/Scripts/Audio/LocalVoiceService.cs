using System;
using Whisper.Core.Contracts;

namespace Whisper.Audio
{
    /// <summary>
    /// IVoiceService 的**本机桩实现**（零第三方依赖）。
    ///
    /// 纪律（V9 §13.2）：真 `VivoxVoiceService` 将来放同一目录并实现同一接口；
    /// 本机桩不引 Vivox，故可在无 Unity 环境下编译装配。
    ///
    /// 关键语义（V9 §13.5「判定与传输解耦」）：`LocalEnergy01` 只是**采集源**，
    /// 声纹强度判定链在别处（Gameplay）消费它 —— 换供应商不影响判定。
    /// </summary>
    public sealed class LocalVoiceService : IVoiceService
    {
        public bool IsMuted { get; private set; }
        /// <summary>本地参与者音频能量 0..1（采集源优先级 ① 的占位：真机由 Vivox 能量回调提供）。</summary>
        public float LocalEnergy01 { get; private set; }
        public string ChannelName { get; private set; }

        public event Action<string, float> OnParticipantEnergy;

        public void JoinChannel(string channelName) => ChannelName = channelName;
        public void LeaveChannel() => ChannelName = null;
        public void SetLocalMute(string participantId, bool muted) => IsMuted = muted;

        /// <summary>测试与调试面板用：注入一路能量（模拟麦克风电平）。</summary>
        public void SimulateEnergy(string participantId, float energy01)
        {
            LocalEnergy01 = participantId == "local" ? energy01 : LocalEnergy01;
            OnParticipantEnergy?.Invoke(participantId, energy01);
        }
    }
}
