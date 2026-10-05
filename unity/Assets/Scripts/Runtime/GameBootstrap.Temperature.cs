using UnityEngine;
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Core.Contracts;
using Whisper.Gameplay.Level;
using Whisper.Gameplay.Progression;   // Progression / Shop / TaskSystem（等级-商店-任务，恐鬼症对齐）
using Whisper.Gameplay.Objectives;    // ObjectiveSystem（**局内任务**：合同日志里的可选目标，与每日任务是两套）
using Whisper.Net;
using Whisper.Net.Direct;   // LanSession / LanAddress / RoomCode（零信令直连层）
using Whisper.Audio;
using Whisper.Backend;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Environment;

namespace Whisper.Runtime
{
    /// <summary>
    /// 温度与天气（`GameBootstrap` 的 partial 拆分）。
    ///
    /// 【为什么拆】温度/天气/鬼房判定加进来后主文件涨到 697 行，触发规模纪律（gate-code C5，>600 判红）。
    /// 这四件事是一个内聚的整体：**天气决定基线室温 → 鬼房降温 → 鬼离开回暖 → HUD 读数**，
    /// 与"装配场景/角色/怪物/HUD"无关，边界干净。
    ///
    /// ## 权威规格
    /// `docs/spec/supplement-2026-10-04-late-full.md` 第二节（用户原件）：
    /// 普通鬼把鬼房降到区间 [-2,5]°C；带「刺骨寒温」证据的降到 [-8,-5]°C；
    /// 鬼在鬼房内降温 ×1.6；鬼周围 3m 内更陡；玩家 < −1°C 口吐寒气；8 种天气决定基线（−1~23°C）。
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>鬼房（一个**设定**：开局选定 + 途中按难度概率更换），不是"鬼现在在哪"。</summary>
        Whisper.Gameplay.Monsters.GhostRoom _ghostRoom;

        /// <summary>
        /// 本局的鬼是否带「刺骨寒温」证据。取**任意一只**怪即可 —— 本作同一局只有一种鬼种
        /// （三只怪是同一鬼种的不同个体/角色），所以只要有一只带证据，鬼房就走 [-8,-5] 区间。
        /// </summary>
        bool ResolveGhostFreezingEvidence()
        {
            var monsters = GameConfig.Get("monsters") as System.Collections.Generic.Dictionary<string, object>;
            if (monsters == null) return false;
            foreach (var kv in monsters)
            {
                if (kv.Key.StartsWith("_", System.StringComparison.Ordinal)) continue;
                var m = kv.Value as System.Collections.Generic.Dictionary<string, object>;
                if (m == null) continue;
                if (m.TryGetValue("hasFreezingEvidence", out var v) && v is bool b && b) return true;
            }
            return false;
        }

        /// <summary>数出几何层里**开着**的物理洞口数（`Doors` 是公开列表，逐个数即可）。
        /// 为什么不加个 `OpenDoorCount` 属性：那要改 `build-render` 的文件，而这里读公开列表就够了。</summary>
        static int CountOpenDoors(Whisper.Gameplay.Level.LevelGeometry geo)
        {
            if (geo == null) return 0;
            int n = 0;
            for (int i = 0; i < geo.Doors.Count; i++) if (geo.Doors[i].Open) n++;
            return n;
        }

