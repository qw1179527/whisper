using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Whisper.Core.Contracts;
using Whisper.Net.Direct;

namespace Whisper.Net
{
    /// <summary>
    /// INetService 的**真实 UDP 实现**：IPv6 直连 + 房间码内嵌主机地址（零服务器 / 零账号 / 零绑卡）。
    ///
    /// ## 为什么是"自己发 UDP"而不是某个联机 SDK
    /// 手机端实测结论（HANDOFF §6）：IPv4 侧是对称 NAT（三个 STUN 服务器给出同一 IP、三个不同端口）→
    /// UDP 打洞不可行；**IPv6 侧全局可路由且端口守恒**（同一地址同一端口）→ 拿地址就能直连。
    /// 而付费 SaaS 有硬顶或要绑卡（Photon 100 CCU 单向门 / Unity Relay 需 UGS 付费方式），
    /// 所以路线定为「房间码里内嵌主机 IPv6 字面量 + 直连 UDP」，**零成本、零第三方依赖**。
    ///
    /// ## 线程模型（这一条是为真机稳定性定的，不是洁癖）
    /// 收包在 `Socket` 上是阻塞的，但 Unity 的 API 只能在主线程调用。因此：
    ///   · 后台线程 **只做一件纯 BCL 的事**：`ReceiveFrom` 拿到字节 → 塞进 `ConcurrentQueue`；
    ///   · 主线程调用 <see cref="Pump"/> 时把队列抽干 → 解析 → 触发事件（`OnPlayerUpdated` 等）。
    /// 这样事件永远在主线程触发（订阅方可以随便碰 Transform/UI），而收包不会被帧率拖累。
    ///
    /// ## 契约纪律（V9 §13.1 / §13.2）
    /// 本文件位于 `Net/` —— 全项目**唯一**允许 import 第三方命名空间的位置
    /// （`tools/gate-code.mjs` 的 C6 判据把 `System.Net.Sockets` 列入禁令、但对 `/Net/` 槽位豁免）。
    /// 玩法层（Gameplay/UI/Core）不引用本文件、也不引用任何第三方命名空间，门禁可判。
    ///
    /// ## 与<see cref="LocalNetService"/>的关系
    /// 二者实现同一个契约，可直接互换（组合根按房间码是否为空选择实现）；本类不依赖 Unity，
    /// 因此能在 `native/csharp-verify` 里**真编译真跑**，用回环 UDP 断言端到端一致。
    /// </summary>
    public sealed class UdpV6NetService : INetService, IDisposable
    {
        // ── 时基与限流（数值来自配置真源，不硬编码）──
        readonly int _tickRate;
        readonly int _batchEveryTicks;
        readonly int _transformSendHz;
        readonly int _maxPlayers;

        Socket _socket;
        IPEndPoint _hostEndPoint;              // 客户端：主机地址；主机：null（用 IPEndPoint 而不是 EndPoint：SendTo 需要具体类型，用基类会在调用点编译失败）
        System.Threading.Thread _rxThread;
        volatile bool _running;

        readonly System.Collections.Concurrent.ConcurrentQueue<RxPacket> _rxQueue =
            new System.Collections.Concurrent.ConcurrentQueue<RxPacket>();

        /// <summary>
        /// 收到的单个数据报。**刻意用具名结构体而不是元组**：
        /// 元组元素类型会跟着实参的静态类型走（`EndPoint` vs `IPEndPoint`），
        /// 出队后传给 `SendTo(IPEndPoint)` 就编译失败 —— 这个坑我踩过一次，用具名类型一次消除。
        /// </summary>
        readonly struct RxPacket
        {
            public readonly byte[] Data;
            public readonly IPEndPoint From;
            public RxPacket(byte[] data, IPEndPoint from) { Data = data; From = from; }
        }

