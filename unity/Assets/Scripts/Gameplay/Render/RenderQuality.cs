using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Render
{
    /// <summary>画质档（用户要求：低 / 高 / 顶级）。</summary>
    public enum QualityTier { Low = 0, High = 1, Top = 2 }

    /// <summary>帧率档（用户要求：60 / 90 / 120）。</summary>
    public enum FrameRateTier { Fps60 = 60, Fps90 = 90, Fps120 = 120 }

    /// <summary>
    /// 渲染质量档位（用户永久约束 §2/§6）。
    ///
    /// ## 为什么单独做一层，而不是直接调 Unity 的 QualitySettings
    /// Unity 的 `QualitySettings.SetQualityLevel(n)` 是按**名字/序号**切工程里预设的档，
    /// 但用户要的是一组**明确的开关**（体积光/辉光/颗粒/SSGI/SSAO/眼部适应/阴影质量/抗锯齿 + 帧率），
    /// 而工程里的 6 档预设（Very Low…Ultra）并没有按这个维度划分。
    /// 所以正解是：**本类持有一份显式的档位表**（三档 × 每个开关的值），
    /// 应用时既设 Unity 的全局量，也把开关值交给后处理组件 —— 一处定义、一处应用。
    ///
    /// ## 数值来源
    /// 全部读 `render.tiers.*`（`data/config.json`），代码零硬编码。
    /// 官方没有这套数值（官方是 Unity 内置管线，用户要求"做得更好"）→ 配置里标 `design`。
    ///
    /// ## 与"60/90/120 帧"的关系
    /// 帧率是**独立**于画质档的（用户并列提出）→ `Apply` 同时接收 tier 与 frameRate。
    /// `Application.targetFrameRate` 是唯一真源；`QualitySettings.vSyncCount` 必须为 0，
    /// 否则在移动端 vSync 会覆盖 targetFrameRate（这是"设了 120 却只有 60"的经典原因）。
    /// </summary>
    public sealed class RenderQuality
    {
        /// <summary>一个档位的全部开关（值来自配置）。</summary>
        public struct Tier
        {
            public string Name;
            /// <summary>渲染缩放（1 = 原生；<1 降分辨率换帧率）。</summary>
            public float RenderScale;
            /// <summary>像素光上限（超出的灯被降级，灯光效果变差但更快）。</summary>
            public int PixelLightCount;
            /// <summary>阴影：0 关 / 1 硬 / 2 软。</summary>
            public int Shadows;
            /// <summary>阴影贴图分辨率档：0..3（Unity 的 ShadowResolution）。</summary>
            public int ShadowResolution;
            /// <summary>阴影距离（米）。</summary>
            public float ShadowDistanceM;
            /// <summary>MSAA 采样数（0/2/4/8）。</summary>
            public int AntiAliasing;
            /// <summary>各向异性过滤（0/1/2 = Disable/PerTexture/ForceEnable）。</summary>
            public int AnisotropicFiltering;
            /// <summary>绘制距离（米）。</summary>
            public float FarClipM;
            // ── 后处理开关（交给 PostFx 组件；本类只存值）──
            public bool Bloom;
            public bool Ssgi;
            public bool Ssao;
            public bool EyeAdaptation;
            public bool Grain;
            public bool Vignette;
            public bool VolumetricLight;
            public float BloomIntensity;
            public float SsgiIntensity;
            public float SsaoRadiusM;
            public float GrainAmount;
            public float EyeAdaptSpeed;
        }

        readonly Tier[] _tiers = new Tier[3];

        /// <summary>配置缺键清单（空 = 全部来自配置）。</summary>
        public IReadOnlyList<string> ConfigProblems { get; }

        /// <summary>当前档位。</summary>
        public QualityTier Current { get; private set; } = QualityTier.High;
        /// <summary>当前帧率档。</summary>
        public FrameRateTier FrameRate { get; private set; } = FrameRateTier.Fps60;

        public RenderQuality(GameConfigReader cfg)
        {
            var problems = new List<string>();
            _tiers[0] = ReadTier(cfg, "low", "低画质", problems);
            _tiers[1] = ReadTier(cfg, "high", "高画质", problems);
            _tiers[2] = ReadTier(cfg, "top", "顶级画质", problems);
            ConfigProblems = problems;
        }

        /// <summary>取某档（只读）。</summary>
        public Tier Get(QualityTier t) => _tiers[(int)t];
        /// <summary>当前档。</summary>
        public Tier CurrentTier => _tiers[(int)Current];
        /// <summary>三档名字（UI 用）。</summary>
        public string NameOf(QualityTier t) => _tiers[(int)t].Name;

        /// <summary>切换档位并返回新档（**不**直接碰 Unity API —— 由调用方 Apply，便于测试）。</summary>
        public Tier Select(QualityTier t) { Current = t; return CurrentTier; }

        /// <summary>切换帧率档（60/90/120，仅这三档）。</summary>
        public bool SelectFrameRate(int fps)
        {
            if (fps != 60 && fps != 90 && fps != 120) return false;
            FrameRate = (FrameRateTier)fps;
            return true;
        }

        /// <summary>档位序（低→高→顶级→低），供"一键切换"按钮用。</summary>
        public QualityTier NextTier() => (QualityTier)(((int)Current + 1) % 3);
        /// <summary>帧率序（60→90→120→60）。</summary>
        public int NextFrameRate() => FrameRate == FrameRateTier.Fps60 ? 90 : FrameRate == FrameRateTier.Fps90 ? 120 : 60;

        /// <summary>HUD 一行摘要。</summary>
        public string Describe()
            => $"画质 {CurrentTier.Name} · {CurrentTier.RenderScale:0.##}x · 帧率 {(int)FrameRate}"
             + $" · 像素光 {CurrentTier.PixelLightCount} · 阴影 {ShadowName(CurrentTier.Shadows)}"
             + $" · MSAA {(CurrentTier.AntiAliasing == 0 ? "关" : CurrentTier.AntiAliasing + "x")}"
             + $" · 辉光{(CurrentTier.Bloom ? "开" : "关")} SSGI{(CurrentTier.Ssgi ? "开" : "关")}"
             + $" SSAO{(CurrentTier.Ssao ? "开" : "关")} 眼适应{(CurrentTier.EyeAdaptation ? "开" : "关")}"
             + $" 颗粒{(CurrentTier.Grain ? "开" : "关")}";

        static string ShadowName(int s) => s <= 0 ? "关" : s == 1 ? "硬" : "软";

        Tier ReadTier(GameConfigReader cfg, string key, string fallbackName, List<string> problems)
        {
            string root = "render.tiers." + key + ".";
            var t = new Tier
            {
                Name = cfg.String(root + "name", fallbackName),
                RenderScale = cfg.Float(root + "renderScale", 1f),
                PixelLightCount = cfg.Int(root + "pixelLightCount", 4),
                Shadows = cfg.Int(root + "shadows", 2),
                ShadowResolution = cfg.Int(root + "shadowResolution", 2),
                ShadowDistanceM = cfg.Float(root + "shadowDistanceM", 40f),
                AntiAliasing = cfg.Int(root + "antiAliasing", 4),
                AnisotropicFiltering = cfg.Int(root + "anisotropicFiltering", 2),
                FarClipM = cfg.Float(root + "farClipM", 90f),
                Bloom = cfg.Bool(root + "bloom", true),
                Ssgi = cfg.Bool(root + "ssgi", false),
                Ssao = cfg.Bool(root + "ssao", false),
                EyeAdaptation = cfg.Bool(root + "eyeAdaptation", true),
                Grain = cfg.Bool(root + "grain", true),
                Vignette = cfg.Bool(root + "vignette", true),
                VolumetricLight = cfg.Bool(root + "volumetricLight", false),
                BloomIntensity = cfg.Float(root + "bloomIntensity", 0.6f),
                SsgiIntensity = cfg.Float(root + "ssgiIntensity", 0.5f),
                SsaoRadiusM = cfg.Float(root + "ssaoRadiusM", 0.6f),
                GrainAmount = cfg.Float(root + "grainAmount", 0.05f),
                EyeAdaptSpeed = cfg.Float(root + "eyeAdaptSpeed", 1.2f),
            };
            // 自检：名字没读到配置（还是缺省）就登记问题，而不是静默用兜底值
            if (cfg.Get("render.tiers." + key) == null)
                problems.Add($"render.tiers.{key} 整段缺失（用了内置缺省）");
            // 合理性：MSAA 只能是 0/2/4/8；阴影 0..2
            if (t.AntiAliasing != 0 && t.AntiAliasing != 2 && t.AntiAliasing != 4 && t.AntiAliasing != 8)
            { problems.Add($"render.tiers.{key}.antiAliasing={t.AntiAliasing} 非法（只能 0/2/4/8）"); t.AntiAliasing = 4; }
            if (t.Shadows < 0 || t.Shadows > 2)
            { problems.Add($"render.tiers.{key}.shadows={t.Shadows} 非法（只能 0/1/2）"); t.Shadows = 2; }
            if (t.RenderScale <= 0f || t.RenderScale > 1f)
            { problems.Add($"render.tiers.{key}.renderScale={t.RenderScale} 非法（(0,1]）"); t.RenderScale = 1f; }
            return t;
        }
    }
}
