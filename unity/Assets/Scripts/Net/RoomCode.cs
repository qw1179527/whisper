using System;
using System.Net;
using System.Text;

namespace Whisper.Net.Direct
{
    /// <summary>
    /// 房间码编解码（**零信令服务器**的关键）。
    ///
    /// ## 为什么要有它（跨地区联机 · 免费路线的核心决定）
    /// 实测结论（2026-10-03，真机 RMX5062 / Android 16 / 中国移动）：
    ///   · IPv4 侧是**对称 NAT**（三个 STUN 服务器给出同 IP 不同端口）→ UDP 打洞不可行；
    ///     而 Photon 免费档 100 CCU 硬顶、Unity Relay 需绑支付方式 → 都排除。
    ///   · IPv6 侧是**全局可路由地址且端口守恒**（绑定 [2409:...]:38000，三个 STUN 服务器
    ///     看到的都是同一个地址、同一个端口）→ **端到端无 NAT，任何人拿地址就能直连**。
    ///
    /// 于是剩下的唯一问题不是"怎么连"，而是"**怎么知道对方地址**"。
    /// 常规做法要一台信令服务器（要钱或要账号）。本类把它消掉：
    /// **把主机地址与端口直接编进房间码**，玩家用微信/QQ 把这一串发给朋友即可。
    ///   · 不需要服务器、不需要账号、不需要绑卡、不需要端口映射
    ///
    /// ## 编码设计（长度是第一约束）
    /// 第一版用"端点文本 + base64url"，实测 **64 个字符** —— 本机断言直接判红（没人愿意转发）。
    /// 现在改为**定长二进制**：
    ///   IPv6：1 字节版本 + 16 字节地址 + 2 字节端口 = 19 字节 → base32 = 31 字符
    ///   IPv4：1 字节版本 +  4 字节地址 + 2 字节端口 =  7 字节 → base32 = 12 字符
    /// 用 base32（A-Z2-7）而不是 base64：**只有大写字母与数字，不含易混符号**，
    /// 转发、手抄、口述都不容易错。
    ///
    /// ## 纯 C# 纪律
    /// 本文件不引用 UnityEngine —— 因此能进 native/csharp-verify 跑手，在本机**真编译真跑**。
    /// </summary>
    public static class RoomCode
    {
        const byte TagV6 = 0x6;
        const byte TagV4 = 0x4;

        /// <summary>base32 字母表（RFC 4648，去掉填充 '='）。</summary>
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        static readonly int[] Reverse = BuildReverse();

        static int[] BuildReverse()
        {
            var r = new int[128];
            for (int i = 0; i < r.Length; i++) r[i] = -1;
            for (int i = 0; i < Alphabet.Length; i++) r[Alphabet[i]] = i;
            return r;
        }

        /// <summary>编码后的长度（供 UI 文案与断言使用，避免各处重算）。</summary>
        public static int EncodedLength(bool isIPv6)
        {
            int bytes = isIPv6 ? 19 : 7;
            return (bytes * 8 + 4) / 5;
        }

        /// <summary>按主机字面量推断码长（解析失败时按 IPv6 上限返回，UI 好预留空间）。</summary>
        public static int ExpectedLength(string host, int port)
        {
            bool v6 = host != null && host.IndexOf(':') >= 0;
            return EncodedLength(v6);
        }

        /// <summary>把主机端点编成房间码。IPv6 字面量请勿带方括号。</summary>
        public static string Encode(string host, int port)
        {
            if (string.IsNullOrEmpty(host)) throw new ArgumentException("host 不能为空", nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (!IPAddress.TryParse(host, out var ip))
                throw new ArgumentException("host 不是合法 IP 字面量：" + host, nameof(host));

            byte[] buf;
            var addr = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                buf = new byte[19];
                buf[0] = TagV6;
                Array.Copy(addr, 0, buf, 1, 16);
                buf[17] = (byte)(port >> 8);
                buf[18] = (byte)(port & 0xFF);
            }
            else
            {
                buf = new byte[7];
                buf[0] = TagV4;
                Array.Copy(addr, 0, buf, 1, 4);
                buf[5] = (byte)(port >> 8);
                buf[6] = (byte)(port & 0xFF);
            }
            return Base32Encode(buf);
        }

        /// <summary>解析房间码。失败返回 false（不抛异常：玩家可能粘错东西）。</summary>
        public static bool TryDecode(string code, out string host, out int port)
        {
            host = null; port = 0;
            if (string.IsNullOrWhiteSpace(code)) return false;
            // 容忍从聊天软件粘来的空白与零宽字符（实战必然遇到）
            code = code.Trim().Trim('\u200b', '\u200e', '\u200f', '\ufeff', '-', ' ').ToUpperInvariant();
            if (code.Length < 3) return false;

            var bytes = Base32Decode(code);
            if (bytes == null || bytes.Length < 3) return false;

            byte tag = bytes[0];
            if (tag == TagV6)
            {
                if (bytes.Length < 19) return false;
                var a = new byte[16]; Array.Copy(bytes, 1, a, 0, 16);
                host = new IPAddress(a).ToString();
                port = (bytes[17] << 8) | bytes[18];
            }
            else if (tag == TagV4)
            {
                if (bytes.Length < 7) return false;
                var a = new byte[4]; Array.Copy(bytes, 1, a, 0, 4);
                host = new IPAddress(a).ToString();
                port = (bytes[5] << 8) | bytes[6];
            }
            else return false;

            return port > 0;
        }

        /// <summary>是否 IPv6 房间码（UI 用来提示"对面必须有 IPv6 才能连"）。</summary>
        public static bool IsIPv6Code(string code)
        {
            if (!TryDecode(code, out var h, out _)) return false;
            return h.IndexOf(':') >= 0;
        }

        static string Base32Encode(byte[] data)
        {
            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int acc = 0, bits = 0;
            foreach (var b in data)
            {
                acc = (acc << 8) | b; bits += 8;
                while (bits >= 5) { bits -= 5; sb.Append(Alphabet[(acc >> bits) & 31]); }
            }
            if (bits > 0) sb.Append(Alphabet[(acc << (5 - bits)) & 31]);
            return sb.ToString();
        }

        static byte[] Base32Decode(string s)
        {
            int outLen = s.Length * 5 / 8;
            if (outLen == 0) return null;
            var outp = new byte[outLen];
            int acc = 0, bits = 0, o = 0;
            foreach (var ch in s)
            {
                int v = ch < 128 ? Reverse[ch] : -1;
                if (v < 0) return null;
                acc = (acc << 5) | v; bits += 5;
                if (bits >= 8) { bits -= 8; if (o < outLen) outp[o++] = (byte)((acc >> bits) & 0xFF); }
            }
            if (o != outLen) return null;
            return outp;
        }
    }
}
