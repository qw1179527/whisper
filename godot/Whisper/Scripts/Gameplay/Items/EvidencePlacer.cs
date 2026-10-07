using System.Collections.Generic;
using Whisper.Gameplay.Level;

namespace Whisper.Gameplay.Items
{
    /// <summary>
    /// 证据点落位：在房间内挑一个**代理真正站得到**的位置（V9 §7 证据机制 + §19.2 关卡即数据）。
    ///
    /// 为什么不能直接用房间中心（本项目最严重的假绿事故）：
    /// 证据点原本定义在 `r.CenterX/CenterZ`，而家具落位只要"不越界、不压门洞"即可，
    /// 于是一张 0.9×2.0 的病床正好压在房间中心 —— 代理半径 0.34m 时最近可站位距证据点
    /// **1.35m > 拾取半径 0.9m**，5 个证据只能拿到 1 个，**关卡不可通关**。
    /// 而当时的门禁全绿：M8 查的是"房间图连通"（与能不能走过去无关），
    /// C# 端到端用例又是**瞬移**过去的。
    ///
    /// 做法：在房间里按 0.25m 栅格扫描候选点，取"可站（含代理半径、不含家具）且可达
    /// （与该房间门的可站格连通）"的候选中**离房间中心最近**的一个。
    /// 纯 C#、无引擎依赖 → 可本机断言（见 gate-test 的"证据点可达"断言）。
    /// </summary>
    public static class EvidencePlacer
    {
        public const float AgentRadiusM = 0.34f;
        /// <summary>候选点扫描步长（米）。0.25 能在 3×4m 房间里给出足够选择。</summary>
        public const float StepM = 0.25f;

        /// <summary>
        /// 某位置是否"代理站得下"：**直接按几何判净空**，不看格粒度。
        ///
        /// 为什么不复用 `geo.Passable` 的四角判定：可走格是 0.5m 的方块，而代理半径 0.34m
        /// 需要约 0.68m 净宽；病房内缩后只剩 1.8m，用"四角格"判定会把大部分合法站位误杀
        /// （实测 ward_01 通过、ward_02~05 全部报"无可达落位"）。
        /// 这里改为直接判「到房间墙内表面 ≥ 0.34m」且「不与任何道具盒相交（按 0.34 膨胀）」。
        /// </summary>
        public static bool IsStandable(LevelGeometry geo, LevelData level, float x, float z)
        {
            if (geo == null || level == null) return false;
            if (!geo.Passable(x, z)) return false;                       // 不在墙里
            var room = FindRoom(level, x, z);
            if (room == null) return false;
            const float r = AgentRadiusM;
            if (x - r < room.MinX + LevelGeometry.WallThickness - 1e-3f) return false;
            if (x + r > room.MaxX - LevelGeometry.WallThickness + 1e-3f) return false;
            if (z - r < room.MinZ + LevelGeometry.WallThickness - 1e-3f) return false;
            if (z + r > room.MaxZ - LevelGeometry.WallThickness + 1e-3f) return false;
            // 与道具盒的净空：代理圆近似为方形膨胀
            var plan = LevelAssembly.Build(level);
            foreach (var pp in plan.Props)
            {
                if (pp.RoomId != room.Id) continue;
                if (x + r > pp.CenterX - pp.SizeX / 2 && x - r < pp.CenterX + pp.SizeX / 2
                    && z + r > pp.CenterZ - pp.SizeZ / 2 && z - r < pp.CenterZ + pp.SizeZ / 2) return false;
            }
            return true;
        }

