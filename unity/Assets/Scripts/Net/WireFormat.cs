using System;
using System.Collections.Generic;

namespace Whisper.Net.Direct
{
    /// <summary>
    /// 联机线格式（V9 §13.4 四类同步对象的**字节级**实现）。
    ///
    /// ## 为什么必须自己定线格式，而不是让 SDK 自己序列化
    /// V9 §13.4 给了硬带宽预算：**下行 ≤12KB/s · 上行 ≤6KB/s · 每 3 Tick（50ms）一批**（= 20 批/秒）。
    ///   · 下行折算：12000 ÷ 20 = **600 字节/批** → `DownBatchBudgetBytes = 600`（与预算一致）
    ///   · 上行折算：6000 ÷ 20 = **300 字节/批**
    ///     ⚠️ `UpBatchBudgetBytes` 取 **100**，比折算值**更保守 3 倍**（不是"6000÷20=100"——
    ///     独立复核指出原注释这个算式是错的；100 是刻意留的余量，不是预算本身）。取更严的上限
    ///     使"每批 ≤100 字节"这条断言同时满足两种口径，代价是有时需要丢低优先级变更。
    /// 这个预算下，{ 字符串 ID + float 坐标 × 4 } 的朴素序列化**必然超**——
    /// 而且超了不会报错，只会表现为"移动端流量爆掉/延迟飙升"这种上线才发现的症状。
    /// 所以线格式要显式设计、显式度量，并用断言把它钉住（见 native/csharp-verify [10]）。
    ///
    /// ## 定长纪律
    /// 每条记录都是定长（位置用 16 位量化、朝向用 8 位），因此：
    ///   · 大小可**事先算准**（不靠猜、不靠实测才知道）
    ///   · 解析不需要逐字段判断长度 → 无解析歧义、无分配
    ///
    /// ## 量化与精度（按 1 米网格关卡的尺度选）
    ///   位置：16 位覆盖 ±128m，步进 1/256 m ≈ **3.9 毫米** —— 远小于 V9 的位置校验容差（速度×1.2×Δt）
    ///   朝向：8 位覆盖 360°，步进 **1.4°** —— 配合 10Hz 插值足够顺滑
    ///   理智：8 位覆盖 0..1，步进 0.4% —— 只用于 HUD 与感知加成，不必更细
    ///
    /// ## 玩家槽位而不是字符串 ID
    /// 每条记录 1 字节槽位（0..3），房间码/握手阶段由 Host 分配槽位↔PlayerId 映射。
    /// 这是"每批省下几十字节"的关键取舍：V9 §13.4 的 4 人上限让槽位足够用。
    ///
    /// ## 纯 C# 纪律
    /// 不引用 UnityEngine → 能进 native/csharp-verify，在本机**真编译真跑**并断言字节数。
    /// </summary>
    public static class WireFormat
    {
        public const byte ProtocolVersion = 1;

        /// <summary>协议消息类型（同步对象分类的线上体现）。</summary>
        public enum Kind : byte
        {
            Hello = 1,       // 握手：槽位分配
            State = 2,       // 一批状态（①②③④合并发送）
            Stimulus = 3,    // 声纹事件（瞬时 RPC，V9 §13.4 明确"不走状态同步"）
            Ack = 4,         // 确认 / 心跳
        }

        // ── 定长尺寸（全部有断言钉住，改一处必须同步改断言）──
        public const int HeaderSize = 5;        // version(1)+kind(1)+seq(2)+flags(1)
        public const int PlayerRecordSize = 8;  // slot(1)+X(2)+Y(2)+Z(2)+Yaw(1)
        public const int SanityRecordSize = 2;  // slot(1)+sanity01(1)
        public const int PropRecordSize = 3;    // propHash(2)+flags(1)
        // 声纹事件 = HeaderSize(5，含 Begin 写的 version/kind/seq/flags)
        //            + srcHash(2) + intensity01(1) + x(2) + z(2) + radius8(1) + reserved(1) = 14
        public const int StimulusSize = 14;
        public const int MaxPlayerSlots = 4;    // V9 §13.4 四人合作

        /// <summary>
        /// 一批状态在**不带任何记录**时的固定字节数：
        /// 头(5) + phase(1) + evidenceCount(1) + playerCount(1) + sanityCount(1) + propCount(1) = 10。
        /// 抽成常量是因为三个长度字段各占 1 字节，容易漏算（我漏算过三次，每次"看起来都自洽"）。
        /// </summary>
        public const int StateBatchFixedBytes = 10;

