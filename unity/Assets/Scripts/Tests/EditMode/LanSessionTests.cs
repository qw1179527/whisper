using NUnit.Framework;
using Whisper.Core.Contracts;
using Whisper.Net;
using Whisper.Net.Direct;

namespace Whisper.Tests.EditMode
{
    /// <summary>
    /// 联机装配断言（「可开黑」的**前置线**）。
    ///
    /// ## 为什么必须有这些断言
    /// 本轮实测发现两个"代码看着有、路走不通"的缺口，且**都没有任何门禁覆盖**：
    ///   ① 仓里没有任何「取本机局域网地址」的代码 → `TryHost` 没有前置，建房这条路在组合根上不存在；
    ///   ② 组合根里写死的是 `LocalNetService`（本机回环桩）→ 两台设备永远连不上。
    /// 这类缺口不会被语法门禁抓（代码本身能编译），也不会被既有 45 条断言抓（它们不碰联机装配）。
    /// 所以这里补的是**判据**：地址挑选的优先级、房间码往返、以及"意图明确才开真 socket"。
    ///
    /// ## 测试纪律
    /// - `LanAddress.PickBestCandidates` 是纯函数 → 注入候选即可测，**不依赖真网卡**（CI/真机都稳）；
    /// - 不真去 bind 端口做联网集成测试（那属于真机取证，见 docs/handoff-2026-10-05c-kaikei-path.md §1）；
    /// - 房间码非法输入必须**返回 false 而不是抛异常**（玩家会粘错东西）。
    /// </summary>
    public class LanSessionTests
    {
        // ── 地址挑选 ───────────────────────────────────────────────────────

        [Test]
        public void LanAddress_PrefersGlobalIPv6()
        {
            // 零信令直连的设计前提是端到端无 NAT 的全局 IPv6，故它优先级最高。
            var picked = LanAddress.PickBestCandidates(new[]
            {
                "192.168.1.44",              // 私网 v4（可用但次优先）
                "2409:8a00:1234:5678::9",    // 全局 v6
                "10.0.0.7",
            });
            Assert.AreEqual("2409:8a00:1234:5678::9", picked);
        }

        [Test]
        public void LanAddress_FallsBackToPrivateIPv4_WhenOnlyLinkLocalV6()
        {
            // 实测常见情形：手机有 fe80:: 链路本地 v6 + 192.168.x.x 私网 v4。
            // 链路本地出了本网段不可达，**不能**编进房间码；此时必须落到私网 v4。
            var picked = LanAddress.PickBestCandidates(new[]
            {
                "fe80::1234:5678:9abc:def0",
                "192.168.1.44",
            });
            Assert.AreEqual("192.168.1.44", picked);
        }

        [Test]
        public void LanAddress_IgnoresLoopbackAndGarbage()
        {
            // loopback 发给朋友等于"连我自己"；垃圾串不能让它崩。
            var picked = LanAddress.PickBestCandidates(new[] { "127.0.0.1", "::1", "不是地址", "", "   " });
            Assert.IsNull(picked, "只有 loopback/垃圾时应当没有可用地址（返回 null）");
        }

        [Test]
        public void LanAddress_PrefersPrivateOverPublicIPv4()
        {
            // 局域网开黑是主场景：同一 WiFi 下私网地址最可靠，胜过公网 v4。
            var picked = LanAddress.PickBestCandidates(new[] { "203.0.113.9", "192.168.0.31" });
            Assert.AreEqual("192.168.0.31", picked);
        }

        // ── 房间码（加入路径的前置）────────────────────────────────────────

        [Test]
        public void RoomCode_RoundTripsIPv4()
        {
            // 加入时玩家输入的码必须能解回主机端点，否则客户端没有目的地。
            var code = RoomCode.Encode("192.168.1.44", LanAddress.DefaultPort);
            Assert.AreEqual(RoomCode.EncodedLength(false), code.Length, "IPv4 码长应为 12（定长 base32）");
            Assert.IsTrue(RoomCode.TryDecode(code, out var host, out var port), "自己编的码必须能解");
            Assert.AreEqual("192.168.1.44", host);
            Assert.AreEqual(LanAddress.DefaultPort, port);
        }

        [Test]
        public void RoomCode_RoundTripsIPv6()
        {
            var code = RoomCode.Encode("2409:8a00:1234:5678::9", 38123);
            Assert.AreEqual(RoomCode.EncodedLength(true), code.Length, "IPv6 码长应为 31（定长 base32）");
            Assert.IsTrue(RoomCode.TryDecode(code, out var host, out var port));
            Assert.AreEqual(38123, port);
            Assert.IsTrue(host.Contains("2409"), "解回的应是同一网段地址：" + host);
        }

        [Test]
        public void RoomCode_RejectsGarbageWithoutThrowing()
        {
            // 玩家会从聊天软件粘来脏东西：必须 false，不能抛。
            Assert.IsFalse(RoomCode.TryDecode("hello", out _, out _));
            Assert.IsFalse(RoomCode.TryDecode("", out _, out _));
            Assert.IsFalse(RoomCode.TryDecode(null, out _, out _));
        }

