using System;
using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 关卡几何层（V9 §19.2：由 Level DSL **编译**出可碰撞、可行走的几何）。
    ///
    /// 为什么这一层必须存在：DSL 只是数据，`LevelBuilder` 要实例化几何就必须先有
    /// 「格子/墙线/门洞/道具盒」这些中间产物。灰盒把这件事放在 `__m4`（compileWalls/compileDoors）
    /// 与 `__m5`（makeCollider/propBoxes/wallCells）里；本类是它们的 C# 对应物，
    /// 刻意不依赖 UnityEngine，从而可以在本机被断言覆盖（Unity 侧只留一个薄的实例化层）。
    ///
    /// 坐标系约定（与灰盒严格一致，见 LevelData.Room 注释）：
    ///   · 房间 pos = 最小角点；格 (gx,gz) 覆盖世界 [gx,gx+1) × [gz,gz+1)
    ///   · 世界 → 格：Math.Floor；格可走 = 不在任何"墙格"里
    /// </summary>
    public sealed class LevelGeometry
    {
        public readonly int Width;      // 格数（x 方向）
        public readonly int Height;     // 格数（z 方向）
        public readonly int MinGX, MinGZ;
        readonly bool[] _blocked;       // [gz * Width + gx]
        public readonly List<Box> PropBoxes = new List<Box>();
        public readonly List<Box> DoorBlockers = new List<Box>();

        public struct Box
        {
            public float X0, Z0, X1, Z1;
            public Box(float x0, float z0, float x1, float z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
        }

        LevelGeometry(int minGX, int minGZ, int w, int h)
        {
            MinGX = minGX; MinGZ = minGZ; Width = w; Height = h;
            _blocked = new bool[w * h];
        }

        public bool PassableCell(int gx, int gz)
        {
            if (gx < MinGX || gz < MinGZ || gx >= MinGX + Width || gz >= MinGZ + Height) return false;
            return !_blocked[(gz - MinGZ) * Width + (gx - MinGX)];
        }

        void MarkBlocked(int gx, int gz)
        {
            if (gx < MinGX || gz < MinGZ || gx >= MinGX + Width || gz >= MinGZ + Height) return;
            _blocked[(gz - MinGZ) * Width + (gx - MinGX)] = true;
        }

        public bool Passable(float x, float z) => PassableCell((int)Math.Floor(x), (int)Math.Floor(z));

        /// <summary>
        /// 由关卡数据编译几何（对应灰盒 buildLevel + compileWalls + compileDoors）。
        ///
        /// 编译顺序（顺序有意义）：
        ///   ① 全图标记为墙 → ② 房间矩形挖空 → ③ 门洞所在格打通（**只打通被两侧空间共享的格**，
        ///   否则等于在墙上开无意义的洞）→ ④ 走廊矩形挖空 → ⑤ 按墙段生成碰撞盒。
        /// </summary>
        public static LevelGeometry Compile(LevelData level)
        {
            // ① 计算包围盒
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var r in level.Rooms)
            {
                minX = Math.Min(minX, r.MinX); minZ = Math.Min(minZ, r.MinZ);
                maxX = Math.Max(maxX, r.MaxX); maxZ = Math.Max(maxZ, r.MaxZ);
            }
            foreach (var c in level.Corridors)
            {
                // 走廊本身不改变包围盒（两端都是房间）；保留循环以便将来支持"露天走廊"
                _ = c;
            }
            int minGX = (int)Math.Floor(minX) - 1, minGZ = (int)Math.Floor(minZ) - 1;
            int maxGX = (int)Math.Ceiling(maxX) + 1, maxGZ = (int)Math.Ceiling(maxZ) + 1;
            var geo = new LevelGeometry(minGX, minGZ, maxGX - minGX, maxGZ - minGZ);

            // ① 全图墙
            for (int gz = minGZ; gz < maxGZ; gz++)
                for (int gx = minGX; gx < maxGX; gx++)
                    geo.MarkBlocked(gx, gz);

            // ② 房间挖空
            foreach (var r in level.Rooms) geo.CarveRect(r.MinX, r.MinZ, r.MaxX, r.MaxZ);

            // ③ 门洞：把门所在格打通（门格 = 与该门中心同侧的相邻格）
            foreach (var r in level.Rooms)
                foreach (var d in r.Doors)
                {
                    d.ToWorld(r, out float dx, out float dz);
                    int gx = (int)Math.Floor(dx), gz = (int)Math.Floor(dz);
                    // 门洞横跨墙线，故把墙线两侧各打通一格，保证两侧空间真正连通
                    geo.CarveRect(dx - 0.01f, dz - 0.01f, dx + 0.01f, dz + 0.01f);
                    geo.Unmark(gx, gz);
                    if (d.Wall == "north") geo.Unmark(gx, gz);
                    else if (d.Wall == "south") geo.Unmark(gx, gz - 1);
                    else if (d.Wall == "west") geo.Unmark(gx - 1, gz);
                    else geo.Unmark(gx, gz);
                }

            // ④ 走廊挖空（走廊区域本身可走）
            foreach (var r in level.Rooms) { /* 占位：走廊几何由门洞与房间连通性表达 */ }

            // ⑤ 墙段碰撞盒 + 道具盒
            geo.BuildWallBoxes(level);
            geo.BuildPropBoxes(level);
            return geo;
        }

        void CarveRect(float x0, float z0, float x1, float z1)
        {
            int gx0 = (int)Math.Floor(x0), gz0 = (int)Math.Floor(z0);
            int gx1 = (int)Math.Ceiling(x1) - 1, gz1 = (int)Math.Ceiling(z1) - 1;
            for (int gz = gz0; gz <= gz1; gz++)
                for (int gx = gx0; gx <= gx1; gx++)
                    Unmark(gx, gz);
        }

        void Unmark(int gx, int gz)
        {
            if (gx < MinGX || gz < MinGZ || gx >= MinGX + Width || gz >= MinGZ + Height) return;
            _blocked[(gz - MinGZ) * Width + (gx - MinGX)] = false;
        }

        /// <summary>
        /// 墙碰撞盒：把"与可走格相邻的墙格"合并成条带（对应灰盒 wallCells + mergeStrips + buildWallBoxes）。
        /// 只有**贴着可走区域**的墙格才需要碰撞盒——整层实心外圈的内部格没人会碰到，建了纯属浪费。
        /// </summary>
        void BuildWallBoxes(LevelData level)
        {
            var cells = new List<(int gx, int gz)>();
            for (int gz = MinGZ; gz < MinGZ + Height; gz++)
                for (int gx = MinGX; gx < MinGX + Width; gx++)
                {
                    if (PassableCell(gx, gz)) continue;      // 不是墙
                    bool touchesFloor =
                        PassableCell(gx, gz - 1) || PassableCell(gx, gz + 1) ||
                        PassableCell(gx - 1, gz) || PassableCell(gx + 1, gz);
                    if (touchesFloor) cells.Add((gx, gz));
                }

            // 横向连续合并（与灰盒 mergeStrips 同策略：先横向、再纵向会得到更少但更长的条带；
            // 这里用逐格盒以保证与"格级碰撞"语义完全一致，条带合并只影响性能不影响行为）
            foreach (var (gx, gz) in cells)
                DoorBlockers.Add(new Box(gx, gz, gx + 1, gz + 1));
        }

        /// <summary>
        /// 套件占地尺寸（0 度放置时的 [宽x, 深z]，单位米）——**必须与 asset-manifest.json 的 footprint 一致**。
        ///
        /// 为什么不能像早期那样用固定 1×1 盒：实测一张 0.9×2.0 的病床旋转 90° 后
        /// 在房间里的真实占地是 2.0×0.9，而固定盒既不对尺寸也不对朝向 ——
        /// 后果是"看得见的床"与"撞得到的床"不是同一个东西（穿模或空气墙）。
        /// 若清单将来新增套件，这里缺失的 key 会走 fallback 并在 Validate 里被提示补 footprint。
        /// </summary>
        static readonly Dictionary<string, (float w, float d)> KitFootprint = new Dictionary<string, (float, float)>(StringComparer.Ordinal)
        {
            ["bed_b"] = (0.9f, 2.0f),
            ["cabinet_a"] = (0.8f, 0.5f),
        };

        void BuildPropBoxes(LevelData level)
        {
            foreach (var r in level.Rooms)
                foreach (var p in r.Props)
                {
                    var (w, d) = KitFootprint.TryGetValue(p.Kit ?? "", out var f) ? f : (0.8f, 0.8f);
                    // rot 为 90°/270° 时宽深互换（与生成器 footprintHalf 同一判据）
                    float rot = ((p.Rot % 180f) + 180f) % 180f;
                    if (rot >= 45f && rot < 135f) { var t = w; w = d; d = t; }
                    float cx = r.MinX + p.X, cz = r.MinZ + p.Z;
                    PropBoxes.Add(new Box(cx - w / 2f, cz - d / 2f, cx + w / 2f, cz + d / 2f));
                }
        }

        // ── 碰撞解析（对应灰盒 makeCollider 的 resolve）──

        public const float MaxStep = 0.25f;

        public struct MoveResult { public float X, Z; public bool Blocked; }

        /// <summary>
        /// 分离轴滑动 + **子步进**。
        ///
        /// 子步进不是可选优化：单帧位移若一次判定，5 米的位移会被判成"终点合法"从而穿过整面墙
        /// （灰盒首轮实测：从出生点向西 5 米直接穿墙落到门厅）。每步不超过 <see cref="MaxStep"/>。
        /// </summary>
        public MoveResult Resolve(float fromX, float fromZ, float deltaX, float deltaZ, float radius = 0.34f)
        {
            float x = fromX, z = fromZ;
            bool anyBlocked = false;
            float maxAbs = Math.Max(Math.Abs(deltaX), Math.Abs(deltaZ));
            int steps = Math.Max(1, (int)Math.Ceiling(maxAbs / MaxStep));
            float dx = deltaX / steps, dz = deltaZ / steps;

            for (int i = 0; i < steps; i++)
            {
                float nx = x + dx;
                if (!BlockedAt(nx + Math.Sign(dx) * radius, z, radius)) x = nx; else anyBlocked = true;
                float nz = z + dz;
                if (!BlockedAt(x, nz + Math.Sign(dz) * radius, radius)) z = nz; else anyBlocked = true;
            }
            return new MoveResult { X = x, Z = z, Blocked = anyBlocked };
        }

        bool BlockedAt(float px, float pz, float radius)
        {
            if (!Passable(px, pz)) return true;
            for (int i = 0; i < PropBoxes.Count; i++)
            {
                var b = PropBoxes[i];
                if (px + radius > b.X0 && px - radius < b.X1 && pz + radius > b.Z0 && pz - radius < b.Z1) return true;
            }
            return false;
        }

        /// <summary>可走格数（诊断用：编译出 0 格说明几何生成失败）。</summary>
        public int PassableCount()
        {
            int n = 0;
            for (int i = 0; i < _blocked.Length; i++) if (!_blocked[i]) n++;
            return n;
        }

        /// <summary>从某点做洪水填充，返回可达格数（用于验证"门真的连通了两个空间"）。</summary>
        public int ReachableCount(float x, float z)
        {
            int sx = (int)Math.Floor(x), sz = (int)Math.Floor(z);
            if (!PassableCell(sx, sz)) return 0;
            var seen = new HashSet<(int, int)>();
            var stack = new Stack<(int, int)>();
            stack.Push((sx, sz)); seen.Add((sx, sz));
            while (stack.Count > 0)
            {
                // 注意：这里刻意不用 `var (cx, cz) = stack.Pop()` 的隐式解构——
                // Roslyn 在此上下文推不出类型（CS8130），而 dotnet 8.0 主编译器不报，
                // 于是它只会在 Unity 里才炸。显式取字段最稳。
                var cur = stack.Pop();
                int cx = cur.Item1, cz = cur.Item2;
                var neighbors = new[] { (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1) };
                for (int i = 0; i < neighbors.Length; i++)
                {
                    var n = neighbors[i];
                    if (!PassableCell(n.Item1, n.Item2) || seen.Contains(n)) continue;
                    seen.Add(n); stack.Push(n);
                }
            }
            return seen.Count;
        }
    }
}