        /// <summary>V9 §13.4 上行预算折算出的每批字节上限。</summary>
        public const int UpBatchBudgetBytes = 100;
        /// <summary>V9 §13.4 下行预算（12KB/s ÷ 20 批/s）。</summary>
        public const int DownBatchBudgetBytes = 600;

        /// <summary>量化位置：米 → 16 位（±128m，步进 1/256m）。</summary>
        public static ushort QuantizePos(float meters)
        {
            int v = (int)Math.Round(meters * 256.0);
            if (v > short.MaxValue) v = short.MaxValue;
            if (v < short.MinValue) v = short.MinValue;
            return unchecked((ushort)(short)v);
        }

        /// <summary>反量化。</summary>
        public static float DequantizePos(ushort q) => unchecked((short)q) / 256f;

        /// <summary>量化朝向：度 → 8 位（360/256 ≈ 1.41° 步进）。</summary>
        public static byte QuantizeYaw(float degrees)
        {
            float d = degrees % 360f;
            if (d < 0f) d += 360f;
            int v = (int)Math.Round(d * 256.0 / 360.0) & 0xFF;
            return (byte)v;
        }

        public static float DequantizeYaw(byte q) => q * 360f / 256f;

        /// <summary>量化 0..1。</summary>
        public static byte Quantize01(float v01)
        {
            if (v01 <= 0f) return 0;
            if (v01 >= 1f) return 255;
            return (byte)Math.Round(v01 * 255f);
        }

        public static float Dequantize01(byte q) => q / 255f;

        /// <summary>字符串 ID → 16 位哈希（道具/门/刺激源共用；用 FNV-1a 保证跨进程一致）。</summary>
        public static ushort Hash16(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            uint h = 2166136261u;
            foreach (var ch in s)
            {
                h ^= (byte)(ch & 0xFF); h *= 16777619u;
                h ^= (byte)((ch >> 8) & 0xFF); h *= 16777619u;
            }
            return (ushort)(h & 0xFFFF);
        }

        /// <summary>
        /// 写入器：把一条协议消息顺序写进一块复用缓冲（零分配，避免每批 20 次 GC）。
        /// 所有写入都是定长字段，因此长度**可事先算准**（见 <see cref="StateBatchSize"/>）。
        /// </summary>
        public sealed class Writer
        {
            readonly List<byte> _buf = new List<byte>(256);
            public int Length => _buf.Count;
            public void Clear() => _buf.Clear();
            public byte[] ToArray() => _buf.ToArray();
            public void U8(byte v) => _buf.Add(v);
            public void U16(ushort v) { _buf.Add((byte)(v >> 8)); _buf.Add((byte)(v & 0xFF)); }
            public void Begin(Kind kind, ushort seq, byte flags = 0)
            {
                _buf.Clear();
                U8(ProtocolVersion); U8((byte)kind); U16(seq); U8(flags);
            }
        }

        /// <summary>
        /// 读入器：**逐字段带边界检查**的有状态游标。
        /// 每个 U8/U16 都返回 bool 而不是抛异常 —— 网络路径上一次未捕获异常就等于掉线
        /// （空包解析抛 NullReferenceException 就是被断言抓到过的真实缺陷）。
        /// </summary>
        public struct Reader
        {
            readonly byte[] _b; int _i;
            public Reader(byte[] b) { _b = b; _i = 0; }
            public int Position => _i;
            public int Remaining => _b.Length - _i;
            public bool U8(out byte v) { if (Remaining < 1) { v = 0; return false; } v = _b[_i++]; return true; }
            public bool U16(out ushort v)
            {
                if (Remaining < 2) { v = 0; return false; }
                v = (ushort)((_b[_i] << 8) | _b[_i + 1]); _i += 2; return true;
            }
            public byte[] Underlying => _b;
        }

        /// <summary>批内玩家记录（定长 8 字节）。</summary>
        public struct PlayerRecord
        {
            public byte Slot, Yaw;
            public ushort X, Y, Z;
            public PlayerRecord(byte slot, float x, float y, float z, float yaw)
            {
                Slot = slot; X = QuantizePos(x); Y = QuantizePos(y); Z = QuantizePos(z); Yaw = QuantizeYaw(yaw);
            }
            public float Fx => DequantizePos(X);
            public float Fy => DequantizePos(Y);
            public float Fz => DequantizePos(Z);
            public float YawDeg => DequantizeYaw(Yaw);
        }

