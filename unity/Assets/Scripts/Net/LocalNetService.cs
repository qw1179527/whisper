using System;
using System.Collections.Generic;
using Whisper.Core.Contracts;

namespace Whisper.Net
{
    /// <summary>
    /// INetService 的**本机桩实现**（零第三方依赖）。
    ///
    /// 纪律（V9 §13.2）：SDK 实现类是唯一允许 import 第三方命名空间的位置 —— 本文件刻意**不引任何 SDK**，
    /// 因此本机（无 Fusion、无 Unity）也能编译装配；真正的 `FusionNetService` 将来放同目录，
    /// 只替换实现、不动接口与玩法代码。`[Networked]` 同步字段也只能写在 Net/ 程序集里。
    ///
    /// 状态面（V9 §13.4 ①③④）：以可写的本地快照承载，供玩法层**只读**读取；
    /// 真实现里这些字段由 Fusion 状态同步驱动，玩法代码的读取方式不变。
    /// </summary>
    public sealed class LocalNetService : INetService
    {
        public bool IsHost { get; private set; }
        public bool IsConnected { get; private set; }
        /// <summary>固定 60 Tick/s（V9 §13.4），由组合根从配置表注入以免硬编码。</summary>
        public int TickRate { get; }

        public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;

        readonly List<PlayerSnapshot> _players = new List<PlayerSnapshot>();
        readonly Dictionary<string, PropState> _props = new Dictionary<string, PropState>(StringComparer.Ordinal);
        int _evidenceCount;
        string _worldHash = "-";

        public LocalNetService(int tickRate = 60) => TickRate = tickRate;

        public event Action<string> OnRoomClosed;
        public event Action<bool> OnHostMigration;
        public event Action<MatchPhase> OnPhaseChanged;
        public event Action<PropState> OnPropChanged;
        public event Action<PlayerSnapshot> OnPlayerUpdated;

        /// <summary>只读快照：每次访问重建（桩实现不必缓存；真实现由 Fusion 状态直接投影）。</summary>
        public NetworkSnapshot Snapshot =>
            new NetworkSnapshot(Phase, _players.ToArray(), new List<PropState>(_props.Values).ToArray(), _evidenceCount, _worldHash);

        public void Connect(string roomCode, string authToken)
        {
            IsHost = true;
            IsConnected = true;
        }

        public void Disconnect()
        {
            IsConnected = false;
            IsHost = false;
        }

        public void SendVoiceStimulus(in StimulusEvent stimulus)
        {
            // 本机桩：声纹事件不跨进程广播（无网络）。真实实现走 UDP（见 UdpV6NetService）。
        }

        /// <summary>
        /// 契约要求的"本地玩家位姿上行口"。本机桩没有网络，因此语义等价于**本地回显**：
        /// 把自己写进快照并触发 <see cref="OnPlayerUpdated"/>，使玩法层在单机下也能走同一条状态路径
        /// （这样"接上真网络"时不需要改玩法代码，只需把实现换成 <see cref="UdpV6NetService"/>）。
        /// </summary>
        public void SendLocalPlayer(in PlayerSnapshot local) => UpsertPlayer(local);

        // ── 以下为**驱动接口**（调试面板 / 测试 / 将来的网络回调用），不属于 INetService 契约 ──

        public void SimulateHostMigration(bool started) => OnHostMigration?.Invoke(started);
        public void SimulateRoomClosed(string reason) => OnRoomClosed?.Invoke(reason);

        public void SetPhase(MatchPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        public void UpsertPlayer(PlayerSnapshot p)
        {
            var idx = _players.FindIndex(x => x.PlayerId == p.PlayerId);
            if (idx >= 0) _players[idx] = p; else _players.Add(p);
            OnPlayerUpdated?.Invoke(p);
        }

        public void UpsertProp(PropState p)
        {
            _props[p.PropId] = p;
            OnPropChanged?.Invoke(p);
        }

        public void SetEvidence(int count) => _evidenceCount = count;
        public void SetWorldHash(string hash) => _worldHash = hash;
    }
}
