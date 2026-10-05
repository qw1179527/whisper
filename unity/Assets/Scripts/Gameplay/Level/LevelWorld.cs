using System;
using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// **多层世界**（纯 C#，不引 UnityEngine）：把"每层一张格网 + 竖井作为跨层边"组装起来。
    ///
    /// ## 为什么必须有它（三层地图最大的坑）
    /// `LevelGeometry` 的格网是 **2D 的、不记楼层**。把三层的房间喂进去编译，
    /// 会得到**一张混在一起的图**：一层的墙会挡住二层的路，二层的房间会把一层的墙"挖开"，
    /// `Passable(x,z)` 在层与层之间**串层**。所以：
    ///   · **每层一个 `LevelGeometry` 实例**（`LevelGeometry.CompileFloor`），层内判据各自独立；
    ///   · **跨层只能走竖井**（`LevelData.Shafts`）—— 竖井是"同一块 (x,z) 在若干层都成立"的声明。
    ///
    /// ## 判据（都能在本机断言，不需要 Unity）
    ///   · `Reachable(from, to)`：楼层图上的可达性。边的判据**不只是"竖井覆盖这两层"**，
    ///     还要求竖井的 (x,z) 在**两端都真的可走** —— 否则就是"画了个竖井，玩家过不去"
    ///     （这正是三层地图最容易漏的一类断连）。
    ///   · 层间隔离：同一 (x,z) 在不同层可以有不同答案，互不影响。
    ///
    /// ## 诚实边界
    /// 本类只做**平面格网 + 竖井连通**：楼梯的斜坡/台阶高度、电梯门的开合时序都不在模型内
    /// （那些属于角色移动与门系统）。它回答的是"这三层在拓扑上是否连通、会不会串层"。
    /// </summary>
    public sealed class LevelWorld
    {
        /// <summary>本关的最大楼层号 + 1（无房间的层也占位，便于索引稳定）。</summary>
        public int FloorCount { get; private set; }

        /// <summary>关卡数据（竖井、房间、门都在这里）。</summary>
        public LevelData Level { get; private set; }

        readonly List<LevelGeometry> _byFloor = new List<LevelGeometry>();
        readonly List<int> _walkableByFloor = new List<int>();

        /// <summary>编译整个关卡：逐层编译格网 + 统计每层可走格数。</summary>
        public static LevelWorld Build(LevelData level, float cellSize = 0.5f)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            var w = new LevelWorld { Level = level };
            int maxFloor = 0;
            foreach (var r in level.Rooms) if (r.Floor > maxFloor) maxFloor = r.Floor;
            foreach (var s in level.Shafts) if (s.ToFloor > maxFloor) maxFloor = s.ToFloor;
            w.FloorCount = maxFloor + 1;
            for (int f = 0; f < w.FloorCount; f++)
            {
                var geo = LevelGeometry.CompileFloor(level, f, cellSize);
                w._byFloor.Add(geo);
                int n = 0;
                // ⚠ `PassableCell` 收的是**绝对格坐标**（格网有自己的原点 MinGX/MinGZ ——
                // 上层楼层坐标偏移大时原点不是 0），按 0..Width 循环会数出 0 格。
                for (int gz = geo.MinGZ; gz < geo.MinGZ + geo.Height; gz++)
                    for (int gx = geo.MinGX; gx < geo.MinGX + geo.Width; gx++)
                        if (geo.PassableCell(gx, gz)) n++;
                w._walkableByFloor.Add(n);
            }
            return w;
        }

        /// <summary>取某层格网（越界抛错 —— 静默返回 0 层会让"三层变一层"这种错很难查）。</summary>
        public LevelGeometry Floor(int floor)
        {
            if (floor < 0 || floor >= FloorCount) throw new ArgumentOutOfRangeException(nameof(floor), $"楼层 {floor} 不在 [0,{FloorCount - 1}]");
            return _byFloor[floor];
        }

        /// <summary>某层可走格数（验收用：三层总数必须显著大于单层 557）。</summary>
        public int WalkableOn(int floor) => (floor >= 0 && floor < FloorCount) ? _walkableByFloor[floor] : 0;

        /// <summary>全部楼层可走格数之和。</summary>
        public int TotalWalkable()
        {
            int n = 0;
            foreach (var v in _walkableByFloor) n += v;
            return n;
        }

        /// <summary>层内可走判定（**只看该层**，不串层）。</summary>
        public bool Passable(float x, float z, int floor)
            => floor >= 0 && floor < FloorCount && _byFloor[floor].Passable(x, z);

        /// <summary>某层上覆盖 (x,z) 的竖井（可能多个：楼梯 + 电梯并排）。</summary>
        public List<Shaft> ShaftsAt(float x, float z, int floor)
        {
            var list = new List<Shaft>();
            foreach (var s in Level.Shafts)
                if (s.Covers(floor) && s.Contains(x, z)) list.Add(s);
            return list;
        }

        /// <summary>
        /// 竖井在该层是否**真的能站人**：竖井矩形的四角 + 中心都试一遍，
        /// 只要有一个点可走就算通（楼梯井里常有扶手/踏步占地）。
        /// </summary>
        public bool ShaftUsableOn(Shaft s, int floor)
        {
            if (!s.Covers(floor)) return false;
            var geo = _byFloor[floor];
            float cx = s.CenterX, cz = s.CenterZ;
            if (geo.Passable(cx, cz)) return true;
            float[][] pts =
            {
                new[] { s.MinX + 0.2f, s.MinZ + 0.2f }, new[] { s.MaxX - 0.2f, s.MinZ + 0.2f },
                new[] { s.MinX + 0.2f, s.MaxZ - 0.2f }, new[] { s.MaxX - 0.2f, s.MaxZ - 0.2f },
            };
            foreach (var p in pts) if (geo.Passable(p[0], p[1])) return true;
            return false;
        }

        /// <summary>
        /// 楼层连通性（BFS）：`from` 能否走到 `to`。边 = 某竖井同时覆盖两端**且两端都能站人**。
        /// 这就是"从入口可达必须跨层也成立"的判据本体。
        /// </summary>
        public bool Reachable(int from, int to)
        {
            if (from < 0 || from >= FloorCount || to < 0 || to >= FloorCount) return false;
            if (from == to) return true;
            var seen = new bool[FloorCount];
            var q = new Queue<int>();
            seen[from] = true; q.Enqueue(from);
            while (q.Count > 0)
            {
                int f = q.Dequeue();
                foreach (var s in Level.Shafts)
                {
                    if (!ShaftUsableOn(s, f)) continue;
                    for (int g = s.FromFloor; g <= s.ToFloor; g++)
                    {
                        if (g < 0 || g >= FloorCount || seen[g]) continue;
                        if (!ShaftUsableOn(s, g)) continue;   // 竖井另一端站不住 = 这条边不成立
                        seen[g] = true;
                        if (g == to) return true;
                        q.Enqueue(g);
                    }
                }
            }
            return false;
        }

        /// <summary>一层一句的诊断（真机 BOOT 行与断言都读它）。</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"多层世界：{FloorCount} 层 · 竖井 {Level.Shafts.Count} · 可走合计 {TotalWalkable()} 格 [");
            for (int f = 0; f < FloorCount; f++)
            {
                if (f > 0) sb.Append(" · ");
                int rooms = 0;
                foreach (var r in Level.Rooms) if (r.Floor == f) rooms++;
                sb.Append($"F{f}: 房 {rooms} · 可走 {_walkableByFloor[f]}");
            }
            sb.Append(']');
            if (Level.Shafts.Count > 0)
            {
                sb.Append(" · 跨层可达 F0→");
                for (int f = 1; f < FloorCount; f++) sb.Append(Reachable(0, f) ? $"F{f}✓" : $"F{f}✗");
            }
            return sb.ToString();
        }
    }
}
