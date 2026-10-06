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

        /// <summary>
        /// 【2026-10-06 重做：每个表面给【自己的色相】，不再"一个基色调明度"】
        ///
        /// ## 用户的原话与它指出的真问题
        /// 「疗养院的地图墙壁房间啥的不还是绿色、灰色等纯色吗，**谁家医院这样**」
        ///
        /// 查下来根因是**结构性的**，不是色值没调好：
        /// 此前每个分区的**所有表面**都写成 `Scale(ZoneBase(zone), k)` ——
        /// 墙 0.73 / 地 0.43 / 顶 0.78 / 道具 0.66，**同一个基色乘不同系数**。
        /// 压力区基色是 `ColorMold #5C8C6E`（中绿），于是：
        /// ```
        ///   墙 #43664F · 地 #273C2F · 顶 #487C56 · 道具 #3C5C48     ← 全是绿，只差明度
        /// ```
        /// **一个房间从头到脚同一个色相**，看起来当然就是"纯绿一片"。
        ///
        /// 而真实医院的观感是**靠不同色相拼出来的**：
        /// 【浅绿墙裙/白墙】+【灰色地砖】+【白色吊顶】+【深色金属门框】——
        /// 四者的区别在**色相**，不在明度。这正是此前缺的那一层。
        ///
        /// ## 设计约束（两条，都有理由）
        /// ① **亮度基本维持原值**，只换色相。
        ///    为什么不趁机调亮：用户此前明确反馈过"好亮、氛围不如灰盒"，
        ///    而这套亮度是按灰盒的暗基调标定过的。**换色相是这次的目标，改亮度不是。**
        /// ② **保留分区的情绪梯度**（安全区暖 / 压力区冷 / 高风险区锈），
        ///    它服务于玩法（越危险的区域越脏越暗），不能因为"要像医院"就抹掉。
        ///
        /// ## 表怎么读
        /// 每行是一个分区，列出该分区下五种表面的**独立色值**。
        /// 数字后用注释标出它"读起来是什么"——
        /// 因为这类表最容易在后续维护里被改回"统一乘系数"，注释是防止那种退化的。
        /// </summary>
        static readonly (string zone, string wall, string floor, string ceiling, string prop, string frame)[] SurfaceTable =
        {
            //          墙（医院浅绿/暖白灰泥）  地（地砖/塑胶地）  顶（吊顶）      道具          门框（深金属）
            ("safe",     "#D9D2C2", "#B0A99C", "#E4DED1", "#C6BEA9", "#33322F"),  // 暖白区：像走廊，干净
            ("pressure", "#8FA38C", "#5A5954", "#8E908A", "#6E6656", "#2E3230"),  // 压力区：医院浅绿墙 + 灰地砖
            ("high-risk","#6B5A48", "#43413C", "#5A554E", "#4E4234", "#26231F"),  // 高风险：脏褐墙 + 暗水泥地
        };

        static (string wall, string floor, string ceiling, string prop, string frame) SurfaceOf(string zone)
        {
            foreach (var r in SurfaceTable)
                if (r.zone == zone) return (r.wall, r.floor, r.ceiling, r.prop, r.frame);
            var d = SurfaceTable[1];   // 未登记的分区按"压力区"处理（此前 default 也是这个语义）
            return (d.wall, d.floor, d.ceiling, d.prop, d.frame);
        }

        /// <summary>墙：医院浅绿墙裙 / 暖白灰泥 / 脏褐灰泥 —— 按分区取。</summary>
        public static Rgb Wall(string zone) => Parse(SurfaceOf(zone).wall);

        /// <summary>地：**灰色**（地砖 / 塑胶地 / 暗水泥）—— 刻意与墙不同色相。</summary>
        public static Rgb Floor(string zone) => Parse(SurfaceOf(zone).floor);

        /// <summary>顶：**近中性**（吊顶）—— 刻意不跟墙走，否则抬头又是同一片绿。</summary>
        public static Rgb Ceiling(string zone) => Parse(SurfaceOf(zone).ceiling);

        /// <summary>道具：暖木/旧金属调 —— 与墙面拉开，否则道具"融进墙里"看不出是物件。</summary>
        public static Rgb Prop(string zone) => Parse(SurfaceOf(zone).prop);

        /// <summary>
        /// 门框：**深色金属**（独立色值，不再由基色混墨推）。
        ///
        /// 【为什么改】`DoorFrameInkMix = 0` 时期门框 = 基色原色 → 压力区下就是**亮绿**，
        /// 而医院门框是深色金属包边，是画面里少数几个"深色重音"。
        /// 把它定成深色还能在浅色墙面上勾出边框，让门"读得出来"。
        /// </summary>
        public static Rgb DoorFrame(string zone) => Parse(SurfaceOf(zone).frame);

        // ══════════════════════════════════════════════════════════════════════════════
        // 部件角色（用户 2026-10-06：「不是有个 PBR 着色器吗，可以混合用」）
        // ══════════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 按**部件节点名**判"这是什么部件"→ 返回角色名（小写常量，见下方 <c>Role*</c>）。
        ///
        /// ## 为什么返回**字符串**而不是材质族枚举
        /// 本文件属于**纯逻辑层** —— `native/csharp-verify` 把 `Gameplay/Level/*.cs` 链接进去
        /// 做真编译真跑断言（本机唯一跑得起来的那套）。而材质族枚举
        /// （`ProceduralTextures.MaterialFamily`）住在 `Gameplay/Render`，**依赖 UnityEngine**，
        /// 不在那个工程里。所以在这里引用它 = CS0234 编译失败（2026-10-06 实测踩到）。
        /// 返回角色名后：**本文件零依赖**，由 `LevelBuilder`（已经在 Unity 侧）把角色名翻成材质族。
        ///
        /// ## 为什么必须用节点名，而不是用 GLB 的材质名
        /// 实测：GLB 的 `role_trim` 一个分组就吞掉 **318 个节点**（墙裙 / 顶角线 / 门套 / 窗套…），
        /// `role_detail` 吞 128 个 —— 语义完全不同的部件被压成同一组，
        /// **只按材质名选不出"该用木地板还是金属门框"**。节点名才是部件身份的真源。
        ///
        /// ## 为什么必须换掉"按包围盒猜角色"的旧做法
        /// 旧实现只有两条判据（是否铺满房间 / 是否高瘦），**其余一律归"天花板"**——
        /// 于是墙裙、顶角线、门套、灯带、暖气片全被涂成同一个颜色。
        /// </summary>
        public static string MaterialRoleOf(string partName)
        {
            if (string.IsNullOrEmpty(partName)) return RoleUnknown;
            if (partName == "floor") return RoleFloor;
            if (partName.StartsWith("drain_", System.StringComparison.Ordinal)) return RoleDrain;
            if (partName == "ceiling" || partName.StartsWith("cornice_", System.StringComparison.Ordinal)) return RoleCeiling;
            if (partName.StartsWith("ceil_beam_", System.StringComparison.Ordinal)) return RoleBeam;
            if (partName == "light_panel" || partName.StartsWith("lamp_", System.StringComparison.Ordinal)) return RoleLight;
            if (partName.StartsWith("skirt_", System.StringComparison.Ordinal)) return RoleSkirt;
            if (partName.StartsWith("pilaster_", System.StringComparison.Ordinal)) return RoleWall;
            if (partName.StartsWith("conduit_", System.StringComparison.Ordinal)) return RolePipe;
            if (partName.StartsWith("doorjamb_", System.StringComparison.Ordinal)) return RoleDoorFrame;
            if (partName.StartsWith("window_", System.StringComparison.Ordinal)) return RoleWindow;
            if (partName.StartsWith("radiator_", System.StringComparison.Ordinal)) return RoleRadiator;
            if (partName.StartsWith("rack_", System.StringComparison.Ordinal)) return RoleRack;
            return RoleUnknown;   // 未识别：由调用方按分区给灰泥/混凝土兜底（不是黑，也不是随机）
        }

        // 角色常量（与 MaterialRoleOf 一一对应；集中在此便于门禁/测试引用，避免散落的字面量）
        public const string RoleUnknown   = "unknown";
        public const string RoleFloor     = "floor";
        public const string RoleDrain     = "drain";
        public const string RoleCeiling   = "ceiling";
        public const string RoleBeam      = "beam";
        public const string RoleLight     = "light";
        public const string RoleSkirt     = "skirt";
        public const string RoleWall      = "wall";
        public const string RolePipe      = "pipe";
        public const string RoleDoorFrame = "doorframe";
        public const string RoleWindow    = "window";
        public const string RoleRadiator  = "radiator";
        public const string RoleRack      = "rack";

        /// <summary>
        /// 该部件是否**自发光**（灯带 / 屏幕 / 激光），返回强度（0 = 不发光）。
        /// 口径与 <see cref="MaterialRoleOf"/> 同一套语义真源（都只看节点名）。
        /// 亮度由调用方写进 `_WhisperEmission`（rgb=色 a=强度）——黑场里必须自己亮的部件靠它。
        /// </summary>
        public static float EmissionOf(string partName)
        {
            if (string.IsNullOrEmpty(partName)) return 0f;
            if (partName == "light_panel") return 2.2f;        // 顶灯灯带
            if (partName.StartsWith("lamp_", System.StringComparison.Ordinal)) return 2.2f;
            if (partName.StartsWith("screen_", System.StringComparison.Ordinal)) return 1.6f;
            if (partName.StartsWith("laser_", System.StringComparison.Ordinal)) return 3.0f;
            return 0f;
        }

        /// <summary>光分区基色（V9 §11 色板）。保留：灯光染色与 HUD 仍按分区取基调。</summary>
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