        // ── 槽位分配（0..3，V9 §13.4 四人）──
        // 对端用 IPEndPoint 直接做键（不要存字符串再解析：EndPoint 无法直接 parse 回来）
        readonly List<(IPEndPoint ep, byte slot)> _peers = new List<(IPEndPoint, byte)>();
        readonly Dictionary<string, byte> _slotOf = new Dictionary<string, byte>(StringComparer.Ordinal);
        readonly List<PlayerSnapshot> _players = new List<PlayerSnapshot>();
        readonly Dictionary<string, PropState> _props = new Dictionary<string, PropState>(StringComparer.Ordinal);
        int _evidenceCount;
        string _worldHash = "-";
        long _lastSendTicks;

        /// <summary>发包缓冲复用（20 批/秒 × 长局，逐批 new 会喂 GC）。</summary>
        readonly WireFormat.Writer _writer = new WireFormat.Writer();
        readonly List<WireFormat.PlayerRecord> _pendingRecords = new List<WireFormat.PlayerRecord>(WireFormat.MaxPlayerSlots);

        /// <summary>
        /// 单调毫秒时钟（用于发送节流）。
        ///
        /// 【实测坑，2026-10-04】这里原先用 <c>Environment.TickCount64</c> —— 本机 .NET 8 跑手能编译、
        /// 也能跑，但**Unity 的 .NET Standard 面里没有这个成员**，本地出包直接报
        /// `CS0117: 'Environment' does not contain a definition for 'TickCount64'`。
        /// 这正是"本机断言绿 ≠ Unity 能编译"的经典盲区（门禁链不编译 Unity 侧）。
        /// 换用 <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>：纯 BCL、Unity 支持、
        /// 单调递增且不受帧率影响 —— 网络节流本就该用挂钟而不是帧时间。
        /// </summary>
        static long NowMs() => (long)(System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        public bool IsHost { get; private set; }
        public bool IsConnected { get; private set; }
        public int TickRate => _tickRate;
        public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;

        /// <summary>本机实际监听的端口（主机=房间码里的端口；客户端=系统分配的临时端口）。</summary>
        public int LocalPort => _socket != null && _socket.LocalEndPoint is IPEndPoint ep ? ep.Port : 0;

        /// <summary>已发出的数据报数（断言与 HUD 诊断用；不发包时恒 0 —— 这是"真发了没有"的直接证据）。</summary>
        public int DatagramsSent { get; private set; }
        /// <summary>已收下的数据报数。</summary>
        public int DatagramsReceived { get; private set; }
        /// <summary>因超预算被丢弃的批次数（V9 §13.4 上行 100 字节）。</summary>
        public int DroppedOverBudget { get; private set; }

        public event Action<string> OnRoomClosed;
        public event Action<bool> OnHostMigration;
        public event Action<MatchPhase> OnPhaseChanged;
        public event Action<PropState> OnPropChanged;
        public event Action<PlayerSnapshot> OnPlayerUpdated;

        /// <summary>收到对端位姿时的回调（主机侧据此把状态再分发给其余对端）。</summary>
        public Action<PlayerSnapshot, IPEndPoint> OnPeerState;

        public NetworkSnapshot Snapshot =>
            new NetworkSnapshot(Phase, _players.ToArray(), new List<PropState>(_props.Values).ToArray(), _evidenceCount, _worldHash);

        /// <param name="tickRate">固定 60（config.network.tickRate）。</param>
        /// <param name="batchEveryTicks">每 3 Tick 一批（config.network.batchEveryTicks）。</param>
        /// <param name="transformSendHz">位姿发送频率 10（config.network.transformSendHz）。</param>
        /// <param name="maxPlayers">容量硬顶 4（V9 §13.4）。</param>
        public UdpV6NetService(int tickRate, int batchEveryTicks, int transformSendHz, int maxPlayers)
        {
            _tickRate = tickRate > 0 ? tickRate : 60;
            _batchEveryTicks = batchEveryTicks > 0 ? batchEveryTicks : 3;
            _transformSendHz = transformSendHz > 0 ? transformSendHz : 10;
            _maxPlayers = maxPlayers > 0 ? maxPlayers : WireFormat.MaxPlayerSlots;
        }

        // ── 连接 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 主机：<paramref name="roomCode"/> 为本机地址编成的码（即别人要输的码），端口从码里解出后监听；
        /// 客户端：码里是主机地址，本机绑临时端口后向主机发包。
        /// 失败一律返回 false 并置 IsConnected=false —— 不抛异常（网络路径上抛异常等于掉线）。
        /// </summary>
        public bool TryStart(string roomCode, bool asHost)
        {
            Disconnect();
            if (!RoomCode.TryDecode(roomCode, out var host, out var port)) return false;
            try
            {
                var addr = IPAddress.Parse(host);
                bool v6 = addr.AddressFamily == AddressFamily.InterNetworkV6;
                _socket = new Socket(v6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork,
                                     SocketType.Dgram, ProtocolType.Udp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
                if (v6) _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);

                if (asHost)
                {
                    _socket.Bind(new IPEndPoint(v6 ? IPAddress.IPv6Any : IPAddress.Any, port));
                    _hostEndPoint = null;
                }
                else
                {
                    _socket.Bind(new IPEndPoint(v6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
                    _hostEndPoint = new IPEndPoint(addr, port);
                }

                IsHost = asHost;
                IsConnected = true;
                // 主机自己占槽 0，保证"我"永远在快照里（否则主机的玩家在别人眼里不存在）
                _slotOf["local"] = 0;
                _players.Add(new PlayerSnapshot("local", 0f, 0f, 0f, 0f, true, 1f));
                StartReceiver();
                return true;
            }
            catch (Exception)
            {
                IsConnected = false;
                IsHost = false;
                return false;
            }
        }

        public void Connect(string roomCode, string authToken) => TryStart(roomCode, asHost: true);

        /// <summary>
        /// **客户端加入路径**：码里是主机地址，本机绑一个临时端口后向主机发包。
        ///
        /// 为什么单独开一个方法而不是只靠 <c>TryStart(code, false)</c>：
        /// 契约里的 <see cref="Connect"/> 没有 host/client 语义位，而玩法层按 §13.1 不许引用 Net，
        /// 于是"我要当客户端"这件事必须有**语义明确**的入口，否则只能靠调用方记得传 false
        /// （验证者实测：原实现里 <c>Connect</c> 硬编码 asHost:true → 客户端根本没有加入路径）。
        /// </summary>
        public bool TryJoin(string roomCode) => TryStart(roomCode, asHost: false);

        /// <summary>本机是否已作为客户端连上主机（真伪由"是否收到过主机包"决定，见 <see cref="DatagramsReceived"/>）。</summary>
        public bool HasHostEndpoint => _hostEndPoint != null;

        /// <summary>
        /// **是否真的接上了对端**（而不只是"码解析成功、socket 建好了"）。
        ///
        /// 为什么需要单独一个判据：<see cref="TryJoin"/> 只做**格式与地址解析**，
        /// 主机不存在时它照样返回 true（独立验证者实测：join=True 但 datagrams=0）。
        /// 若 UI 拿 TryJoin 的返回值当"已加入房间"，会**静默停在加载页**——所以"已连上"
        /// 必须由"收到过对端的包"来定义。
        /// </summary>
        public bool IsLinked => IsConnected && (IsHost ? _peers.Count > 0 : DatagramsReceived > 0);

        public void Disconnect()
        {
            _running = false;
            try { _socket?.Close(); } catch (Exception) { /* 关闭失败不阻断 */ }
            _socket = null;
            IsConnected = false;
            IsHost = false;
            _players.Clear();
            _slotOf.Clear();
        }

        public void Dispose() => Disconnect();

        void StartReceiver()
        {
            _running = true;
            _rxThread = new System.Threading.Thread(ReceiveLoop) { IsBackground = true, Name = "WhisperUdpRx" };
            _rxThread.Start();
        }

        void ReceiveLoop()
        {
            var buf = new byte[2048];
            EndPoint any = _socket.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 0)
                : new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    int n = _socket.ReceiveFrom(buf, ref any);
                    if (n <= 0) continue;
                    var copy = new byte[n];
                    Buffer.BlockCopy(buf, 0, copy, 0, n);
                    // 队列元素类型是具名 RxPacket（From 已是 IPEndPoint），不依赖元组推断
                    _rxQueue.Enqueue(new RxPacket(copy, (IPEndPoint)any));
                }
                catch (SocketException) { if (!_running) return; /* 瞬时错误：继续收 */ }
                catch (ObjectDisposedException) { return; }
                catch (Exception) { return; }
            }
        }

