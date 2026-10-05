using System;
using System.Net;
using System.Net.Sockets;

namespace Whisper.Net.Direct
{
    /// <summary>
    /// 房间码的**可达范围**（跨地区 / 仅同网段）。
    ///
    /// ## 为什么必须有它（用户 2026-10-05 要求「以跨地区联机为主」）
    /// 本项目走「IPv6 直连 + 房间码内嵌主机地址」的零信令路线，实测结论（见 `RoomCode` 头注释）：
    ///   · **全局 IPv6** → 端到端无 NAT，拿地址即可直连 ⇒ **跨地区可用**；
    ///   · **私网 IPv4**（10./172.16-31./192.168.）→ 只在同一网段内可达 ⇒ **仅同 WiFi/局域网**；
    ///   · **公网 IPv4** → 多数移动网络是对称 NAT（实测打洞不可行），**通常连不上**，只能算"也许"。
    ///
    /// 于是"房间码"这一串字符**本身携带了可达范围**，UI 必须如实显示，
    /// 否则玩家会以为能跨地区、实际只在自己家 WiFi 里连得上 —— 那是最伤信任的一类缺陷。
    ///
    /// ## 设计
    /// 判据做成**纯函数**（只看地址字符串），因此：
    ///   · 不依赖真网卡 → EditMode 断言可注入任意地址直接测（CI 与真机一致）；
    ///   · 可从**已编好的房间码**反解出范围（`ScopeOfRoomCode`），不依赖建房那一刻的上下文。
    ///
    /// 本类不引用 UnityEngine（纯 C# 纪律），可进 `native/csharp-verify` 真编译真跑。
    /// </summary>
    public enum RoomReach
    {
        /// <summary>跨地区可用：全局 IPv6（无 NAT，拿地址即可直连）。</summary>
        CrossRegion = 0,
        /// <summary>仅同网段：私网 IPv4（同 WiFi/局域网内可用，跨地区不行）。</summary>
        SameNetwork = 1,
        /// <summary>不确定：公网 IPv4（移动网络多为对称 NAT，打洞实测不可行，通常连不上）。</summary>
        Uncertain = 2,
        /// <summary>解析失败（空码/坏码）。</summary>
        Unknown = 3,
    }

    /// <summary>房间码可达范围的判据与文案（见 <see cref="RoomReach"/> 注释）。</summary>
    public static class RoomReachJudge
    {
        /// <summary>按主机地址字面量判定可达范围。</summary>
        public static RoomReach OfAddress(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return RoomReach.Unknown;
            if (!IPAddress.TryParse(host.Trim(), out var ip)) return RoomReach.Unknown;
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (IPAddress.IsLoopback(ip)) return RoomReach.Unknown;
                var b = ip.GetAddressBytes();
                bool linkLocal = b.Length == 16 && b[0] == 0xfe && (b[1] & 0xc0) == 0x80;
                if (linkLocal) return RoomReach.SameNetwork;   // fe80:: 只在本地链路内
                return RoomReach.CrossRegion;                  // 全局 IPv6：本项目跨地区的主路径
            }
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                if (IPAddress.IsLoopback(ip)) return RoomReach.Unknown;
                return IsPrivateIPv4(ip) ? RoomReach.SameNetwork : RoomReach.Uncertain;
            }
            return RoomReach.Unknown;
        }

        /// <summary>从**已编好的房间码**反解可达范围（UI 显示与断言都用这一条口径）。</summary>
        public static RoomReach OfRoomCode(string roomCode)
        {
            if (string.IsNullOrWhiteSpace(roomCode)) return RoomReach.Unknown;
            if (!RoomCode.TryDecode(roomCode.Trim(), out var host, out _)) return RoomReach.Unknown;
            return OfAddress(host);
        }

        /// <summary>给玩家看的一句话（**不夸大**：跨地区要双方都有 IPv6）。</summary>
        public static string Describe(RoomReach reach)
        {
            switch (reach)
            {
                case RoomReach.CrossRegion: return "可跨地区（双方都需有 IPv6）";
                case RoomReach.SameNetwork: return "仅同 WiFi/局域网内可加入";
                case RoomReach.Uncertain: return "不确定（公网 IPv4 多数连不上，建议换 IPv6 网络）";
                default: return "未知（房间码无效）";
            }
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
