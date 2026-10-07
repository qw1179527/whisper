using System;
using System.Collections.Generic;

namespace Whisper.Core.Contracts
{
    /// <summary>对局阶段（V9 §13.4 同步对象④）。</summary>
    public enum MatchPhase
    {
        Lobby = 0,
        Loading = 1,
        Playing = 2,
        Extraction = 3,   // 撤离窗口开启（V9 §7：撤离倒计时 60 秒内怪物狂暴）
        Ended = 4,
    }

    /// <summary>玩家位姿快照（V9 §13.4 同步对象①：玩家 Transform，10Hz 插值）。</summary>
    public readonly struct PlayerSnapshot
    {
        public readonly string PlayerId;
        public readonly float X, Y, Z;
        /// <summary>朝向（度，绕 Y）。</summary>
        public readonly float Yaw;
        /// <summary>该玩家视角（本地玩家为实时值，远端为插值结果）。</summary>
        public readonly bool IsLocal;
        /// <summary>0=镇定 1=崩溃边缘（V9 附录 A-2 理智档位归一化；由玩法层写入，网络层只负责同步）。</summary>
        public readonly float Sanity01;

        public PlayerSnapshot(string playerId, float x, float y, float z, float yaw, bool isLocal, float sanity01)
        {
            PlayerId = playerId; X = x; Y = y; Z = z; Yaw = yaw; IsLocal = isLocal; Sanity01 = sanity01;
        }
    }

    /// <summary>道具/门状态快照（V9 §13.4 同步对象③）。</summary>
    public readonly struct PropState
    {
        public readonly string PropId;
        /// <summary>是否已开启/被拿走（门=开启，道具=已拾取）。</summary>
        public readonly bool Open;
        /// <summary>门是否上锁（道具忽略此字段）。</summary>
        public readonly bool Locked;
        /// <summary>手电电量 0..1（仅手电有意义；其余为 1）。</summary>
        public readonly float Charge01;

        public PropState(string propId, bool open, bool locked, float charge01)
        {
            PropId = propId; Open = open; Locked = locked; Charge01 = charge01;
        }
    }

    /// <summary>
    /// 网络状态快照（只读）——玩法层通过 `INetService.Snapshot` 读取，**不需要**引用 Net 模块，
    /// 从而满足 V9 §13.2「玩法代码只依赖接口」与 §13.4「四类同步对象」的交叉要求。
    /// </summary>
    public sealed class NetworkSnapshot
    {
        public readonly MatchPhase Phase;
        public readonly IReadOnlyList<PlayerSnapshot> Players;
        public readonly IReadOnlyList<PropState> Props;
        public readonly int EvidenceCount;
        /// <summary>世界状态哈希（V9 §13.6 断线重连快照比对用）。</summary>
        public readonly string WorldHash;

        public NetworkSnapshot(MatchPhase phase, IReadOnlyList<PlayerSnapshot> players,
            IReadOnlyList<PropState> props, int evidenceCount, string worldHash)
        {
            Phase = phase; Players = players; Props = props; EvidenceCount = evidenceCount; WorldHash = worldHash;
        }
    }
}
