using System;
using Whisper.Core;

namespace Whisper.Gameplay.Level
{
    /// <summary>不透明度无关的线性 RGB（0..1），纯 C# 便于本机断言。</summary>
    public readonly struct Rgb
    {
        public readonly float R, G, B;
        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
        /// <summary>感知亮度（Rec.601），用于"风险越高越暗"这类可测判据。</summary>
        public float Luma => 0.299f * R + 0.587f * G + 0.114f * B;
        public bool InUnitRange => R >= 0f && R <= 1f && G >= 0f && G <= 1f && B >= 0f && B <= 1f;
        /// <summary>与另一色的最大分量差（判"门框能否与墙区分"）。</summary>
        public float MaxDelta(in Rgb o) =>
            Math.Max(Math.Abs(R - o.R), Math.Max(Math.Abs(G - o.G), Math.Abs(B - o.B)));
        public override string ToString() => $"({R:0.000},{G:0.000},{B:0.000}) luma={Luma:0.000}";
    }

    /// <summary>
    /// 关卡几何配色（V9 §11 色板 → 具体表面颜色）—— **纯逻辑**，不引用 UnityEngine。
    ///
    /// 为什么必须抽出来（真机实测事故 2026-10-03）：
    ///   首版把配色乘算散落在 LevelBuilder 里，门框写成 `LightZoneColor * 1.3f`。
    ///   真机截屏实测：门框算出 **255,255,243**（bone 216×1.3 = 280.8 被截顶），
    ///   门框变成一片惨白、色相被削平，整屏平均亮度 213/255 —— 恐怖游戏看起来像曝光过度。
    ///   这类"数值超界 + 观感偏移"在 Unity 侧只看得到结果、看不到算式，
    ///   所以把算式搬到纯逻辑层：现在"任何表面颜色都不得超出 [0,1]"是可断言的。
    ///
    /// 另一条可断言的性质：**风险越高越暗**（safe &gt; pressure &gt; high-risk）。
    /// 这是 V9 §11 动态光分区的语义，不只是审美——玩家应当能靠明暗读出危险等级。
    ///
    /// 乘数是"unlit 校正"：本工程用自研 Unlit 着色器（黑屏事故后为可靠性刻意如此），
    /// 没有光照模型，所以表面直接按最终屏显颜色给值，需要比 albedo 惯例更暗一些。
    /// 真美术资产/光照接入后这套乘数应被替换，届时本类的断言仍需成立。
    /// </summary>
    public static class LevelPalette
    {
        /// <summary>
        /// 墙 / 地板 / 天花板 / 道具 的校正乘数。
        ///
        /// 【真机实测调暗 · 2026-10-03】首版取 0.72/0.45/0.28/0.55，装机后用户反馈"好亮、氛围不如灰盒"。
        /// 实测数据：安全区墙亮度 207/255、整屏平均 213/255 —— 确实像曝光过度。
        /// 对照灰盒（WebView 版）的背景是 **#0b0b0c（近黑）**，靠小面积亮部制造压迫感；
        /// 而 Unity 版把整面墙按基色满铺，等于把"环境光"拉满。
        /// 于是按灰盒的暗基调重定为 0.45/0.32/0.20/0.40，**风险越高越暗**的梯度成立。
        ///
        /// 【2026-10-04 光照接入后重校准 · 用户反馈"建模颜色有问题"】上面那组是**为 Unlit 定的死色**：
        /// 它把"屏显 = 基色 × 常数"直接当作最终颜色。但渲染已从 `return i.color` 换成
        /// **自研 Lit**（`albedo × (环境项 0.22 + 主方向光 Lambert)`），同一个乘数会再被光照压一次 ——
        /// 实测画面比旧版**暗 35~45%**（corridor_main 32.7 vs 75.3、entrance_safe 47.8 vs 67.2），
        /// 于是"暗调"变成了"糊成一片分不清材质"，这才是颜色的真正问题。
        ///
        /// 现按光照模型反解（构建智能体 A 的实测标定表，模型 `屏显 ≈ 255 × albedo × (0.22 + NdotL·0.85)`）：
        ///   · 墙：典型可见面是 −Z（NdotL≈0.56 → k≈0.61）→ 0.45/0.61 ≈ **0.73**（安全区墙回到 96，旧版 93）
        ///   · 地板：恒为 +Y（k≈0.75）→ 0.32/0.75 ≈ **0.43**（迎光面 66，与旧版一致）
        ///   · 天花板：恒为 −Y（NdotL=0，只吃环境项 k=0.22）→ 0.20/0.22 ≈ **0.78**（屏显 37，旧版 41）
        ///   · 道具：以侧面为主（k≈0.61）→ 0.40/0.61 ≈ **0.66**
        /// 关键点：**这些不是"调亮"，而是把"被光照二次衰减"的那部分补回来**，让屏显回到设计值；
        /// 对比度（迎光 96 / 侧面 71 / 背光 35）反而比 Unlit 版更真实。
        /// </summary>
        public const float WallScale = 0.73f;
        public const float FloorScale = 0.43f;
        public const float CeilingScale = 0.78f;
        public const float PropScale = 0.66f;
        /// <summary>
        /// 门框向墨色混合的比例（**不用乘法**：乘法会在浅色上截顶，混合不会）。
        ///
        /// 【2026-10-04 必须归零】此前 0.42 是为了在 Unlit 下把门框压暗成"深色框"。
        /// 接入光照后语义反了：墙的 albedo 被 `WallScale` 放大，而门框没有对应的放大，
        /// 继续混墨会让门框**比墙还暗**（实测 83 vs 96）——与"门框是亮框"的原意相反。
        /// 归零后：门框 131 vs 墙 96（旧版 131 vs 93），层次恢复。
        /// </summary>
        public const float DoorFrameInkMix = 0.0f;