        // ── 会话装配：非法输入不开真 socket ─────────────────────────────────

        [Test]
        public void LanSession_JoinWithBadCode_FailsCleanlyAndKeepsFallback()
        {
            // 关键：失败时必须**原样返回兜底实现**，而不是留下半装配状态（否则玩法层拿到 null）。
            var fallback = new LocalNetService(60);
            var ok = LanSession.TryJoin(fallback, 60, 3, 10, 4, "NOT-A-ROOM-CODE",
                                        out var net, out var reason);
            Assert.IsFalse(ok, "非法房间码必须返回 false");
            Assert.AreSame(fallback, net, "失败时应保持兜底实现不变（不留半装配状态）");
            Assert.IsNotNull(reason, "失败必须给出可显示给玩家的原因");
            Assert.IsFalse(LanSession.UsingRealNet, "未成功连接时不应置真实现标记");
        }

        [Test]
        public void LanSession_JoinWithEmptyCode_FailsCleanly()
        {
            var fallback = new LocalNetService(60);
            Assert.IsFalse(LanSession.TryJoin(fallback, 60, 3, 10, 4, "   ", out _, out var reason));
            Assert.IsNotNull(reason);
        }

        [Test]
        public void LanSession_ResetToSolo_ClearsRoomCode()
        {
            // 建房/加入后回到单人必须清掉房间码，否则 UI 会一直显示上一局的码。
            LanSession.ResetToSolo();
            Assert.IsNull(LanSession.NetRoomCode);
            Assert.IsFalse(LanSession.UsingRealNet);
        }

        [Test]
        public void LocalNetService_RemainsTheSoloDefault()
        {
            // 单机必须仍能用本机桩起局（无网卡/无权限时不至于玩不了）。
            INetService solo = new LocalNetService(60);
            Assert.IsFalse(solo.IsConnected, "本机桩默认不在联机态");
            solo.Connect("", "");
            Assert.AreEqual(60, solo.TickRate);
        }

        // ── 房间可达范围（用户要求「跨地区为主」的可判定化）────────────────

        [Test]
        public void RoomReach_GlobalIPv6_IsCrossRegion()
        {
            // 本项目跨地区的主路径：全局 IPv6 端到端无 NAT（实测见 RoomCode 头注释）
            Assert.AreEqual(RoomReach.CrossRegion, RoomReachJudge.OfAddress("2409:8a00:1234:5678::9"));
        }

        [Test]
        public void RoomReach_PrivateIPv4_IsSameNetworkOnly()
        {
            // 私网地址跨地区必然连不上 —— 必须如实标成「仅同网段」，不能标成可跨地区
            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfAddress("192.168.1.44"));
            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfAddress("10.0.0.7"));
            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfAddress("172.20.3.9"));
        }

        [Test]
        public void RoomReach_PublicIPv4_IsUncertainNotCrossRegion()
        {
            // 实测移动网络是对称 NAT、打洞不可行 → 不能承诺跨地区（不夸大）
            Assert.AreEqual(RoomReach.Uncertain, RoomReachJudge.OfAddress("203.0.113.9"));
        }

        [Test]
        public void RoomReach_LinkLocalAndLoopback_AreNotCrossRegion()
        {
            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfAddress("fe80::1"));
            Assert.AreEqual(RoomReach.Unknown, RoomReachJudge.OfAddress("127.0.0.1"));
            Assert.AreEqual(RoomReach.Unknown, RoomReachJudge.OfAddress("乱码"));
            Assert.AreEqual(RoomReach.Unknown, RoomReachJudge.OfAddress(null));
        }

        [Test]
        public void RoomReach_FromRoomCode_MatchesAddressJudgement()
        {
            // UI 是拿**房间码**判范围的，故反解口径必须与直接判地址一致
            var v6 = RoomCode.Encode("2409:8a00:1234:5678::9", LanAddress.DefaultPort);
            Assert.AreEqual(RoomReach.CrossRegion, RoomReachJudge.OfRoomCode(v6));
            var v4 = RoomCode.Encode("192.168.1.44", LanAddress.DefaultPort);
            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfRoomCode(v4));
            Assert.AreEqual(RoomReach.Unknown, RoomReachJudge.OfRoomCode("NOT-A-CODE"));
        }

        [Test]
        public void RoomReach_Describe_NeverOverPromises()
        {
            // 文案纪律：私网码不能出现「跨地区」，跨地区码必须提示双方都要 IPv6
            var same = RoomReachJudge.Describe(RoomReach.SameNetwork);
            Assert.IsFalse(same.Contains("跨地区"), "私网码的文案不得出现「跨地区」：" + same);
            var cross = RoomReachJudge.Describe(RoomReach.CrossRegion);
            Assert.IsTrue(cross.Contains("IPv6"), "跨地区文案必须提示 IPv6 前提：" + cross);
        }
    }
}
