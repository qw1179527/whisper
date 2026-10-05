using System;
using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 墙段**合并**（纯 C#，不引 UnityEngine）：把"同一平面上互相重叠的多个墙盒"并成一整段。
    ///
    /// ## 为什么需要它（用户原话："存在建模重叠的地方"）
    /// 每个房间各自沿四面墙建墙盒，而墙盒**正中摆在房间边界线上** → 相邻房间在共享边上各建一份。
    /// 实测本关（`tools/` 之外的独立探针 `_evidence/build-a/overlap-probe`）：
    ///   · **完全同位** 10 对（同位置同尺寸，量化 1mm）→ `LevelBuilder` 的生成期去重能吃掉这些
    ///   · **部分重叠（体积相交）28 对** —— 去重吃不掉：例如 `corridor_link` 的北墙（长 4m）
    ///     整段落在 `corridor_ward` 的南墙（长 15m）**同一个 0.22m 厚的板**里 → 两段墙叠着建，
    ///     顶面/底面共面、端头接缝处还会露出内部面。这就是"看得见的重复建模"。
    ///   · 拐角处正交墙相交 58 处（0.012 m² 级）—— 属于设计允许的搭接。
    ///
    /// 本类把①"同板同高"的墙段按坐标分组、②把**重叠或相接**的跨度并成一段，**每段只建一次**。
    /// 于是"两段墙叠在同一块板里"从"靠人记得"变成"生成期不可能发生"。
    ///
    /// ## 判据口径
    /// 只在**同一块板**内合并：轴（横/纵）、板中心坐标（量化 1mm）、壁厚、墙高 全部相同的才算同板。
    /// 不同高度的墙（门楣等）不会被并进来 —— 它们本来就该分开建。
    /// </summary>
    public static class WallRunMerger
    {
        /// <summary>一段轴对齐墙（世界坐标；`Along` = 沿 X 还是沿 Z 延伸）。</summary>
        public struct Run
        {
            public bool AlongX;
            /// <summary>沿墙方向的两个端点（X 或 Z）。</summary>
            public float From, To;
            /// <summary>板的中心坐标（X 或 Z 里"不沿"的那一轴）。</summary>
            public float Plane;
            /// <summary>离地高度与墙高。</summary>
            public float BaseY, Height;
            /// <summary>壁厚。</summary>
            public float Thickness;
            public override string ToString()
                => $"{(AlongX ? "X向" : "Z向")} 板@{Plane:0.000} [{From:0.000},{To:0.000}] y[{BaseY:0.000},{BaseY + Height:0.000}] 厚{Thickness:0.000}";
        }

        /// <summary>合并前/后的统计（给日志与取证用）。</summary>
        public struct Result
        {
            public int InCount, OutCount, MergedAway;
            public override string ToString() => $"墙段合并：{InCount} → {OutCount}（并掉 {MergedAway} 段重复/碎片）";
        }

        /// <summary>
        /// 合并墙段。返回合并后的清单（顺序稳定：先 X 向再 Z 向，各自按板坐标、起点排序）。
        ///
        /// ## 算法：每块板做一次"矩形并集分解"
        /// 同一块板上，墙段既可能**沿墙方向**重叠（走廊 4m 的墙落在病房 15m 的墙里），
        /// 也可能**只在高度上**重叠（走廊墙高 3.0 与病房墙高 3.5 同一块板）。
        /// 只按"起点排序 + 延长"处理后者会漏（高度不同分组就不同）→ 仍然叠着建。
        /// 所以这里做二维分解：
        ///   ① 收集该板上所有端点作为**切分点**，切成若干互不重叠的区间；
        ///   ② 每个区间取"覆盖它"的全部墙段，把它们的**高度区间求并**；
        ///   ③ 输出"区间 × 高度带"的矩形 —— 这些矩形两两不重叠（体积不相交）；
        ///   ④ 最后把同一板上高度带相同且**相接**的相邻矩形再并回去，避免碎片化。
        /// </summary>
        public static List<Run> Merge(List<Run> input, out Result result, float tolerance = 0.001f)
        {
            var outp = new List<Run>();
            result = new Result { InCount = input?.Count ?? 0 };
            if (input == null || input.Count == 0) return outp;

            // ① 分组：轴 + 板坐标 + 壁厚（**不含高度** —— 高度在组内统一分解）
            var groups = new Dictionary<string, List<Run>>(StringComparer.Ordinal);
            foreach (var r in input)
            {
                if (r.To - r.From <= tolerance || r.Height <= tolerance) continue;   // 退化段直接丢
                string key = $"{(r.AlongX ? "X" : "Z")}|{Q(r.Plane)}|{Q(r.Thickness)}";
                if (!groups.TryGetValue(key, out var list)) { list = new List<Run>(); groups[key] = list; }
                list.Add(r);
            }

            foreach (var kv in groups)
            {
                var list = kv.Value;
                // ② 切分点：所有端点去重（量化到 1mm）后排序
                var cuts = new List<float>();
                foreach (var r in list) { AddCut(cuts, r.From); AddCut(cuts, r.To); }
                cuts.Sort();
                var bands = new List<Run>();
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    float a = cuts[i], b = cuts[i + 1];
                    if (b - a <= tolerance) continue;
                    // 覆盖该区间的墙段 → 高度并集
                    var spans = new List<(float lo, float hi)>();
                    foreach (var r in list)
                        if (r.From <= a + tolerance && r.To >= b - tolerance)
                            spans.Add((r.BaseY, r.BaseY + r.Height));
                    if (spans.Count == 0) continue;
                    spans.Sort((p, q) => p.lo.CompareTo(q.lo));
                    float lo = spans[0].lo, hi = spans[0].hi;
                    for (int k = 1; k < spans.Count; k++)
                    {
                        if (spans[k].lo <= hi + tolerance) { if (spans[k].hi > hi) hi = spans[k].hi; }
                        else
                        {
                            bands.Add(MakeRun(list[0], a, b, lo, hi));
                            lo = spans[k].lo; hi = spans[k].hi;
                        }
                    }
                    bands.Add(MakeRun(list[0], a, b, lo, hi));
                }
                // ③ 相邻同高度带且相接 → 并回一段（减少碎片）
                bands.Sort((p, q) =>
                {
                    int c = p.BaseY.CompareTo(q.BaseY);
                    if (c != 0) return c;
                    c = p.Height.CompareTo(q.Height);
                    if (c != 0) return c;
                    return p.From.CompareTo(q.From);
                });
                for (int i = 0; i < bands.Count; i++)
                {
                    var cur = bands[i];
                    while (i + 1 < bands.Count
                           && Math.Abs(bands[i + 1].BaseY - cur.BaseY) < tolerance
                           && Math.Abs(bands[i + 1].Height - cur.Height) < tolerance
                           && bands[i + 1].From <= cur.To + tolerance)
                    {
                        if (bands[i + 1].To > cur.To) cur.To = bands[i + 1].To;
                        i++;
                    }
                    outp.Add(cur);
                }
            }

            outp.Sort((a, b) =>
            {
                if (a.AlongX != b.AlongX) return a.AlongX ? -1 : 1;
                int c = a.Plane.CompareTo(b.Plane);
                if (c != 0) return c;
                c = a.BaseY.CompareTo(b.BaseY);
                if (c != 0) return c;
                return a.From.CompareTo(b.From);
            });
            result.OutCount = outp.Count;
            result.MergedAway = result.InCount - result.OutCount;
            return outp;
        }

        static Run MakeRun(in Run proto, float from, float to, float lo, float hi) => new Run
        {
            AlongX = proto.AlongX,
            From = from,
            To = to,
            Plane = proto.Plane,
            BaseY = lo,
            Height = hi - lo,
            Thickness = proto.Thickness,
        };

        static void AddCut(List<float> cuts, float v)
        {
            foreach (var c in cuts) if (Math.Abs(c - v) < 0.0005f) return;
            cuts.Add(v);
        }

        static long Q(float v) => (long)Math.Round(v * 1000.0);   // 量化到 1mm
    }
}