        /// <summary>
        /// 在房间内挑证据点：优先靠近房间中心，但必须站得住、且与房间门所在的可站区域连通。
        /// 找不到时返回 false —— 调用方应按"关卡缺陷"处理并让它可见，而不是静默退回房间中心。
        /// </summary>
        public static bool TryPlace(LevelGeometry geo, LevelData level, Room room, out float x, out float z)
        {
            x = room.CenterX; z = room.CenterZ;
            if (geo == null || level == null || room == null) return false;

            // 落位判定用**更细的格**（0.25m）：0.5m 格 + 0.26m 墙 + 0.6m 内缩，会把 1.8m 净宽的
            // 病房量化到只剩 1~2 格，导致大量合法站位被判"站不下"（实测 ward_02~05 全灭）。
            // 运行时碰撞仍用 0.5m 格（性能与既有语义不变），只有落位这类"找位置"的判定用细格。
            if (!ReferenceEquals(_fineCacheLevel, level) || _fineCache == null)
            {
                _fineCacheLevel = level;
                _fineCache = LevelGeometry.Compile(level, 0.25f);
            }
            geo = _fineCache;

            // 种子：从"该房间的门内一侧"出发做连通性洪水填充
            // 种子：房间内**全域**找可站格，取"离某扇门最近"的那个。
            // 我上一版只在门正前方 1.5m 采样 —— 那里往往正对墙/家具（门口本来就不适合站人），
            // 于是种子为空、洪泛结果为空，四个病房全部报"无可达落位"。
            var stands = new List<(float x, float z)>();
            for (float cz = room.MinZ + AgentRadiusM; cz <= room.MaxZ - AgentRadiusM; cz += StepM)
                for (float cx = room.MinX + AgentRadiusM; cx <= room.MaxX - AgentRadiusM; cx += StepM)
                    if (IsStandable(geo, level, cx, cz)) stands.Add((cx, cz));
            if (stands.Count == 0) return false;

            (float x, float z) seed = stands[0];
            float bestSeed = float.MaxValue;
            foreach (var d in room.Doors)
            {
                d.ToWorld(room, out float dx, out float dz);
                foreach (var st in stands)
                {
                    float dd = (st.x - dx) * (st.x - dx) + (st.z - dz) * (st.z - dz);
                    if (dd < bestSeed) { bestSeed = dd; seed = st; }
                }
            }
            var reachable = FloodReachable(geo, level, room, seed.x, seed.z);
            float bestD = float.MaxValue;
            bool found = false;
            for (float cz = room.MinZ + AgentRadiusM; cz <= room.MaxZ - AgentRadiusM; cz += StepM)
                for (float cx = room.MinX + AgentRadiusM; cx <= room.MaxX - AgentRadiusM; cx += StepM)
                {
                    if (!IsStandable(geo, level, cx, cz)) continue;
                    if (!reachable.Contains(Key(cx, cz))) continue;
                    float d = (cx - room.CenterX) * (cx - room.CenterX) + (cz - room.CenterZ) * (cz - room.CenterZ);
                    if (d < bestD) { bestD = d; x = cx; z = cz; found = true; }
                }
            return found;
        }

        static LevelGeometry _fineCache;
        static LevelData _fineCacheLevel;

        static Room FindRoom(LevelData level, float x, float z)
        {
            foreach (var r in level.Rooms)
                if (x >= r.MinX - 1e-3f && x <= r.MaxX + 1e-3f && z >= r.MinZ - 1e-3f && z <= r.MaxZ + 1e-3f) return r;
            return null;
        }

        static IEnumerable<(float, float)> Offsets(string wall, float k)
        {
            switch (wall)
            {
                case "north": yield return (0, -k); break;
                case "south": yield return (0, k); break;
                case "west": yield return (k, 0); break;
                default: yield return (-k, 0); break;
            }
        }

        static int Key(float x, float z) => (int)System.Math.Round(x / StepM) * 100000 + (int)System.Math.Round(z / StepM);

        /// <summary>在房间范围内做 0.25m 栅格洪水填充（只走可站格）。</summary>
        static HashSet<int> FloodReachable(LevelGeometry geo, LevelData level, Room room, float sx, float sz)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<(float, float)>();
            stack.Push((sx, sz)); seen.Add(Key(sx, sz));
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                for (int i = 0; i < 4; i++)
                {
                    float nx = cur.Item1, nz = cur.Item2;
                    if (i == 0) nx += StepM; else if (i == 1) nx -= StepM;
                    else if (i == 2) nz += StepM; else nz -= StepM;
                    if (nx < room.MinX || nx > room.MaxX || nz < room.MinZ || nz > room.MaxZ) continue;
                    if (!IsStandable(geo, level, nx, nz)) continue;
                    int k = Key(nx, nz);
                    if (!seen.Add(k)) continue;
                    stack.Push((nx, nz));
                }
            }
            return seen;
        }
    }
}
