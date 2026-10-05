using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Whisper.Net.Direct
{
    /// <summary>
    /// 本机局域网地址的挑选（房间码的**前置**：要建房，先得知道把哪个地址编进码里）。
    ///
    /// ## 为什么单独一个类
    /// `RoomCode` 只做「地址+端口 → 码」的编解码，**它不负责取地址**；而 `UdpV6NetService.TryHost`
    /// 需要一个已经编好的码。此前仓里**没有任何取本机地址的代码**（实测 grep 无命中），
    /// 于是"建房"这条路在组合根上根本走不通 —— 本类是补上这一环。
    ///
    /// ## 选地址的判据（不猜，按可用性排序）
    /// 1. **全局 IPv6 优先**：零信令直连的设计前提（见 `RoomCode` 头注释：端到端无 NAT）。
    ///    只排除 loopback / link-local（fe80::/10）——后者出了网段就没用。
    /// 2. 否则取**私网 IPv4**（10./172.16-31./192.168.）——同一局域网内一样能直连，
    ///    只是跨网段/跨 NAT 不行。这条路径保证"同一 WiFi 下开黑"成立。
    /// 3. 再否则取第一个非 loopback 的单播地址（最坏情况，总比 loopback 强）。
    /// 4. 全都没有 → 返回 false（**不抛异常**：网络探测失败不该让游戏起不来）。
    ///
    /// ## 纯 C# 纪律
    /// 本类只用 System.Net.*，不引用 UnityEngine —— 因此可进 `native/csharp-verify` 真编译真跑，
    /// 也能被 EditMode 断言直接调用（`PickBestCandidates` 是纯函数，注入候选即可测，不依赖真网卡）。
    /// </summary>
    public static class LanAddress
    {
        /// <summary>房间码里的默认端口（见 RoomCode 头注释的 38000 约定）。</summary>
        public const int DefaultPort = 38000;

        /// <summary>取本机最适合编进房间码的地址；失败返回 false。</summary>
        public static bool TryGetLocal(out string host, int port = DefaultPort)
        {
            host = null;
            var cands = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip == null) continue;
                        if (IPAddress.IsLoopback(ip)) continue;
                        if (ip.AddressFamily != AddressFamily.InterNetwork
                            && ip.AddressFamily != AddressFamily.InterNetworkV6) continue;
                        cands.Add(ip.ToString());
                    }
                }
            }
            catch (System.Exception)
            {
                return false;
            }
            host = PickBestCandidates(cands);
            return host != null;
        }

        /// <summary>
        /// **纯函数**：按可用性排序挑选地址（供断言注入候选，不依赖真网卡）。
        /// 返回 null 表示没有可用地址。
        /// </summary>
        public static string PickBestCandidates(IEnumerable<string> candidates)
        {
            if (candidates == null) return null;
            string globalV6 = null, privV4 = null, anyV4 = null, anyV6 = null;
            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                if (!IPAddress.TryParse(c, out var ip)) continue;
                if (IPAddress.IsLoopback(ip)) continue;
                bool v6 = ip.AddressFamily == AddressFamily.InterNetworkV6;
                if (v6)
                {
                    if (IsIPv6LinkLocal(ip)) continue;
                    if (globalV6 == null) globalV6 = c;
                    if (anyV6 == null) anyV6 = c;
                }
                else
                {
                    if (IsPrivateIPv4(ip))
                    {
                        if (privV4 == null) privV4 = c;   // 私网优先于公网 IPv4（局域网开黑是主场景）
                    }
                    else if (anyV4 == null) anyV4 = c;
                }
            }
            return globalV6 ?? privV4 ?? anyV4 ?? anyV6;
        }

        /// <summary>fe80::/10 链路本地（出了本网段不可达，不能编进房间码）。</summary>
        static bool IsIPv6LinkLocal(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return b.Length == 16 && b[0] == 0xfe && (b[1] & 0xc0) == 0x80;
        }

        /// <summary>RFC1918 私网（10/8、172.16/12、192.168/16）。</summary>
        static bool IsPrivateIPv4(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            return false;
        }
    }
}
