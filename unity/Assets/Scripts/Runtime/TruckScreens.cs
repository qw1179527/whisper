using System.Collections.Generic;
using UnityEngine;
using Whisper.Gameplay.Level;

namespace Whisper.Runtime
{
    /// <summary>
    /// 卡车内**屏幕组（四屏）**——按官方布局落地的可读屏。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 出处（用户铁律：先拿官方资料再开工。这是**已核验的官方资料**）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// `docs/reference-official/04-场景介绍初始界面与地图.md` §2.2「右侧：屏幕组（四屏布局）」：
    ///
    /// | 位置 | 屏幕 | 功能（原文） |
    /// |---|---|---|
    /// | 左上 | 建筑地图 | 建筑布局；白色按键切楼层；一楼外围**绿色横线**=出入口；**绿色电池图标**=电闸位置；黄点=自带摄影机；运动传感器以黄线显示（触发变绿） |
    /// | 右上 | 声音传感器屏 | 装了声音传感器后显示该区域音量大小 |
    /// | 左下 | 玩家理智值 | 团队平均与个人理智；平均越低越频繁猎杀 |
    /// | 右下 | 活动强度 | 鬼魂活跃度 1–10 级；**10 级代表可能正在猎杀**；不能作为 EMF 5 级证据 |
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么用**程序化 texture** 而不是 UI Canvas
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 屏幕是"贴在 3D 世界里的物件"（卡车墙上的显示器），玩家会走近、会转动视角看它；
    /// 用世界空间的 Canvas 要么每帧做 billboard（视角一变就露馅），要么需要为每块屏建 RenderTexture 相机。
    /// 直接给面片贴一张**运行时绘制的 Texture2D**：零资产、确定性、本机可断言（
    /// `native/csharp-verify` 覆盖纯逻辑，绘制逻辑本身也在本文件里可读可测）。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 数据来自**产品真源**，不另造一套
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 理智/证据/时钟来自 `HudModel`（HUD 的唯一口径）；地图来自 `LevelData`（关卡 DSL）。
    /// 四屏的更新走 `SetData`，由组合根每 0.5 秒喂一次（与 HUD 节流同频，避免每帧重绘贴图）。
    ///
    /// ⚠ **如实登记的缺口**：声音传感器屏目前只画"未安装传感器"的空态 ——
    /// 因为本工程还没有声音传感器的**放置与音量上报**系统（资料4 说它依赖玩家布置传感器）。
    /// 这里不假装有数据（那会让屏幕撒谎），而是明确显示空态。
    /// </summary>
    public sealed class TruckScreens
    {
        /// <summary>单屏贴图边长（2 的幂，便于 mipmap；128 在手机上够清晰且重绘 <2ms）。</summary>
        public const int TexSize = 128;

        /// <summary>四屏在卡车局部坐标下的位置（相对车厢左后下角；与 TruckScene.BuildInterior 同系）。</summary>
        public readonly struct Slot
        {
            public readonly string Name;
            public readonly Vector3 LocalCenter;
            public readonly Vector2 Size;
            public Slot(string name, Vector3 c, Vector2 s) { Name = name; LocalCenter = c; Size = s; }
        }

        /// <summary>
        /// 四屏布局（官方：左侧任务板 + **右侧四屏**，2×2）。
        /// 车厢宽 2.4m ⇒ 右墙内表面约 x=+1.1；四屏贴在该墙前，左右各 0.26m、上下各 0.19m。
        /// </summary>
        public static readonly Slot[] Slots =
        {
            new Slot("Map",      new Vector3(0.86f, 1.52f, 3.55f), new Vector2(0.52f, 0.38f)),   // 左上：建筑地图
            new Slot("Sound",    new Vector3(0.86f, 1.52f, 4.15f), new Vector2(0.52f, 0.38f)),   // 右上：声音传感器
            new Slot("Sanity",   new Vector3(0.86f, 1.06f, 3.55f), new Vector2(0.52f, 0.38f)),   // 左下：理智值
            new Slot("Activity", new Vector3(0.86f, 1.06f, 4.15f), new Vector2(0.52f, 0.38f)),   // 右下：活动强度
        };