        // ── 主线程：抽干收包队列并触发事件 ───────────────────────────────

        /// <summary>
        /// 主线程每帧调用（组合根已接）。返回本次处理的报文数，便于断言与诊断。
        /// **只有这里会触发事件** —— 订阅方可以安全碰 Unity 对象。
        /// </summary>
        public int Pump()
        {
            int handled = 0;
            while (_rxQueue.TryDequeue(out var pkt))
            {
                handled++;
                DatagramsReceived++;
                if (WireFormat.TryReadStateBatch(pkt.Data, out var batch)) ApplyState(batch, pkt.From);
                else if (WireFormat.TryReadStimulus(pkt.Data, out var st)) RaiseStimulus(st);
            }
            return handled;
        }

        void ApplyState(in WireFormat.StateBatch batch, IPEndPoint from)
        {
            // 对端槽位：按来源端点分配（主机视角），客户端只认主机发来的
            byte slot = SlotFor(from);
            foreach (var rec in batch.Players)
            {
                var id = "peer" + rec.Slot;
                var snap = new PlayerSnapshot(id, rec.Fx, rec.Fy, rec.Fz, rec.YawDeg, false, 1f);
                UpsertPlayer(snap);
                OnPeerState?.Invoke(snap, from);
            }
            if (batch.Phase != (WireFormat.MatchPhaseLite)Phase) SetPhase((MatchPhase)batch.Phase);
            _evidenceCount = batch.EvidenceCount;
        }