        /// <summary>光分区基色（V9 §11 色板）。</summary>
        public static Rgb ZoneBase(string zone)
        {
            switch (zone)
            {
                case "safe": return Parse(DesignTokens.ColorBone);        // 安全区：暖白
                case "high-risk": return Parse(DesignTokens.ColorRust);   // 高风险：锈褐
                default: return Parse(DesignTokens.ColorMold);            // 压力区：霉绿灰
            }
        }

        static readonly Rgb Ink = Parse(DesignTokens.ColorInk);

        public static Rgb Wall(string zone) => Scale(ZoneBase(zone), WallScale);
        public static Rgb Floor(string zone) => Scale(ZoneBase(zone), FloorScale);
        public static Rgb Ceiling(string zone) => Scale(ZoneBase(zone), CeilingScale);
        public static Rgb Prop(string zone) => Scale(ZoneBase(zone), PropScale);

        /// <summary>
        /// 门框：向墨色**混合**而不是乘一个 &gt;1 的系数。
        /// 乘法在浅色上必然截顶（bone 216×1.3 = 280.8 → 255），混合则天然落在区间内。
        /// </summary>
        public static Rgb DoorFrame(string zone) => Mix(ZoneBase(zone), Ink, DoorFrameInkMix);

        static Rgb Scale(in Rgb c, float k) => new Rgb(Clamp01(c.R * k), Clamp01(c.G * k), Clamp01(c.B * k));

        /// <summary>线性混合（t=0 → a，t=1 → b）。</summary>
        public static Rgb Mix(in Rgb a, in Rgb b, float t)
        {
            float k = Clamp01(t);
            return new Rgb(
                Clamp01(a.R + (b.R - a.R) * k),
                Clamp01(a.G + (b.G - a.G) * k),
                Clamp01(a.B + (b.B - a.B) * k));
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>把 #RRGGBB 解析为 0..1 的 Rgb（非法输入返回灰，绝不抛——配色不该让 Boot 崩）。</summary>
        public static Rgb Parse(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new Rgb(0.5f, 0.5f, 0.5f);
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return new Rgb(0.5f, 0.5f, 0.5f);
            try
            {
                return new Rgb(
                    Convert.ToByte(hex.Substring(0, 2), 16) / 255f,
                    Convert.ToByte(hex.Substring(2, 2), 16) / 255f,
                    Convert.ToByte(hex.Substring(4, 2), 16) / 255f);
            }
            catch (Exception) { return new Rgb(0.5f, 0.5f, 0.5f); }
        }
    }
}
