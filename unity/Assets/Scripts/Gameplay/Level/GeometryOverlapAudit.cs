using System;
using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 几何**重叠审计**（纯 C#，不引 UnityEngine）：把"建模重叠/穿模"从"看着像"变成**可数的清单**。
    ///
    /// ## 为什么必须有它（用户原话："存在建模重叠的地方"）
    /// 本项目在这个问题上栽过：墙盒正中摆在房间边界线上 → 相邻两房各建一份**完全同位**的墙
    /// （独立复核量化：59 对重合、重合面积 55.05 m²、198 片同法向共面面片）→ z-fighting 主源。
    /// 当时的修法是"生成阶段去重"（`LevelBuilder._wallKeys`），但**没有任何自动判据**证明它真的清零，
    /// 也没有人知道还剩哪些类别的重叠（部分重叠？体积相交？道具穿墙？）。
    /// 本类把四类重叠一次算清，输出可直接进日志/CSV 的清单 —— 结论要能被复核，而不是"我觉得修好了"。
    ///
    /// ## 四类判据（都只看 XZ 平面 + 高度，与几何模型一致）
    ///   · `ExactDuplicate`：同位置同尺寸（量化 1mm）→ **重复几何**（z-fighting 直因，必须为 0）
    ///   · `CoplanarOverlap`：XZ 有交叠且**高度区间也交叠**但尺寸/位置不同 → 部分共面（面片互压）
    ///   · `VolumeIntersect`：XZ 交叠 + 高度交叠 + 两者都不是薄片 → **体积穿模**
    ///   · `PropInWall`：道具占地盒与墙/门框相交 → 家具穿墙（用户最容易看到的一类）
    ///
    /// ## 诚实边界
    /// 这是**轴对齐盒**层面的判据：套件里的斜置部件、圆柱、以及网格内部的三角面重叠它看不到。
    /// 要把那部分也覆盖，得在 Unity 侧对渲染网格做三角级检测（本机无引擎，做不了）——
    /// 所以本类的结论只对"盒体装配"负责，这一点写在 <see cref="Describe"/> 里，不夸大。
    /// </summary>
    public static class GeometryOverlapAudit
    {
        /// <summary>重叠类别。</summary>
        public enum Kind
        {
            /// <summary>同位置同尺寸（量化 1mm）—— 重复几何，z-fighting 直因。</summary>
            ExactDuplicate,
            /// <summary>XZ 交叠 + 高度交叠，但两者尺寸/位置不同 —— 部分共面。</summary>
            CoplanarOverlap,
            /// <summary>XZ 交叠 + 高度交叠 + 两个都是"实体块"（三向都有厚度）—— 体积穿模。</summary>
            VolumeIntersect,
            /// <summary>道具占地盒与墙/门框相交 —— 家具穿墙。</summary>
            PropInWall,
        }

        /// <summary>一条重叠记录（人可读 + 可机器复核）。</summary>
        public struct Finding
        {
            public Kind Kind;
            public string A;            // 甲件标识（如 `wall ward_01 north#0`）
            public string B;            // 乙件标识
            public float OverlapAreaM2; // XZ 交叠面积（m²）
            public float OverlapYM;     // 高度交叠量（m）
            public override string ToString()
                => $"{Kind} · {A} ↔ {B} · 面积 {OverlapAreaM2:0.000} m² · 高度重叠 {OverlapYM:0.000} m";
        }

        /// <summary>审计结果（分类计数 + 明细 + 诚实边界说明）。</summary>
        public sealed class Report
        {
            public readonly List<Finding> Findings = new List<Finding>();
            public int WallCount, DoorCount, PropCount;

            public int Count(Kind k)
            {
                int n = 0;
                foreach (var f in Findings) if (f.Kind == k) n++;
                return n;
            }

            /// <summary>需要修的条数：完全重复 + 体积穿模 + 道具穿墙（部分共面单独看，未必都要修）。</summary>
            public int MustFixCount => Count(Kind.ExactDuplicate) + Count(Kind.VolumeIntersect) + Count(Kind.PropInWall);

            public string Describe()
            {
                return $"几何重叠审计：墙 {WallCount} · 门 {DoorCount} · 道具 {PropCount} · "
                     + $"完全重复 {Count(Kind.ExactDuplicate)} · 部分共面 {Count(Kind.CoplanarOverlap)} · "
                     + $"体积穿模 {Count(Kind.VolumeIntersect)} · 道具穿墙 {Count(Kind.PropInWall)} · "
                     + $"需修 {MustFixCount}（判据只覆盖轴对齐盒：斜置件/网格内部三角面不在本判据内）";
            }
        }

        /// <summary>跑一遍审计。`wallThickness` 用于给墙盒补出 XZ 厚度（Plan 里的墙只有中心线与长度）。</summary>
        public static Report Run(LevelAssembly.Plan plan, float wallThickness = 0.22f)
        {
            var rep = new Report();
            if (plan == null) return rep;
            rep.WallCount = plan.Walls.Count; rep.DoorCount = plan.Doors.Count; rep.PropCount = plan.Props.Count;

            // ① 墙 × 墙（含同一面墙被两个房间各建一次的情形）
            for (int i = 0; i < plan.Walls.Count; i++)
                for (int j = i + 1; j < plan.Walls.Count; j++)
                    Classify(rep, WallBox(plan.Walls[i], wallThickness), WallBox(plan.Walls[j], wallThickness));

            // ② 墙 × 门框（门框嵌在墙里是**设计如此**，所以只报"完全重复/体积穿模"级别；
            //    共面重叠在这里必然发生，不报 —— 否则每扇门都会报一条噪声）
            for (int i = 0; i < plan.Walls.Count; i++)
                for (int j = 0; j < plan.Doors.Count; j++)
                    Classify(rep, WallBox(plan.Walls[i], wallThickness), DoorBox(plan.Doors[j]), reportCoplanar: false);

            // ③ 道具 × 墙 / 道具 × 门框 —— 家具穿墙（用户最容易看到的一类）
            for (int i = 0; i < plan.Props.Count; i++)
            {
                var pb = PropBox(plan.Props[i]);
                for (int j = 0; j < plan.Walls.Count; j++)
                    if (Overlaps(pb, WallBox(plan.Walls[j], wallThickness), out float area, out float oy) && area > 0.01f)
                        rep.Findings.Add(new Finding
                        {
                            Kind = Kind.PropInWall, A = PropName(plan.Props[i]), B = WallName(plan.Walls[j]),
                            OverlapAreaM2 = area, OverlapYM = oy,
                        });
                for (int j = 0; j < plan.Doors.Count; j++)
                    if (Overlaps(pb, DoorBox(plan.Doors[j]), out float area2, out float oy2) && area2 > 0.01f)
                        rep.Findings.Add(new Finding
                        {
                            Kind = Kind.PropInWall, A = PropName(plan.Props[i]), B = DoorName(plan.Doors[j]),
                            OverlapAreaM2 = area2, OverlapYM = oy2,
                        });
            }
            return rep;
        }

        // ───────────────────────── 盒子与判据 ─────────────────────────

        /// <summary>审计用的轴对齐盒（XZ 平面 + 高度区间）。</summary>
        public struct Box
        {
            public string Name;
            public float X0, Z0, X1, Z1, Y0, Y1;
            public bool Solid;      // true = 三向都有厚度（用于区分"薄片共面"与"实体穿模"）
        }

        static Box WallBox(LevelAssembly.WallPart w, float thickness)
        {
            bool alongX = w.SizeX >= w.SizeZ;      // 墙沿哪条轴延伸
            float hx = alongX ? w.SizeX * 0.5f : thickness * 0.5f;
            float hz = alongX ? thickness * 0.5f : w.SizeZ * 0.5f;
            return new Box
            {
                Name = $"wall {w.RoomId} {w.Wall}",
                X0 = w.CenterX - hx, X1 = w.CenterX + hx,
                Z0 = w.CenterZ - hz, Z1 = w.CenterZ + hz,
                Y0 = 0f, Y1 = w.Height,
                Solid = true,
            };
        }

        static Box DoorBox(LevelAssembly.DoorPart d)
        {
            bool alongX = d.SizeX >= d.SizeZ;
            float hx = alongX ? d.SizeX * 0.5f : 0.04f;
            float hz = alongX ? 0.04f : d.SizeZ * 0.5f;
            return new Box
            {
                Name = $"door {d.RoomId}/{d.DoorId}",
                X0 = d.CenterX - hx, X1 = d.CenterX + hx,
                Z0 = d.CenterZ - hz, Z1 = d.CenterZ + hz,
                Y0 = 0f, Y1 = d.Height,
                Solid = false,     // 门框是薄板：与墙共面是设计
            };
        }

        static Box PropBox(LevelAssembly.PropPart p)
        {
            return new Box
            {
                Name = $"prop {p.RoomId}/{p.Kit}",
                X0 = p.CenterX - p.SizeX * 0.5f, X1 = p.CenterX + p.SizeX * 0.5f,
                Z0 = p.CenterZ - p.SizeZ * 0.5f, Z1 = p.CenterZ + p.SizeZ * 0.5f,
                Y0 = p.BaseY, Y1 = p.BaseY + 1.0f,
                Solid = true,
            };
        }

        static string WallName(LevelAssembly.WallPart w) => $"wall {w.RoomId} {w.Wall}";
        static string DoorName(LevelAssembly.DoorPart d) => $"door {d.RoomId}/{d.DoorId}";
        static string PropName(LevelAssembly.PropPart p) => $"prop {p.RoomId}/{p.Kit}";

        /// <summary>两个盒是否相交，并给出 XZ 交叠面积与高度交叠量。</summary>
        static bool Overlaps(in Box a, in Box b, out float areaM2, out float overlapY)
        {
            float ox = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0);
            float oz = Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0);
            float oy = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
            areaM2 = (ox > 0f && oz > 0f) ? ox * oz : 0f;
            overlapY = oy > 0f ? oy : 0f;
            return ox > 0f && oz > 0f && oy > 0f;
        }

        /// <summary>分类并归档一条重叠。</summary>
        static void Classify(Report rep, in Box a, in Box b, bool reportCoplanar = true)
        {
            if (!Overlaps(a, b, out float area, out float oy)) return;
            float ax = a.X1 - a.X0, az = a.Z1 - a.Z0;
            float bx = b.X1 - b.X0, bz = b.Z1 - b.Z0;
            bool same = Near(ax, bx) && Near(az, bz) && Near(a.X0, b.X0) && Near(a.Z0, b.Z0);
            Kind kind;
            if (same) kind = Kind.ExactDuplicate;
            else if (a.Solid && b.Solid && area > 0.02f && oy > 0.05f && Math.Min(ax, az) > 0.05f && Math.Min(bx, bz) > 0.05f)
                kind = Kind.VolumeIntersect;
            else if (!reportCoplanar) return;      // 设计上允许的共面（门框嵌墙）不报
            else kind = Kind.CoplanarOverlap;
            rep.Findings.Add(new Finding { Kind = kind, A = a.Name, B = b.Name, OverlapAreaM2 = area, OverlapYM = oy });
        }

        /// <summary>量化到 1mm 判等（浮点相等不可靠，与 LevelBuilder._wallKeys 同口径）。</summary>
        static bool Near(float x, float y) => Math.Abs(x - y) < 0.001f;
    }
}