        /// <summary>
        /// 编码一批状态。返回写入的字节数；**调用方必须检查它不超过 UpBatchBudgetBytes**。
        /// 超出时本方法不抛异常而是返回原值 —— 由上层决定丢弃哪些低优先级项（阶段④ > 门③ > 位姿①）。
        /// </summary>
        public static int WriteStateBatch(Writer w, ushort seq, MatchPhaseLite phase, int evidenceCount,
            IReadOnlyList<PlayerRecord> players, byte sanityCount, IReadOnlyList<byte> sanitySlots, IReadOnlyList<byte> sanityValues,
            IReadOnlyList<ushort> propHashes, IReadOnlyList<byte> propFlags)
        {
            w.Begin(Kind.State, seq);
            w.U8((byte)phase);
            w.U8((byte)Math.Min(evidenceCount, 255));

            int count = players?.Count ?? 0;
            if (count > MaxPlayerSlots) count = MaxPlayerSlots;
            w.U8((byte)count);
            for (int i = 0; i < count; i++)
            {
                var p = players[i];
                w.U8(p.Slot); w.U16(p.X); w.U16(p.Y); w.U16(p.Z); w.U8(p.Yaw);
            }

            w.U8(sanityCount);
            for (int i = 0; i < sanityCount; i++) { w.U8(sanitySlots[i]); w.U8(sanityValues[i]); }

            int pc = propHashes?.Count ?? 0;
            w.U8((byte)Math.Min(pc, 255));
            for (int i = 0; i < pc; i++) { w.U16(propHashes[i]); w.U8(propFlags[i]); }

            return w.Length;
        }

        /// <summary>解析一批状态（严格定长校验：长度对不上直接判非法，不"尽力而为"）。</summary>
        public static bool TryReadStateBatch(byte[] data, out StateBatch batch)
        {
            batch = default;
            // 空包是**正常情况**（UDP 可收到空数据报），必须返回 false 而不是抛异常 ——
            // 断言实测抓到过这里漏判（NullReferenceException），网络代码里一次未捕获异常就是掉线。
            if (data == null || data.Length < HeaderSize) return false;
            var r = new Reader(data);
            if (!r.U8(out var ver) || ver != ProtocolVersion) return false;
            if (!r.U8(out var kind) || kind != (byte)Kind.State) return false;
            if (!r.U16(out var seq)) return false;
            if (!r.U8(out var flags)) return false;

            if (!r.U8(out var phase) || !r.U8(out var evidence)) return false;
            if (!r.U8(out var pc)) return false;
            if (pc > MaxPlayerSlots) return false;
            var players = new PlayerRecord[pc];
            for (int i = 0; i < pc; i++)
            {
                if (!r.U8(out var slot) || !r.U16(out var x) || !r.U16(out var y) || !r.U16(out var z) || !r.U8(out var yaw)) return false;
                players[i] = new PlayerRecord { Slot = slot, X = x, Y = y, Z = z, Yaw = yaw };
            }
            if (!r.U8(out var sc)) return false;
            var sanity = new (byte slot, byte val)[sc];
            for (int i = 0; i < sc; i++)
            {
                if (!r.U8(out var s)) return false;
                if (!r.U8(out var v)) return false;
                sanity[i] = (s, v);
            }
            if (!r.U8(out var hc)) return false;
            var props = new (ushort hash, byte flags)[hc];
            for (int i = 0; i < hc; i++)
            {
                if (!r.U16(out var h)) return false;
                if (!r.U8(out var f)) return false;
                props[i] = (h, f);
            }
            if (r.Remaining != 0) return false;   // 定长协议：多余字节=版本不匹配，宁可拒绝

            batch = new StateBatch(seq, (MatchPhaseLite)phase, evidence, players, sanity, props);
            return true;
        }

        /// <summary>编码声纹事件（V9 §13.4：瞬时 RPC，不走状态同步）。</summary>
        public static void WriteStimulus(Writer w, string sourceId, float intensity01, float x, float z, float radiusM)
        {
            w.Begin(Kind.Stimulus, 0);
            w.U16(Hash16(sourceId));
            w.U8(Quantize01(intensity01));
            w.U16(QuantizePos(x));
            w.U16(QuantizePos(z));
            w.U8((byte)Math.Max(0, Math.Min(255, (int)Math.Round(radiusM))));
            w.U8(0);   // reserved：将来加字段不改协议版本
        }

