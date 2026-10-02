using System;

namespace Whisper.Core.Contracts
{
    /// <summary>
    /// 语音服务契约（V9 §13.2 / §13.5）。判定与传输解耦：声纹强度判定完全在本地，
    /// 本接口只负责「让队友听见」与提供本地采集能量。
    /// Unity Vivox 的实现类是唯一允许 import 第三方命名空间的位置（Assets/Scripts/Audio/）。
    /// </summary>
    public interface IVoiceService
    {
        bool IsMuted { get; }

        /// <summary>
        /// 本地参与者音频能量归一化值 0..1（V9 §13.5 采集源优先级 ①：
        /// Vivox 本地参与者能量回调，零额外采集；② external audio input；③ 退化为轮盘驱动）。
        /// </summary>
        float LocalEnergy01 { get; }

        /// <summary>频道名 = 房间码 + 局序号；频道生命周期跟随 Fusion 房间（Host 迁移时频道不动）。</summary>
        void JoinChannel(string channelName);
        void LeaveChannel();

        /// <summary>对局内长按头像屏蔽（V9 §16 合规第 3 项：屏蔽用 LocalMute）。</summary>
        void SetLocalMute(string participantId, bool muted);

        event Action<string, float> OnParticipantEnergy;
    }
}
