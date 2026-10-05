using System;
using Whisper.Core.Contracts;

namespace Whisper.Net.Direct
{
    /// <summary>
    /// 联机会话编排：把「我建一个房」/「我用这个码加入」变成一条**语义明确**的调用。
    ///
    /// ## 为什么需要它（本项目的真实缺口）
    /// `UdpV6NetService` 早就写好了，`LocalNetService` 也同契约，但
    /// `GameBootstrap.TryInstallServices` 里写死的是 `Services.Install(new LocalNetService(tickRate))`
    /// —— 即**本机回环桩**：`SendVoiceStimulus` / `SendLocalPlayer` 都只写进本地列表，不跨进程、不联网。
    /// 后果：两台设备**永远连不上**（"可开黑"的头号阻塞）。
    ///
    /// 缺的其实只有两件事：
    ///   ① **取本机地址**并编成房间码（`LanAddress` + `RoomCode`，此前仓里没有取地址的代码）；
    ///   ② 一个**装配点**，按「单人 / 建房 / 加入」三态选择实现并调用 `TryHost` / `TryJoin`。
    /// 本类就是 ②；`NetRoomCode` 是给 UI 展示的那串码。
    ///
    /// ## 纪律
    /// - **失败不抛异常**：网络路径上抛异常等于掉线，一律返回 false + out 原因。
    /// - `LocalNetService` 仍是**单人与离线兜底**的默认实现（无网卡/无权限时游戏照样能起）。
    /// - 真实现只在有明确意图（建房/加入）时才构造 —— 不在大厅里偷偷开 socket。
    /// </summary>
    public static class LanSession
    {
        /// <summary>建房成功后**要发给朋友的房间码**；未建房时为 null。</summary>
        public static string NetRoomCode { get; private set; }

        /// <summary>当前是否用了真实联机实现（false = 仍在用本机回环桩）。</summary>
        public static bool UsingRealNet { get; private set; }

        /// <summary>
        /// 建房得到的房间码**可达范围**（跨地区 / 仅同网段 / 不确定）。
        /// UI 必须如实显示它 —— 见 RoomReach 注释（"能跨地区"与"只在家里连得上"是两种承诺）。
        /// </summary>
        public static RoomReach Reach { get; private set; } = RoomReach.Unknown;

        /// <summary>
        /// 建房：取本机地址 → 编房间码 → 起 UDP 服务并监听。
        /// 成功后 <see cref="NetRoomCode"/> 即别人要输入的码（UI 应显示在菜单板右上角）。
        /// </summary>
        public static bool TryHost(INetService fallback, int tickRate, int batchEveryTicks,
                                   int transformSendHz, int maxPlayers,
                                   out INetService net, out string roomCode, out string reason)
        {
            net = fallback;
            roomCode = null;
            reason = null;
            if (!LanAddress.TryGetLocal(out var host, LanAddress.DefaultPort))
            {
                reason = "本机没有可用的局域网地址（只有 loopback）";
                return false;
            }
            try
            {
                roomCode = RoomCode.Encode(host, LanAddress.DefaultPort);
            }
            catch (Exception ex)
            {
                reason = $"房间码编码失败（{ex.GetType().Name}）：{ex.Message}";
                return false;
            }
            var svc = new UdpV6NetService(tickRate, batchEveryTicks, transformSendHz, maxPlayers);
            if (!svc.TryStart(roomCode, asHost: true))
            {
                reason = $"监听失败（地址 {host}:{LanAddress.DefaultPort} 可能被占用）";
                return false;
            }
            net = svc;
            NetRoomCode = roomCode;
            UsingRealNet = true;
            Reach = RoomReachJudge.OfRoomCode(roomCode);   // 房间码本身携带可达范围，如实记录
            return true;
        }

        /// <summary>
        /// 加入：按房间码里的主机地址直连。码非法或连不上都返回 false（不抛异常）。
        /// 「连上了」的最终判据是**是否收到过主机包**（`HasHostEndpoint` / `DatagramsReceived`），
        /// 因为 UDP 无握手，bind 成功不等于对面在。
        /// </summary>
        public static bool TryJoin(INetService fallback, int tickRate, int batchEveryTicks,
                                   int transformSendHz, int maxPlayers, string roomCode,
                                   out INetService net, out string reason)
        {
            net = fallback;
            reason = null;
            if (string.IsNullOrWhiteSpace(roomCode)) { reason = "房间码为空"; return false; }
            if (!RoomCode.TryDecode(roomCode.Trim().ToUpperInvariant(), out var host, out var port))
            {
                reason = "房间码格式不对（应为 12 位 IPv4 或 31 位 IPv6 的大写 base32）";
                return false;
            }
            var svc = new UdpV6NetService(tickRate, batchEveryTicks, transformSendHz, maxPlayers);
            if (!svc.TryJoin(roomCode.Trim().ToUpperInvariant()))
            {
                reason = $"加入失败（主机 {host}:{port}）";
                return false;
            }
            net = svc;
            NetRoomCode = roomCode.Trim().ToUpperInvariant();
            UsingRealNet = true;
            Reach = RoomReachJudge.OfRoomCode(NetRoomCode);
            return true;
        }

        /// <summary>回到单机：清掉房间码与真实现标记。</summary>
        public static void ResetToSolo()
        {
            NetRoomCode = null;
            UsingRealNet = false;
            Reach = RoomReach.Unknown;
        }
    }
}