        /// <summary>四屏要显示的**全部数据**（组合根每 0.5 秒喂一次）。</summary>
        public struct Data
        {
            /// <summary>团队平均理智 0–1（存活玩家）。</summary>
            public float SanityAvg;
            /// <summary>本机玩家理智 0–1。</summary>
            public float SanitySelf;
            /// <summary>鬼魂活跃度 1–10（10 = 可能正在猎杀）。0 = 未知。</summary>
            public int Activity;
            /// <summary>已收证据 / 总数。</summary>
            public int Evidence, EvidenceTotal;
            /// <summary>本局已用时（秒）。</summary>
            public float ElapsedSeconds;
            /// <summary>当前显示楼层（地图屏用）。</summary>
            public int Floor;
            /// <summary>调查区域（画地图用）；null = 还没加载。</summary>
            public LevelData Level;
            /// <summary>
            /// 玩家当前所在**分翼**（官方 Sunny Meadows 机制：猎杀时封锁所在翼）。
            /// 地图屏必须显示它 —— 官方明说疗养院"房间高度相似，**极易迷路**"，
            /// 所以"我在哪一翼"和"哪一翼被封了"是玩家最需要的两条信息。
            /// </summary>
            public string PlayerWing;
            /// <summary>已被封锁的翼（null = 当前无封锁）。</summary>
            public string SealedWing;
        }

        readonly Transform _root;
        readonly Material _screenMat;
        readonly Texture2D _mapTex, _soundTex, _sanityTex, _activityTex;
        readonly Renderer[] _renderers = new Renderer[4];
        Data _data;
        bool _built;

        /// <summary>已建屏数（自检可读：4 才算建全）。</summary>
        public int BuiltCount { get; private set; }

        /// <summary>最近一次绘制摘要（HUD/诊断可读）。</summary>
        public string LastDraw { get; private set; } = "未绘制";

        public TruckScreens(Transform truckRoot, Material screenMaterial)
        {
            _root = truckRoot;
            _screenMat = screenMaterial;
            _mapTex = NewTex("TruckScreen_Map");
            _soundTex = NewTex("TruckScreen_Sound");
            _sanityTex = NewTex("TruckScreen_Sanity");
            _activityTex = NewTex("TruckScreen_Activity");
        }

