using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// **分翼封锁**（官方 Sunny Meadows 机制）。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 官方依据（`docs/reference-official/04-场景介绍初始界面与地图.md` §7.2）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// Sunny Meadows 精神病院（**它替代了原来的疯人院 Asylum**）：
    /// &gt; 规模庞大，分为多个**独立分翼**——限制病房 (Restricted Ward)、礼拜堂 (Chapel)、庭院 (Courtyard) 等
    /// &gt; **猎杀时所在分翼封锁**，极难躲藏；房间高度相似，**极易迷路**
    ///
    /// ⇒ "分翼"承载的是一条**玩法机制**，不是一个标签：
    ///   猎杀发生时，玩家所在的那一片会被封住，跑不出去 —— 这是大型图的**核心压力来源**。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么放在 Gameplay（纯逻辑）层而不是 Runtime
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 本类**只算"该封哪些门"**（返回门键列表），**不碰任何 Unity 对象** ⇒
    /// 它跑在本机 `native/csharp-verify` 的真编译真断言里（门禁能验），
    /// 而真正去关门由 Runtime 层拿着这份清单调 `LevelBuilder.SetDoorOpen`。
    /// 两边分开的好处：**判定逻辑可测**，而 Unity 那半只做"照单执行"。
    ///
    /// ⚠ 与 `LightZone` 是**两个正交维度**：`Wing` 管玩法连通性，`LightZone` 管观感。
    /// </summary>
    public sealed class WingSystem
    {
        readonly LevelData _level;

        /// <summary>门键（`roomId/doorId`，与 <see cref="LevelGeometry.DoorKey"/> 同口径）。</summary>
        public readonly List<string> SealedDoorKeys = new List<string>();

        /// <summary>当前封锁的翼（null = 未封锁）。</summary>
        public string SealedWing { get; private set; }

        /// <summary>玩家当前所在翼（随位置更新）。</summary>
        public string PlayerWing { get; private set; }

        /// <summary>所有出现过的翼（诊断/地图屏可用）。</summary>
        public readonly List<string> AllWings = new List<string>();

        public WingSystem(LevelData level)
        {
            _level = level;
            if (level?.Rooms == null) return;
            foreach (var r in level.Rooms)
            {
                if (string.IsNullOrEmpty(r.Wing)) continue;
                if (!AllWings.Contains(r.Wing)) AllWings.Add(r.Wing);
            }
        }

        /// <summary>锁着吗。</summary>
        public bool IsSealed => !string.IsNullOrEmpty(SealedWing);

        /// <summary>
        /// 更新"玩家在哪一翼"。
        ///
        /// 判定口径：**先找包含该点的房间**（不含边界，避免相邻房同时命中）；
        /// 都不包含时退化为"离哪个房间中心最近" —— 玩家站在门洞/走廊交界时不该判成"无翼"，
        /// 那会让封锁逻辑在关键时刻失效（而那正是最需要它生效的时刻）。
        /// </summary>
        public void UpdatePlayerWing(float x, float z)
        {
            if (_level?.Rooms == null || _level.Rooms.Count == 0) { PlayerWing = null; return; }
            Room best = null;
            foreach (var r in _level.Rooms)
            {
                if (r.ContainsPoint(x, z)) { best = r; break; }
            }
            if (best == null)
            {
                float bestD = float.MaxValue;
                foreach (var r in _level.Rooms)
                {
                    float d = r.DistSqToCenter(x, z);
                    if (d < bestD) { bestD = d; best = r; }
                }
            }
            PlayerWing = best?.Wing;
        }

        /// <summary>
        /// 封锁指定翼：把该翼**所有房间的所有门**登记为"要关上"。
        ///
        /// 为什么"该翼所有房间的门"就是边界：翼内部的房间本来也彼此连通，
        /// 一起关上等于**整片冻住**（官方就是"所在分翼封锁、极难躲藏"），
        /// 而不是"只堵住通往别处的门" —— 后者要判定门的另一侧属于哪一翼，
        /// 对不规则布局容易漏判，漏判的后果是玩家从缝里跑掉（机制失效）。
        ///
        /// 返回本次新登记的门数。
        /// </summary>
        public int SealWing(string wing)
        {
            SealedDoorKeys.Clear();
            SealedWing = null;
            if (string.IsNullOrEmpty(wing) || _level?.Rooms == null) return 0;
            foreach (var r in _level.Rooms)
            {
                if (r.Wing != wing || r.Doors == null) continue;
                foreach (var d in r.Doors)
                {
                    if (string.IsNullOrEmpty(d.Id)) continue;
                    string key = LevelGeometry.DoorKey(r.Id, d.Id);
                    if (!SealedDoorKeys.Contains(key)) SealedDoorKeys.Add(key);
                }
            }
            if (SealedDoorKeys.Count == 0) return 0;
            SealedWing = wing;
            return SealedDoorKeys.Count;
        }

        /// <summary>解除封锁（猎杀结束）。返回被解除的门键数。</summary>
        public int Unseal()
        {
            int n = SealedDoorKeys.Count;
            SealedDoorKeys.Clear();
            SealedWing = null;
            return n;
        }

        /// <summary>一行诊断（HUD/日志/取证可读）。</summary>
        public string Describe()
            => $"玩家翼={PlayerWing ?? "-"} · 已封={SealedWing ?? "-"} · 封锁门 {SealedDoorKeys.Count} 扇"
             + $" · 全图翼 {AllWings.Count} 个[{string.Join(",", AllWings)}]";
    }
}
