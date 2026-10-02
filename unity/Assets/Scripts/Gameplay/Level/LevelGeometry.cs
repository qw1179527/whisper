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
        /// <summary>
        /// 墙碰撞盒（薄墙，厚 <see cref="WallThickness"/>）。用于子步进移动解析时判断"是否撞墙"。
        /// 原名 `DoorBlockers` 会被误读成"门的阻挡物"，但它装的其实是**墙** ——
        /// 独立复核 F12 指出该命名会误导后续接入（且当时全仓库无读取方）。现按实际语义改名。
        /// </summary>
        public readonly List<Box> WallBoxes = new List<Box>();

        /// <summary>
        /// 轴对齐碰撞盒（XZ 平面）。几何层只认 AABB —— 关卡是轴对齐的矩形拼装，
        /// 用 AABB 能让碰撞判定保持 O(盒数) 且完全确定（无浮点方向判断）。
        /// </summary>
        public struct Box
        {
            public float X0, Z0, X1, Z1;
            public Box(float x0, float z0, float x1, float z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
        }

        LevelGeometry(int minGX, int minGZ, int w, int h, float cellSize = DefaultCellSize)
        {
            CellSize = cellSize;
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

        public bool Passable(float x, float z) => PassableCell(CellOf(x), CellOf(z));

        /// <summary>
        /// 由关卡数据编译几何（对应灰盒 buildLevel + compileWalls + compileDoors）。
        ///
        /// 编译顺序（顺序有意义）：
        ///   ① 全图标记为墙 → ② 房间矩形挖空 → ③ 门洞所在格打通（**只打通被两侧空间共享的格**，
        ///   否则等于在墙上开无意义的洞）→ ④ 走廊矩形挖空 → ⑤ 按墙段生成碰撞盒。
        /// </summary>
        /// <summary>
        /// 把关卡数据编译成碰撞几何（1 米网格 + 薄墙碰撞盒 + 道具盒）。
        ///
        /// ## 三个"看起来对但根本不成立"的坑（都靠本机 probe 与独立复核才暴露）
        /// 1. **房间之间必须有墙**。网格单位 1 米、墙厚仅 0.26 米，若直接把"落在房间矩形内的格"
        ///    全部挖空，相邻房间（ward_01 x[4,7] 与 ward_02 x[7,10]）的内部格会直接相邻，
        ///    墙落在格缝上 —— 网格里没有格子承载它，于是 44 面墙全部消失、11 个房间敞通、
        ///    门形同虚设（实测：可走 161 格、洪水填充 161/161 单连通）。
        ///    现在：房间内部**按墙厚内缩**后再挖，边界那一列/行留作墙带。
        /// 2. **门洞必须夹在所属房间的编号范围内**。否则打通"门格 + 内侧一格"时会顺带凿穿
        ///    走廊与病房之间的整段墙。
        /// 3. **外墙不能漏**。内缩同时天然保住了关卡外圈。
        /// </summary>
        public static LevelGeometry Compile(LevelData level, float cellSize = DefaultCellSize)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var r in level.Rooms)
            {
                minX = Math.Min(minX, r.MinX); minZ = Math.Min(minZ, r.MinZ);
                maxX = Math.Max(maxX, r.MaxX); maxZ = Math.Max(maxZ, r.MaxZ);
            }
            int minGX = CellOf(minX, cellSize) - 1, minGZ = CellOf(minZ, cellSize) - 1;
            int maxGX = CellOf(maxX, cellSize) + 2, maxGZ = CellOf(maxZ, cellSize) + 2;
            var geo = new LevelGeometry(minGX, minGZ, maxGX - minGX, maxGZ - minGZ, cellSize);

            var owners = new Dictionary<(int, int), int>();
            void Pledge(float x0, float z0, float x1, float z1)
            {
                int gx0 = CellOf(x0, cellSize), gx1 = CellOf(x1 - 1e-4f, cellSize);
                int gz0 = CellOf(z0, cellSize), gz1 = CellOf(z1 - 1e-4f, cellSize);
                for (int gz = gz0; gz <= gz1; gz++)
                    for (int gx = gx0; gx <= gx1; gx++)
                        owners[(gx, gz)] = owners.TryGetValue((gx, gz), out var n) ? n + 1 : 1;
            }

            // ① 全图先设为墙
            for (int gz = minGZ; gz < maxGZ; gz++)
                for (int gx = minGX; gx < maxGX; gx++)
                    geo.MarkBlocked(gx, gz);

            // ② 认领（仅为统计与可读性保留；实际挖空在 ③ 按内缩矩形做）
            foreach (var r in level.Rooms) Pledge(r.MinX, r.MinZ, r.MaxX, r.MaxZ);

            // ③ 房间内部：按墙厚内缩后，仍完整落在其中的格才算可走
            foreach (var r in level.Rooms)
            {
                // 内缩量只需"足以把相邻房间的边界格分开"（>0 即可把边界那一列/行让给墙），
                // 不必等于墙厚 —— 用墙厚会把窄房间（走廊 z 向仅 1m）整条吃掉，实测直接导致不连通。
                // 房间内部：**按 INSET 内缩**后覆盖的格。
                //
                // ⚠ INSET 必须 > CellSize/2（这里 0.6 > 0.25）。为什么：
                // 墙在本模型里是"**缝格**"——两个相邻房间各自内缩 0.6m 后，边界附近会空出
                // 1~2 列/行没有任何房间认领的格，那些格就是墙；门洞再在其中凿通。
                // 内缩不足（我试过 0.26 与 0.1）时两房间的可走格直接相邻，跨过共享边
                // 两侧都"可走"，等于没有墙 —— 独立复核 F2「房间之间没有内墙」就是这么来的。
                // 内缩量必须**随房间尺寸收窄**：走廊只有 1m 深，固定 0.6 会把它的内部算成空集
                // （实测 corridor_ward 得到 "gz 11..10" = 无内部格，整条走廊不可达）。
                // 取 0.6 与"较小边长的 35%"中的较小者。
                float INSET = Math.Min(0.6f, Math.Min(r.MaxX - r.MinX, r.MaxZ - r.MinZ) * 0.35f);
                int gx0 = CellOf(r.MinX + INSET, cellSize), gx1 = CellOf(r.MaxX - INSET - 1e-4f, cellSize);
                int gz0 = CellOf(r.MinZ + INSET, cellSize), gz1 = CellOf(r.MaxZ - INSET - 1e-4f, cellSize);
                if (gx1 < gx0 || gz1 < gz0) continue;   // 极窄房间（理论上不应出现）跳过而非算成空集
                for (int gz = gz0; gz <= gz1; gz++)
                    for (int gx = gx0; gx <= gx1; gx++)
                        geo.Unmark(gx, gz);
            }

            // ④ 门洞：在所属房间编号范围内打通「门格 + 内侧一格」
            foreach (var r in level.Rooms)
            {
                int rgx0 = CellOf(r.MinX, cellSize), rgx1 = CellOf(r.MaxX - 1e-4f, cellSize);
                int rgz0 = CellOf(r.MinZ, cellSize), rgz1 = CellOf(r.MaxZ - 1e-4f, cellSize);
                foreach (var d in r.Doors)
                {
                    d.ToWorld(r, out float dx, out float dz);
                    int gx = CellOf(dx, cellSize), gz = CellOf(dz, cellSize);
                    bool northSouth = d.Wall == "north" || d.Wall == "south";
                    // 门洞打通：从门格沿法向**两侧各凿到"进入本房间内部"为止**（上限 6 格）。
                    //
                    // 为什么必须"凿到进入内部为止"而不是固定凿 N 格：房间内部按 INSET 内缩后，
                    // 门格与内部之间隔着缝带（有时 1 格、有时 2~3 格，取决于缝与格边界的对齐），
                    // 固定凿 2 格或 5 格都会有一部分门洞恰好差一格接不上 ——
                    // 实测 ward_02..05 与 corridor_ward 形成 28 格孤岛。
                    void Open(int ax, int az)
                    {
                        if (northSouth) { if (ax < rgx0 || ax > rgx1) return; }   // 夹沿墙坐标（防顺墙凿穿）
                        else { if (az < rgz0 || az > rgz1) return; }
                        geo.Unmark(ax, az);
                    }
                    Open(gx, gz);
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        bool reached = false;
                        for (int k = 1; k <= 6; k++)
                        {
                            int ax = northSouth ? gx : gx + dir * k;
                            int az = northSouth ? gz + dir * k : gz;
                            Open(ax, az);
                            // "进入本房间内部"= 该格已在房间的内缩矩形之内
                            float ins = Math.Min(0.6f, Math.Min(r.MaxX - r.MinX, r.MaxZ - r.MinZ) * 0.35f);
                            if (ax >= CellOf(r.MinX + ins, cellSize) && ax <= CellOf(r.MaxX - ins - 1e-4f, cellSize)
                                && az >= CellOf(r.MinZ + ins, cellSize) && az <= CellOf(r.MaxZ - ins - 1e-4f, cellSize))
                            { reached = true; break; }
                        }
                        _ = reached;
                    }
                }
            }

            geo.BuildWallBoxes(level);
            geo.BuildPropBoxes(level);
            return geo;
        }

        void CarveRect(float x0, float z0, float x1, float z1)
        {
            int gx0 = CellOf(x0), gz0 = CellOf(z0);
            int gx1 = CellOf(x1 - 1e-4f), gz1 = CellOf(z1 - 1e-4f);
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
                WallBoxes.Add(new Box(gx, gz, gx + 1, gz + 1));
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
        /// <summary>墙厚（米）。网格单位是 1 米，房间内部按此内缩，边界格留作墙带。</summary>
        public const float WallThickness = 0.26f;
        /// <summary>
        /// 网格单位（米）。**必须明显小于房间尺寸**：早期用 1 米格时，3×4 米的病房内缩后
        /// 只剩 1×3 格，门洞格与内部格总是错开一格，导致"有墙但走不进去"。
        /// 0.5 米格：墙厚 0.26 m 能完整落在格内，走廊 1 m 宽也留有 1 格可走。
        /// </summary>
        public const float DefaultCellSize = 0.5f;
        /// <summary>本实例的格尺寸（默认 0.5m）。落位类判定可用更细的格（如 0.25m）避免量化过粗。</summary>
        public readonly float CellSize = DefaultCellSize;
        int CellOf(float v) => (int)Math.Floor(v / CellSize);
        /// <summary>静态换算（供 Compile 这类静态流程使用；实例内请用 CellOf）。</summary>
        static int CellOf(float v, float cellSize) => (int)Math.Floor(v / cellSize);

        /// <summary>一次移动解析的结果：终点坐标 + 过程中是否被挡（用于停滞/绕行判定）。</summary>
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
        /// <summary>
        /// 找出离给定点**最近的可走且无道具**的格中心。
        /// 为什么需要：房间中心格常常被家具占用（本项目 ward_03 中心就放着病床），
        /// 用它当洪水填充/碰撞测试的起点会得到"看起来不连通/被挡"的假象。
        /// </summary>
        public bool TryFindFreeCell(float x, float z, out float fx, out float fz)
        {
            int cx = CellOf(x), cz = CellOf(z);
            for (int ring = 0; ring <= 12; ring++)
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;   // 只看该环
                        int gx = cx + dx, gz = cz + dz;
                        if (!PassableCell(gx, gz)) continue;
                        float px = (gx + 0.5f) * CellSize, pz = (gz + 0.5f) * CellSize;
                        // 必须连**代理半径的净空**一起查：只查格中心会给出"贴着家具边"的起点，
                        // 于是小位移一推就被判 blocked（实测碰撞解析断言因此变红）。
                        const float R = 0.34f;
                        if (!Passable(px - R, pz - R) || !Passable(px + R, pz - R)
                            || !Passable(px - R, pz + R) || !Passable(px + R, pz + R)) continue;
                        bool hitProp = false;
                        foreach (var b in PropBoxes)
                            if (px + R > b.X0 && px - R < b.X1 && pz + R > b.Z0 && pz - R < b.Z1) { hitProp = true; break; }
                        if (hitProp) continue;
                        fx = px; fz = pz; return true;
                    next:;
                    }
            fx = x; fz = z; return false;
        }

        public int PassableCount()
        {
            int n = 0;
            for (int i = 0; i < _blocked.Length; i++) if (!_blocked[i]) n++;
            return n;
        }

        /// <summary>从某点做洪水填充，返回可达格数（用于验证"门真的连通了两个空间"）。</summary>
        public int ReachableCount(float x, float z)
        {
            int sx = CellOf(x), sz = CellOf(z);
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