        byte SlotFor(IPEndPoint ep)
        {
            var key = ep.ToString();
            if (_slotOf.TryGetValue(key, out var s)) return s;
            byte next = (byte)_slotOf.Count;
            if (next >= _maxPlayers) next = (byte)(_maxPlayers - 1);
            _slotOf[key] = next;
            _peers.Add((ep, next));
            return next;
        }

        /// <summary>声纹事件是瞬时 RPC（V9 §13.4），不进状态批。</summary>
        void RaiseStimulus(in WireFormat.StimulusLite st)
        {
            // 线上只有 16 位源哈希（省字节）；转回契约结构时 SourceKey 用哈希的十六进制串表示，
            // 订阅侧按"未知源"处理即可 —— 判定本来就在本地（V9 §13.5），不依赖源名字符串。
            var ev = new StimulusEvent("h" + st.SourceHash.ToString("x4"), st.Intensity01, st.RadiusM, false, st.X, st.Z, 0);
            OnStimulus?.Invoke(ev);
        }

        /// <summary>收到声纹事件（瞬时 RPC）时的回调。</summary>
        public Action<StimulusEvent> OnStimulus;

        // ── 发包 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 本地玩家位姿上行（10Hz 节流，由 <c>network.transformSendHz</c> 决定）。
        /// 未连接 / 非本次 tick 窗口时静默返回 —— 网络不可用不该让玩法卡住。
        ///
        /// **必须同时写回本机快照**：玩法层读的是 <see cref="Snapshot"/>，若只发包不写快照，
        /// 那么换掉 <see cref="LocalNetService"/> 之后本机在快照里恒为 (0,0,0)——
        /// 表现为"HUD 里自己的位置永远不动"，而且是**静默**的（包照发、对端照收）。
        /// 独立验证者实测到过这一点（LocalNetService 走 UpsertPlayer 所以没暴露）。
        /// </summary>
        public void SendLocalPlayer(in PlayerSnapshot local)
        {
            if (!IsConnected || _socket == null) return;
            // 本机快照写回（与 LocalNetService.SendLocalPlayer → UpsertPlayer 语义一致）
            UpsertPlayer(local);
            // 槽位 0 = 本机（主机与客户端都把自己的位姿放在槽 0 上报）
            var rec = new WireFormat.PlayerRecord(0, local.X, local.Y, local.Z, local.Yaw);

            // 节流：每 1/transformSendHz 秒才发一次（TickRate 与 Hz 都来自配置）
            long now = NowMs();
            long minGapMs = Math.Max(1, 1000 / _transformSendHz);
            if (now - _lastSendTicks < minGapMs) return;
            _lastSendTicks = now;

            SendStateBatch(rec);
        }