        /// <summary>
        /// 一批状态**将要占多少字节**（发送前先算，不靠"发完再看"）。
        ///
        /// 为什么必须有这个函数：V9 §13.4 的预算是硬约束，但**超了不会报错**，
        /// 只会表现为流量与延迟问题。所以容量必须能在发送前判断，从而能主动丢低优先级项
        /// （优先级：阶段④ &gt; 门/道具③ &gt; 位姿①）。
        /// </summary>
        public static int StateBatchSize(int playerCount, int sanityCount, int propCount)
        {
            int pc = Math.Max(0, Math.Min(playerCount, MaxPlayerSlots));
            return HeaderSize
                + 1                       // phase
                + 1                       // evidenceCount
                + 1                       // playerCount
                + pc * PlayerRecordSize
                + 1                       // sanityCount
                + Math.Max(0, sanityCount) * SanityRecordSize
                + 1                       // propCount
                + Math.Max(0, propCount) * PropRecordSize;
        }

        /// <summary>
        /// 在给定预算下**最多能带多少条道具/门变更**（位姿与理智都按满编算）。
        /// 这是"超出预算时该丢多少"的直接答案，也是断言里那条容量上限。
        /// </summary>
        public static int MaxPropChangesFor(int playerCount, int sanityCount, int budgetBytes = UpBatchBudgetBytes)
        {
            int fixedPart = StateBatchSize(playerCount, sanityCount, 0);
            int room = budgetBytes - fixedPart;
            return room <= 0 ? 0 : room / PropRecordSize;
        }

        /// <summary>
        /// 解析声纹事件（瞬时 RPC，V9 §13.4）。
        /// 与状态批同样的纪律：**空包/短包/版本不符一律返回 false，不抛异常**（UDP 收到空数据报是正常情况）。
        /// </summary>
        public static bool TryReadStimulus(byte[] data, out StimulusLite stimulus)
        {
            stimulus = default;
            if (data == null || data.Length < StimulusSize) return false;
            var r = new Reader(data);
            if (!r.U8(out var ver) || ver != ProtocolVersion) return false;
            if (!r.U8(out var kind) || kind != (byte)Kind.Stimulus) return false;
            if (!r.U16(out var _seq)) return false;      // 瞬时 RPC 的 seq 未用，但仍在头里
            if (!r.U8(out var _flags)) return false;
            if (!r.U16(out var srcHash)) return false;
            if (!r.U8(out var intensity01)) return false;
            if (!r.U16(out var x)) return false;
            if (!r.U16(out var z)) return false;
            if (!r.U8(out var radius8)) return false;
            if (!r.U8(out var _reserved)) return false;
            if (r.Remaining != 0) return false;          // 定长协议：多余字节=版本不匹配
            stimulus = new StimulusLite(srcHash, Dequantize01(intensity01), DequantizePos(x), DequantizePos(z), radius8);
            return true;
        }

        /// <summary>解析后的声纹事件（源用 16 位哈希表示，避免在热路径上分配字符串）。</summary>
        public readonly struct StimulusLite
        {
            public readonly ushort SourceHash;
            public readonly float Intensity01;
            public readonly float X, Z;
            public readonly int RadiusM;
            public StimulusLite(ushort sourceHash, float intensity01, float x, float z, int radiusM)
            { SourceHash = sourceHash; Intensity01 = intensity01; X = x; Z = z; RadiusM = radiusM; }
        }

        /// <summary>
        /// 解析后的一批状态（对应 V9 §13.4 同步对象 ①③④ 与证据计数）。
        /// 全部为定长数组，由 <see cref="TryReadStateBatch"/> 严格校验后产出。
        /// </summary>
        public readonly struct StateBatch
        {
            public readonly ushort Seq;
            public readonly MatchPhaseLite Phase;
            public readonly int EvidenceCount;
            public readonly PlayerRecord[] Players;
            public readonly (byte slot, byte val)[] Sanity;
            public readonly (ushort hash, byte flags)[] Props;
            public StateBatch(ushort seq, MatchPhaseLite phase, int evidence, PlayerRecord[] players,
                (byte, byte)[] sanity, (ushort, byte)[] props)
            { Seq = seq; Phase = phase; EvidenceCount = evidence; Players = players; Sanity = sanity; Props = props; }
        }

        /// <summary>
        /// 线格式自带的阶段枚举（**刻意不复用 Core.Contracts.MatchPhase**）：
        /// 线格式是跨版本契约，不能因为玩法层加了个阶段就让老客户端解析错位。
        /// </summary>
        public enum MatchPhaseLite : byte
        {
            Lobby = 0, Loading = 1, Playing = 2, Extraction = 3, Ended = 4,
        }
    }
}