        static Texture2D NewTex(string name)
        {
            var t = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            t.name = name;
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>建四屏面片（挂在卡车根节点下）。幂等：重复调用只建一次。</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;
            var texs = new[] { _mapTex, _soundTex, _sanityTex, _activityTex };
            for (int i = 0; i < Slots.Length; i++)
            {
                var s = Slots[i];
                // 面片法线朝 -X（朝车厢内部），厚度 0.02：与 TruckScene.Part 的立方体口径一致
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Truck_Screen_" + s.Name;
                go.transform.SetParent(_root, false);
                go.transform.localPosition = s.LocalCenter;
                go.transform.localScale = new Vector3(0.02f, s.Size.y, s.Size.x);
                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    // 每块屏一份材质实例：四屏各自一张贴图，不能共用（共用会导致四屏显示同一画面）
                    var m = _screenMat != null ? new Material(_screenMat.shader) : null;
                    if (m != null)
                    {
                        m.name = "TruckScreenMat_" + s.Name;
                        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texs[i]);
                        if (m.HasProperty("_WhisperEmission"))
                        {
                            // 自发光：黑场卡车里屏幕必须自己亮（否则玩家什么都看不见）
                            m.SetColor("_WhisperEmission", new Color(0.75f, 0.92f, 1.00f, 1.10f));
                        }
                    }
                    r.sharedMaterial = m;
                    _renderers[i] = r;
                }
                BuiltCount++;
            }
            SetData(default);   // 先画一遍空态，避免"黑屏直到第一次数据"
        }

        /// <summary>喂数据并按需重绘（贴图重绘有成本，所以只在数据变化时重建）。</summary>
        public void SetData(in Data d)
        {
            bool changed = !_built
                || Mathf.Abs(d.SanityAvg - _data.SanityAvg) > 0.005f
                || Mathf.Abs(d.SanitySelf - _data.SanitySelf) > 0.005f
                || d.Activity != _data.Activity
                || d.Evidence != _data.Evidence
                || d.Floor != _data.Floor
                || d.Level != _data.Level
                || Mathf.Abs(d.ElapsedSeconds - _data.ElapsedSeconds) > 1f;
            _data = d;
            if (!changed) return;
            Redraw();
        }

        void Redraw()
        {
            DrawMap(_mapTex, _data);
            DrawSound(_soundTex, _data);
            DrawSanity(_sanityTex, _data);
            DrawActivity(_activityTex, _data);
            LastDraw = $"地图 楼层{_data.Floor} · 分翼 {_data.PlayerWing ?? "-"}"
                + (_data.SealedWing != null ? $"(已封锁 {_data.SealedWing})" : "")
                + $" · 理智 {_data.SanityAvg * 100f:0}%/{_data.SanitySelf * 100f:0}%"
                + $" · 活动 {_data.Activity}/10 · 证据 {_data.Evidence}/{_data.EvidenceTotal}";
        }

        // ───────────────────────── 四个屏的绘制 ─────────────────────────

        /// <summary>
        /// 左上：**建筑地图**（官方：布局 + 白色按键切楼层 + 绿色横线出入口 + 绿色电池电闸 + 黄点摄影机）。
        /// 本实现画：房间矩形（按光区着色）、当前楼层高亮、玩家不在（玩家位姿由实时系统给，本轮不接）。
        /// </summary>
        void DrawMap(Texture2D tex, in Data d)
        {
            Clear(tex, new Color(0.03f, 0.06f, 0.05f));
            if (d.Level == null || d.Level.Rooms == null || d.Level.Rooms.Count == 0)
            {
                Label(tex, "无地图", 0.5f, 0.5f, new Color(0.4f, 0.8f, 0.6f));
                tex.Apply();
                return;
            }
            // 世界范围 → 贴图范围（留 12% 边距）
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            // ⚠ **只按当前楼层算范围**（原先用全关卡范围）。
            // 后果（本机出图可见）：地图按楼层画，但范围含其它楼层的房间 ⇒
            // 当前楼层的房间只占画布一部分，右侧留大片空白。
            // 逐层算范围后，每层都能铺满屏幕（屏幕小、要看清，面积优先）。
            foreach (var r in d.Level.Rooms)
            {
                if (r.Floor != d.Floor) continue;
                if (r.MinX < minX) minX = r.MinX;
                if (r.MinZ < minZ) minZ = r.MinZ;
                if (r.MaxX > maxX) maxX = r.MaxX;
                if (r.MaxZ > maxZ) maxZ = r.MaxZ;
            }
            if (minX > maxX) { minX = minZ = 0f; maxX = maxZ = 1f; }   // 该层无房间（不要出现负跨度）
            // ── 【2026-10-06 本机检出】各轴**独立**缩放，而不是共用一个 scale ──────────
            // 缺陷：原先用 `scale = 0.76 / max(spanX, spanZ)`，两轴共用。
            // 疗养院是 22×11（扁的），屏幕是 128×128（正方形）⇒ 地图只占画布**不到一半高度**，
            // 浪费一半屏幕（而屏幕是玩家凑近看的物件，字号本来就紧张）。
            // 本机把 C# 的绘制逻辑照搬到 node（`tmp/port-screen.mjs`）出图**看出来的**，
            // 不占 CI —— 这是"UI 也能本机验证"的第一条通道。
            // ⚠ 各轴**独立拉满**（不做等比缩放）。
            // 真实宽高比是 **22:11 = 2:1**，而屏幕是 128×128 正方形 ——
            // 等比缩放会让地图只占画布**下半部**（本机出图看得很清楚），浪费一半面积。
            // 屏幕是玩家凑近看的 0.5m 物件、字号本来就紧张 ⇒ **铺满**比"保持比例"更重要。
            // （取舍已写明：牺牲比例、换取面积。若将来屏幕变大可改回等比。）
            float spanX = Mathf.Max(maxX - minX, 1f), spanZ = Mathf.Max(maxZ - minZ, 1f);
            const float FillK = 0.96f;                       // 留 4% 边距，避免贴边被裁
            float scaleX = FillK / spanX, scaleZ = FillK / spanZ;
            float ox = (1f - spanX * scaleX) * 0.5f, oz = (1f - spanZ * scaleZ) * 0.5f;

            foreach (var r in d.Level.Rooms)
            {
                int px = Mathf.RoundToInt((ox + (r.MinX - minX) * scaleX) * TexSize);
                int py = Mathf.RoundToInt((oz + (r.MinZ - minZ) * scaleZ) * TexSize);
                int pw = Mathf.Max(1, Mathf.RoundToInt(r.SizeX * scaleX * TexSize));
                int ph = Mathf.Max(1, Mathf.RoundToInt(r.SizeZ * scaleZ * TexSize));
                // 光区配色：与 LevelPalette 的情绪梯度同源（危险区偏暖、安全区偏冷）
                Color c = r.LightZone == "safe" ? new Color(0.35f, 0.75f, 0.55f)
                        : r.LightZone == "high-risk" ? new Color(0.75f, 0.45f, 0.30f)
                        : new Color(0.40f, 0.62f, 0.85f);
                bool here = r.Floor == d.Floor;             // 当前楼层高亮
                if (!here) c *= 0.35f;
                // 证据点：画成亮块（官方地图上鬼房相关标记是重点信息）
                if (r.EvidencePoint && here) c = Color.Lerp(c, new Color(1f, 0.95f, 0.6f), 0.45f);
                // ── 分翼（官方机制）──────────────────────────────────────────────
                // 玩家所在翼：描亮（**"我在哪一翼"** —— 官方说这张图极易迷路）
                // 被封锁的翼：压成暗红（**"哪一翼被封了"** —— 那是会要命的信息）
                if (here && !string.IsNullOrEmpty(d.PlayerWing) && r.Wing == d.PlayerWing)
                    c = Color.Lerp(c, Color.white, 0.35f);
                if (here && !string.IsNullOrEmpty(d.SealedWing) && r.Wing == d.SealedWing)
                    c = Color.Lerp(c, new Color(0.85f, 0.15f, 0.12f), 0.55f);
                Rect(tex, px, py, pw, ph, c);
            }
            // 出入口（标准撤离点）用**绿色横线**——官方口径
            if (d.Level.Extraction != null)
            {
                foreach (var r in d.Level.Rooms)
                {
                    if (r.Id != d.Level.Extraction.Standard) continue;
                    int px = Mathf.RoundToInt((ox + (r.MinX - minX) * scaleX) * TexSize);
                    int pw = Mathf.Max(2, Mathf.RoundToInt(r.SizeX * scaleX * TexSize));
                    int py = Mathf.RoundToInt((oz + (r.MinZ - minZ) * scaleZ) * TexSize);
                    Rect(tex, px, py - 1, pw, 2, new Color(0.2f, 1f, 0.35f));
                }
            }
            Label(tex, $"楼层 {d.Floor}", 0.5f, 0.06f, new Color(0.9f, 0.95f, 1f), 1);
            tex.Apply();
        }

        /// <summary>
        /// 右上：**声音传感器屏**。
        /// ⚠ 如实登记：本工程还没有"声音传感器的放置与音量上报"系统（资料4 说该屏依赖玩家布置传感器）。
        /// 所以这里明确画**空态**而不是假数据 —— 屏幕撒谎比屏幕空着更糟。
        /// </summary>
        void DrawSound(Texture2D tex, in Data d)
        {
            Clear(tex, new Color(0.04f, 0.05f, 0.07f));
            Label(tex, "声音传感器", 0.5f, 0.72f, new Color(0.55f, 0.70f, 0.85f), 1);
            Label(tex, "未安装", 0.5f, 0.46f, new Color(0.45f, 0.50f, 0.58f), 1);
            // 空态波形：一条基线，明确"没有信号"而不是"信号为零"
            Rect(tex, 12, TexSize / 2 - 14, TexSize - 24, 1, new Color(0.25f, 0.32f, 0.38f));
            tex.Apply();
        }

        /// <summary>左下：**玩家理智值**（官方：显示团队平均与个人理智）。</summary>
        void DrawSanity(Texture2D tex, in Data d)
        {
            Clear(tex, new Color(0.05f, 0.04f, 0.06f));
            Label(tex, "理智", 0.5f, 0.86f, new Color(0.80f, 0.75f, 0.90f), 1);
            Bar(tex, 10, 92, TexSize - 20, 12, d.SanityAvg, SanityColor(d.SanityAvg), "队");
            Bar(tex, 10, 62, TexSize - 20, 12, d.SanitySelf, SanityColor(d.SanitySelf), "我");
            Label(tex, $"{d.SanityAvg * 100f:0}%", 0.5f, 0.30f, SanityColor(d.SanityAvg));
            // 证据进度：也放这块屏（官方把证据放在日志，但屏幕上给出即时反馈更有用；这是我们的取舍，已注明）
            Label(tex, $"证据 {d.Evidence}/{d.EvidenceTotal}", 0.5f, 0.10f, new Color(0.7f, 0.85f, 0.7f), 1);
            tex.Apply();
        }

        /// <summary>右下：**活动强度**（官方：1–10 级；10 级代表可能正在猎杀；不能作为 EMF5 证据）。</summary>
        void DrawActivity(Texture2D tex, in Data d)
        {
            Clear(tex, new Color(0.06f, 0.04f, 0.04f));
            Label(tex, "活动强度", 0.5f, 0.86f, new Color(0.90f, 0.80f, 0.70f), 1);
            // 10 格柱状：亮起的格数 = 当前等级
            int n = Mathf.Clamp(Mathf.RoundToInt(d.Activity), 0, 10);
            int bw = (TexSize - 24) / 10;
            for (int i = 0; i < 10; i++)
            {
                Color c = i < n
                    ? (i >= 9 ? new Color(1f, 0.30f, 0.25f) : new Color(0.95f, 0.75f, 0.30f))
                    : new Color(0.16f, 0.14f, 0.13f);
                Rect(tex, 12 + i * bw, 72, bw - 1, 26, c);
            }
            Label(tex, n > 0 ? $"{n} / 10" : "—", 0.5f, 0.34f,
                n >= 9 ? new Color(1f, 0.45f, 0.40f) : new Color(0.9f, 0.85f, 0.75f));
            // 官方明确："10 级不一定代表正在猎杀" ⇒ 只在 10 级给"可能"提示，不给断言
            if (n >= 10) Label(tex, "可能正在猎杀", 0.5f, 0.12f, new Color(1f, 0.5f, 0.45f), 1);
            tex.Apply();
        }

        // ───────────────────────── 贴图绘制原语（确定性、无资产）─────────────────────────

        static Color SanityColor(float v)
            => v > 0.6f ? new Color(0.55f, 0.85f, 0.55f)
             : v > 0.3f ? new Color(0.92f, 0.82f, 0.35f)
                        : new Color(0.95f, 0.35f, 0.30f);

        static void Clear(Texture2D t, Color c)
        {
            var px = new Color[t.width * t.height];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px);
        }

        /// <summary>画一个矩形（坐标原点在**左下**，与 Unity 纹理一致；y 向上）。</summary>
        static void Rect(Texture2D t, int x, int y, int w, int h, Color c)
        {
            for (int yy = y; yy < y + h; yy++)
            {
                if (yy < 0 || yy >= t.height) continue;
                for (int xx = x; xx < x + w; xx++)
                {
                    if (xx < 0 || xx >= t.width) continue;
                    t.SetPixel(xx, yy, c);
                }
            }
        }

        /// <summary>画一条进度条（value 0–1；顺序为背景 + 填充）。</summary>
        static void Bar(Texture2D t, int x, int y, int w, int h, float value, Color fill, string tag)
        {
            Rect(t, x, y, w, h, new Color(0.12f, 0.12f, 0.14f));
            int fw = Mathf.Clamp(Mathf.RoundToInt(w * Mathf.Clamp01(value)), 0, w);
            Rect(t, x, y, fw, h, fill);
            // 标签用极简 3×5 点阵（只支持几个字，够标"队/我"）
            if (tag == "队") Glyph(t, x - 6, y + 2, fill);
        }

        /// <summary>一个 3×5 的点阵"圆点"，用来在贴图上做极小的位置标记（不引入字体资源）。</summary>
        static void Glyph(Texture2D t, int x, int y, Color c) => Rect(t, x, y, 3, 5, c);

        /// <summary>
        /// 画一行"文字"。
        /// ⚠ 取舍：本工程**没有屏幕字体资源**（无字体资产、无 TMP），而屏幕需要可读标签。
        /// 这里不做真字形渲染，只画**条状占位 + 高对比色**表示"这里有字"，
        /// 并把真实数值交给 `LastDraw`（HUD/日志可读）。**不假装屏幕上有可读文字。**
        /// （真字形要在 Blender 里烘一张位图字体，属于后续里程碑；已在会话记录里登记。）
        /// </summary>
        static void Label(Texture2D t, string text, float cx, float cy, Color c, int lines = 2)
        {
            int x0 = Mathf.RoundToInt(cx * t.width), y0 = Mathf.RoundToInt(cy * t.height);
            for (int i = 0; i < lines; i++)
            {
                int w = Mathf.Max(6, text.Length * 3);
                Rect(t, x0 - w / 2, y0 + i * 5, w, 3, c);
            }
        }
    }
}