        /// <summary>把一批状态发给主机（客户端）或广播给全部已知对端（主机）。</summary>
        void SendStateBatch(WireFormat.PlayerRecord self)
        {
            _pendingRecords.Clear();
            _pendingRecords.Add(self);

            int size = WireFormat.StateBatchSize(_pendingRecords.Count, 0, 0);
            if (size > WireFormat.UpBatchBudgetBytes)   // 预算硬约束：宁可不发，不发超包
            {
                DroppedOverBudget++;
                return;
            }

            _writer.Clear();
            int written = WireFormat.WriteStateBatch(_writer, NextSeq(), (WireFormat.MatchPhaseLite)Phase, _evidenceCount,
                _pendingRecords, 0, Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<ushort>(), Array.Empty<byte>());
            if (written > WireFormat.UpBatchBudgetBytes) { DroppedOverBudget++; return; }

            var bytes = _writer.ToArray();
            if (IsHost)
            {
                // 主机广播给所有已知对端（_peers 里存的是 IPEndPoint，不做字符串往返解析）
                foreach (var (ep, _) in _peers) SendTo(bytes, ep);
            }
            else if (_hostEndPoint != null) SendTo(bytes, _hostEndPoint);
        }

        int SendTo(byte[] bytes, IPEndPoint target)
        {
            try
            {
                int n = _socket.SendTo(bytes, target);
                DatagramsSent++;
                return n;
            }
            catch (Exception) { return 0; }   // 对端不可达不抛给玩法层
        }

        ushort _seq;
        ushort NextSeq() => ++_seq;

        /// <summary>声纹事件上行（瞬时 RPC；单包 14 字节，不占状态批预算）。</summary>
        public void SendVoiceStimulus(in StimulusEvent stimulus)
        {
            if (!IsConnected || _socket == null) return;
            _writer.Clear();
            WireFormat.WriteStimulus(_writer, stimulus.SourceKey, stimulus.Intensity, stimulus.X, stimulus.Z, stimulus.RadiusMeters);
            var bytes = _writer.ToArray();
            if (IsHost)
            {
                foreach (var (ep, _) in _peers) SendTo(bytes, ep);
            }
            else if (_hostEndPoint != null) SendTo(bytes, _hostEndPoint);
        }

        // ── 下行状态写入（驱动接口，非契约）────────────────────────────────

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

        public void SetPhase(MatchPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        public void SetEvidence(int count) => _evidenceCount = count;
        public void SetWorldHash(string hash) => _worldHash = hash;
        public void SimulateHostMigration(bool started) => OnHostMigration?.Invoke(started);
        public void SimulateRoomClosed(string reason) => OnRoomClosed?.Invoke(reason);
    }
}
