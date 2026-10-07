using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 关卡装配计划：把 LevelData **算成**"要放哪些盒子、放在哪、多大"，但**不碰引擎**。
    ///
    /// 为什么要有这一层（V9 §19「代码优先 / No-Editor Discipline」的直接要求）：
    /// 装配计算（墙按门洞切段、门板落位、道具按 footprint 摆正）原本与 `new GameObject` 混在
    /// `LevelBuilder` 里，于是**本机根本测不了** —— 没有 Unity 时这些计算一行都跑不到，
    /// 独立复核的 F2b 正是"几何层没接进产品且无法验证"。
    /// 现在计算全部搬到这里（纯 C#，本机 .NET 8 真跑真验），`LevelBuilder` 退化为
    /// "读计划 → 建对象"的薄封装。
    ///
    /// 坐标系约定（与 LevelData/LevelGeometry 严格一致）：
    ///   · 世界 XZ 平面，房间 pos = **最小角点**，y 只用楼层高度
    ///   · 房间局部坐标以房间**中心**为原点（LevelBuilder 建对象时用）
    /// </summary>
    public static class LevelAssembly
    {
        /// <summary>墙厚（米）。与 LevelGeometry.WallThickness 保持一致，避免两套口径。</summary>
        public const float WallThickness = LevelGeometry.WallThickness;

        /// <summary>一个墙段（尺寸为全长，不是半长）。</summary>
        public struct WallPart
        {
            public string RoomId;
            public string Wall;          // north / south / east / west
            public int Index;            // 同面墙上的段序号
            public float CenterX, CenterZ;   // 世界坐标
            public float SizeX, SizeZ;       // 世界尺寸（XZ）
            public float Height;
        }

        /// <summary>一块门板（含门框预留，门洞本身不生成墙段）。</summary>
        public struct DoorPart
        {
            public string RoomId;
            public string DoorId;
            public string Wall;
            public float CenterX, CenterZ;
            public float SizeX, SizeZ;
            public float Height;
            public bool Locked;
        }

        /// <summary>一个道具（按 kit footprint 与 rot 摆正）。</summary>
        public struct PropPart
        {
            public string RoomId;
            public string Kit;
            public float CenterX, CenterZ;
            public float SizeX, SizeZ;   // 旋转后的占地尺寸
            public float RotDeg;
            public float BaseY;          // 落地高度（楼层地面）
        }

        /// <summary>
        /// 装配计划：墙段 / 门板 / 道具三张表 + 楼层高。`LevelBuilder` 只读它来建 GameObject，
        /// 因此"可见几何"与"碰撞几何"共用同一份落位计算，不会出现两套口径。
        /// </summary>
        public sealed class Plan
        {
            public readonly List<WallPart> Walls = new List<WallPart>();
            public readonly List<DoorPart> Doors = new List<DoorPart>();
            public readonly List<PropPart> Props = new List<PropPart>();
            public readonly List<string> Notes = new List<string>();
            public float FloorHeightM = 3.5f;   // 楼层高（房间 floor 序号 × 该值）
        }

        /// <summary>道具占地尺寸的真源（与 asset-manifest.json 的 footprint 一致）。</summary>
        public static readonly Dictionary<string, (float w, float d)> KitFootprint =
            new Dictionary<string, (float, float)>
            {
                ["bed_b"] = (0.9f, 2.0f),
                ["cabinet_a"] = (0.8f, 0.5f),
            };

        /// <summary>按 rot 旋转后的占地尺寸（90°/270° 时宽深互换）。</summary>
        public static (float w, float d) RotatedFootprint(string kit, float rotDeg)
        {
            var (w, d) = KitFootprint.TryGetValue(kit ?? "", out var f) ? f : (0.8f, 0.8f);
            float r = ((rotDeg % 180f) + 180f) % 180f;
            return (r >= 45f && r < 135f) ? (d, w) : (w, d);
        }

        /// <summary>
        /// 生成装配计划。**纯函数**：同样的 LevelData 必得同样的计划（确定性，可回归）。
        /// </summary>
        public static Plan Build(LevelData level, float doorThicknessM = 0.08f, float doorHeightM = 2.1f)
        {
            var plan = new Plan();
            if (level == null) return plan;

            foreach (var r in level.Rooms)
            {
                float baseY = r.Floor * plan.FloorHeightM;
                AddRoomWalls(plan, r);
                AddRoomDoors(plan, r, doorThicknessM, doorHeightM);
                AddRoomProps(plan, r, baseY);
            }
            return plan;
        }

        static void AddRoomWalls(Plan plan, Room r)
        {
            // 每面墙按门洞切成段：段 = 墙长 − 门洞区间
            foreach (var wall in new[] { "north", "south", "west", "east" })
            {
                float wallLen = (wall == "north" || wall == "south") ? r.SizeX : r.SizeZ;
                var gaps = new List<(float a, float b)>();
                foreach (var d in r.Doors)
                {
                    if (d.Wall != wall) continue;
                    d.SpanOnWall(r, out float a, out float b);
                    gaps.Add((a, b));
                }
                gaps.Sort((x, y) => x.a.CompareTo(y.a));

                int idx = 0;
                float cursor = 0f;
                foreach (var (a, b) in gaps)
                {
                    if (a > cursor && a - cursor > 0.001f) Emit(plan, r, wall, idx++, cursor, a, wallLen);
                    cursor = System.Math.Max(cursor, b);
                }
                if (wallLen - cursor > 0.001f) Emit(plan, r, wall, idx, cursor, wallLen, wallLen);
            }
        }

        static void Emit(Plan plan, Room r, string wall, int idx, float from, float to, float wallLen)
        {
            float len = to - from;
            if (len <= 0.001f) return;
            float mid = (from + to) / 2f;                  // 沿墙位置（从墙起点算）
            bool horizontal = wall == "north" || wall == "south";
            float half = (horizontal ? r.SizeZ : r.SizeX) / 2f;

            // 世界中心：房间最小角点 + 沿墙偏移 + 贴墙偏移
            // 墙段中心**贴房间边界线**（不是"边界向内半墙厚"）：
            // 房间矩形在 DSL 与碰撞网格里都是"含边界线"的，若这里再向内缩半墙厚，
            // 装配出的墙与碰撞出的墙就相差 0.13m —— 两套口径不一致会让"看得见的墙"
            // 与"撞得到的墙"错开（本项目反复踩过这类双口径问题）。
            float cx, cz;
            if (horizontal)
            {
                cx = r.MinX + mid;
                cz = (wall == "north") ? r.MaxZ : r.MinZ;
            }
            else
            {
                cz = r.MinZ + mid;
                cx = (wall == "east") ? r.MaxX : r.MinX;
            }
            _ = half;
            plan.Walls.Add(new WallPart
            {
                RoomId = r.Id,
                Wall = wall,
                Index = idx,
                CenterX = cx,
                CenterZ = cz,
                SizeX = horizontal ? len : WallThickness,
                SizeZ = horizontal ? WallThickness : len,
                Height = r.SizeY,
            });
        }

        static void AddRoomDoors(Plan plan, Room r, float thick, float height)
        {
            foreach (var d in r.Doors)
            {
                d.ToWorld(r, out float dx, out float dz);
                d.SpanOnWall(r, out float a, out float b);
                float width = b - a;
                bool horizontal = d.Wall == "north" || d.Wall == "south";
                plan.Doors.Add(new DoorPart
                {
                    RoomId = r.Id,
                    DoorId = d.Id,
                    Wall = d.Wall,
                    CenterX = dx,
                    CenterZ = dz,
                    SizeX = horizontal ? width : thick,
                    SizeZ = horizontal ? thick : width,
                    Height = height,
                    Locked = d.Locked,
                });
            }
        }

        static void AddRoomProps(Plan plan, Room r, float baseY)
        {
            foreach (var p in r.Props)
            {
                var (w, d) = RotatedFootprint(p.Kit, p.Rot);
                plan.Props.Add(new PropPart
                {
                    RoomId = r.Id,
                    Kit = p.Kit,
                    CenterX = r.MinX + p.X,
                    CenterZ = r.MinZ + p.Z,
                    SizeX = w,
                    SizeZ = d,
                    RotDeg = p.Rot,
                    BaseY = baseY,
                });
            }
        }
    }
}