        /// 每帧推进鬼房与鬼的位置（**两件独立的事**）。
        ///
        /// 【2026-10-04 语义修正 · 用户澄清】我此前实现成"鬼现在在哪，哪就是鬼房"，是错的：
        /// > 「不是鬼所处的房间就是鬼房，**鬼房是一个设定**，证据灵球只在鬼房能被看见，
        /// >   游戏途中鬼可能会换鬼房，概率看选择的难度」
        ///
        /// 现在：
        /// · **鬼房**由 `GhostRoom` 系统选定（开局一次 + 途中按难度概率更换），是**设定**；
        /// · **鬼的位置**只驱动"周围 3m 更陡的降温"，与鬼房无关；
        /// · 鬼房**恒冷**（鬼离开也不回暖），灵球只在鬼房可见。
        /// </summary>
        void UpdateGhostRoomForTemperature()
        {
            if (Level == null || _monsters == null || _ghostRoom == null) return;

            // ① 鬼房：到点按难度概率决定是否更换（换房是"设定"变了，不是"鬼走到哪"）
            _ghostRoom.Tick(Time.deltaTime);
            _temperature.SetGhostRoom(_ghostRoom.RoomId, _ghostHasFreezingEvidence);
            if (_ghostRoom.JustRelocated)
                Debug.Log($"[Whisper] 鬼换了鬼房 → {_ghostRoom.RoomId}（第 {_ghostRoom.RelocationCount} 次 · 难度 {_ghostRoom.Difficulty}）");

            // ② 鬼的位置：只用于"周围 3m 更陡"与"回到鬼房时降温加速"
            var views = _monsters.LastViews;
            if (views == null || views.Length == 0) return;

            float px = _player != null ? _player.X : 0f, pz = _player != null ? _player.Z : 0f;
            float bestD = float.MaxValue; int bestI = -1;
            for (int i = 0; i < views.Length; i++)
            {
                float dx = views[i].X - px, dz = views[i].Z - pz;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; bestI = i; }
            }
            if (bestI < 0) return;
            var v = views[bestI];
            string ghostNow = RoomIdAt(v.X, v.Z);
            _temperature.SetGhostCurrentRoom(ghostNow);
            _temperature.SetGhostPosition(v.X, v.Z, _ghostRoom.IsGhostRoom(ghostNow));
        }

        /// <summary>
        /// 某个世界坐标落在哪个房间（**边界 1cm 容差**）。
        /// 容差的理由见 `UpdateGhostRoomForTemperature` 的注释：鬼常贴着房间边界走，
        /// 严格比较会让归属在"有/无"之间翻转。
        /// </summary>
        string RoomIdAt(float x, float z)
        {
            if (Level == null) return null;
            const float eps = 0.01f;
            foreach (var room in Level.Rooms)
                if (x >= room.MinX - eps && x <= room.MaxX + eps && z >= room.MinZ - eps && z <= room.MaxZ + eps)
                    return room.Id;
            return null;
        }

        /// <summary>
        /// HUD 的温度行。**显示最冷的房间**而不是玩家所在房间：
        /// 玩家还没找到鬼房时，这一行就是唯一的线索（"某处已经 8°C 了"），
        /// 而显示玩家所在房间在大多数时候都只是 20°C，没有信息量。
        /// </summary>
        string DescribeTemperature()
        {
            if (_temperature == null || Level == null || Level.Rooms.Count == 0) { _temperatureLine = "温度：—"; return _temperatureLine; }

            // 【2026-10-04 修 · 用户反馈"所有地方都是鬼房且温度不变"】
            // 旧版**只显示"最冷房间"**：鬼长时间待在某处时那个房间就是固定的 →
            // 玩家在任何地方看到的都是同一行，观感就是"到处都冷、温度不变"。
            // 现在第一义是**玩家所在处的温度**（玩家真正会读的数），最冷房间作为线索跟在后面，
            // 两者都带房间名，便于交叉对照 —— 这样"走到冷的地方"立刻能在 HUD 上看到变化。
            string here = RoomIdAt(_player != null ? _player.X : 0f, _player != null ? _player.Z : 0f);
            float hereC = _temperature.TemperatureOf(here);
            string breath = _temperature.PlayerBreathing ? " · 口吐寒气" : "";

            string coldestId = null; float coldest = float.MaxValue;
            foreach (var room in Level.Rooms)
            {
                float t = _temperature.TemperatureOf(room.Id);
                if (t < coldest) { coldest = t; coldestId = room.Id; }
            }
            string ghostRoom = _temperature.GhostRoomId;
            string freezing = _temperature.IsFreezingConfirmed(coldestId) ? " · 刺骨寒温已确认" : "";
            // 写入缓存字段再返回：HUD 每 0.5s 拼一次字符串，缓存下来便于真机诊断时按字段取值
            _temperatureLine = $"温度：此处 {here ?? "走廊"} {hereC:0.0}C{breath}"
                 + $" · 最冷 {coldestId} {coldest:0.0}C{freezing}"
                 + (string.IsNullOrEmpty(ghostRoom) ? " · 鬼不在房内" : $" · 鬼房 {ghostRoom}");
            return _temperatureLine;
        }

        /// <summary>
        /// 开局选定鬼房并返回可核验的一行总结。
        ///
        /// 【为什么单独一个方法】用户澄清「鬼房是一个设定」之后，这套初始化有了三步：
        /// 造 `GhostRoom`（含确定性随机源）→ 注册候选房间 → 由局种子选定开局鬼房。
        /// 它是**一个设定**的建立过程，与"天气"（另一条派生流）刻意分开 —— 详见方法内的注释。
        /// </summary>
        string SetupGhostRoom()
        {
            // 鬼房：一个**设定**（用户澄清「不是鬼所处的房间就是鬼房」）。
            // 开局选定一次，之后只按难度概率更换；灵球只在鬼房可见；鬼房恒冷。
            // 选定值由局种子派生 → 联机各端同一天气、同一鬼房，不需要额外同步字段。
            var ghostRoomCfg = new Whisper.Gameplay.Config.GameConfigReader();
            // 随机源 = 由局种子派生的确定性序列（联机各端必须换到同一间鬼房）
            var ghostRoomRoll = Whisper.Gameplay.Environment.Weather.MakeSeededRoll(MatchSeed, 0x6D05u);
            _ghostRoom = new Whisper.Gameplay.Monsters.GhostRoom(ghostRoomCfg, null, ghostRoomRoll);
            foreach (var room in Level.Rooms) _ghostRoom.AddCandidate(room.Id);
            // 开局选鬼房：用局种子的**另一条**派生流（与天气/刮风互不相关，
            // 否则会出现「晴天必然配某间鬼房」这种可被玩家反推的相关性）
            _ghostRoom.SelectInitial(Whisper.Gameplay.Environment.Weather.Pick01ForMatch(MatchSeed, 0xA17Eu));
            return _ghostRoom.Describe() + "（灵球只在鬼房可见）";
        }
    }
}
