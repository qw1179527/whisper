using System;

namespace Whisper.Core.Contracts
{
    /// <summary>
    /// 联机服务契约（V9 §13.2）。玩法代码只依赖本接口；
    /// Photon Fusion 2 的实现类是唯一允许 import 第三方命名空间的位置（Assets/Scripts/Net/）。
    /// </summary>
    public interface INetService
    {
        bool IsHost { get; }
        bool IsConnected { get; }

        /// <summary>固定 60 Tick/s（V9 §13.4）；每 3 Tick（50ms）一批发送。</summary>
        int TickRate { get; }

        void Connect(string roomCode, string authToken);
        void Disconnect();

        /// <summary>声纹事件：瞬时 RPC，不走状态同步（V9 §13.4）。</summary>
        void SendVoiceStimulus(in StimulusEvent stimulus);

        /// <summary>Host 迁移或房主退出导致房间关闭（V9 §13.4：买断房主退出 → 本局打完再解散）。</summary>
        event Action<string> OnRoomClosed;

        /// <summary>Host 迁移开始/结束（迁移期播放「信号干扰」遮罩 2~3 秒）。</summary>
        event Action<bool> OnHostMigration;

        // ── 只读状态面（V9 §13.4 四类同步对象 ①③④）──
        // 没有它，玩法代码就拿不到「玩家位置 / 门与道具状态 / 对局阶段」——
        // 而 Gameplay 按 §13.1 禁止引用 Net，于是这些状态只能经接口暴露。

        /// <summary>当前对局阶段（同步对象④）。</summary>
        MatchPhase Phase { get; }

        /// <summary>当前网络状态快照（玩家位姿 ① / 道具与门 ③ / 阶段 ④ / 证据计数）。只读，不可改。</summary>
        NetworkSnapshot Snapshot { get; }

        /// <summary>阶段变化（同步对象④的变更通知）。</summary>
        event Action<MatchPhase> OnPhaseChanged;

        /// <summary>门或道具状态变化（同步对象③的变更通知；按 id 过滤由订阅方自己做）。</summary>
        event Action<PropState> OnPropChanged;

        /// <summary>玩家位姿更新（同步对象①，10Hz 插值后的结果；逐帧高频，订阅方注意开销）。</summary>
        event Action<PlayerSnapshot> OnPlayerUpdated;
    }
}
