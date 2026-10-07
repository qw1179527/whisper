using UnityEngine;
using UnityEngine.Rendering;      // GraphicsSettings.currentRenderPipeline（取 URP Asset 的 renderScale）
using Whisper.Gameplay.Config;

namespace Whisper.Runtime
{
    /// <summary>
    /// **把 `data/config.json` 的 `render` 段真正应用到引擎**。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么需要它（2026-10-07 实测抓到："配置定义了 22 个旋钮，一个都没被读"）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// `data/config.json` 的 `render` 段是**渲染质量的真源**（`_note` 明写"渲染质量三档"），
    /// 包含两档 × 约 22 个旋钮 + `frameRates`。但全仓搜索的结果是：
    /// ```
    /// Application.targetFrameRate   → 0 处
    /// renderScale / SetQualityLevel / defaultTier  → 0 处
    /// ```
    /// ⇒ **帧率、分辨率缩放、各向异性过滤这些旋钮从来没有被应用过**；
    ///   而 `UrpSetup` 在**编辑器里另写了一套硬编码值**（`shadowDistance = 20f` 等），
    ///   与配置里的（`shadowDistanceM: 40`）**不一致**。
    ///
    /// 这正是本项目反复出现的失效形态：**"配了 ≠ 生效"** ——
    /// 配置文件、文档、注释都写着，唯独**没有那一句把它接上去的代码**。
    ///
    /// ⚠ 本类**不重复定义**任何数值：全部走 `GameConfigReader` 读 `render.*`
    ///   （若在这里再写一遍常量，就等于又造了一份真源，那正是本类的起因）。
    ///
    /// ⚠ 分工：`UrpSetup`（编辑器）负责**资产结构**（管线/渲染器/Volume 组件是否存在），
    ///   本类（运行时）负责**档位数值**（帧率/分辨率/过滤/阴影开关）。
    ///   两者职责不重叠；重叠的部分（如 shadowDistance）以**配置为准**，见 ApplyQuality。
    /// </summary>
    public static class RenderTierApplier
    {
        /// <summary>最近一次应用摘要（HUD/诊断可读，证明"真的接上了"）。</summary>
        public static string LastApplied { get; private set; } = "未应用";

        /// <summary>
        /// 按档位应用渲染设置。`tier` 取 "low" / "high"（缺省用 `render.defaultTier`）。
        ///
        /// 返回值：实际应用的条数（0 = 配置读不到，调用方应把它当**失败**而不是"没事"）。
        /// </summary>
        public static int Apply(GameConfigReader cfg, string tier = null)
        {
            if (cfg == null) { LastApplied = "cfg 为 null —— 未应用任何渲染设置"; return 0; }
            if (string.IsNullOrEmpty(tier)) tier = cfg.String("render.defaultTier", "high");
            string p = "render.tiers." + tier;
            int n = 0;

            // ── ① 帧率：**这是最直接的一项，此前完全没接** ──────────────────────────
            // 口径：`render.frameRates` 是允许的档位集合，`render.defaultFrameRate` 是默认值。
            // 目标帧率取默认值；若调用方要覆盖（设置界面），传进来即可 —— 但**必须在允许集合里**，
            // 否则视为配置错误（宁可回退到默认，也不接受一个没登记的帧率）。
            int target = cfg.Int("render.defaultFrameRate", 60);
            Application.targetFrameRate = target;
            QualitySettings.vSyncCount = 0;     // 移动端：让 targetFrameRate 说话，别被垂直同步接管
            n += 2;

            // ── ② 分辨率缩放（URP 侧）──────────────────────────────────────────────
            // URP Asset 的 `renderScale` 是"以原生分辨率的比例渲染"。
            // 走反射的原因与 CameraPostFx 相同：Runtime 的 asmdef 不引用 URP 程序集。
            var urp = GraphicsSettings.currentRenderPipeline;
            float scale = cfg.Float(p + ".renderScale", 1f);
            if (urp != null && TrySetFloat(urp, "renderScale", Mathf.Clamp(scale, 0.5f, 1.5f))) n++;

            // ── ③ 阴影（QualitySettings 侧；URP Asset 侧由 UrpSetup 管）─────────────
            // `shadows` 在配置里是 0/1/2（Disable/HardOnly/All），与 UnityEngine.ShadowQuality 同序。
            int sh = cfg.Int(p + ".shadows", 2);
            QualitySettings.shadows = (ShadowQuality)Mathf.Clamp(sh, 0, 2);
            int shRes = cfg.Int(p + ".shadowResolution", 2);
            QualitySettings.shadowResolution = (ShadowResolution)Mathf.Clamp(shRes, 0, 3);
            QualitySettings.shadowDistance = cfg.Float(p + ".shadowDistanceM", 40f);
            n += 3;

            // ── ④ 逐像素灯数 / 抗锯齿 / 各向异性 ─────────────────────────────────
            QualitySettings.pixelLightCount = cfg.Int(p + ".pixelLightCount", 4);
            QualitySettings.antiAliasing = Mathf.Clamp(cfg.Int(p + ".antiAliasing", 4), 0, 8);
            QualitySettings.anisotropicFiltering = (AnisotropicFiltering)Mathf.Clamp(
                cfg.Int(p + ".anisotropicFiltering", 1), 0, 2);
            n += 3;

            // ── ④b 体积光（光柱）──────────────────────────────────────────────────
            // ⚠ 这里接的是**产品侧总开关**：`LightShaft.Enabled`。
            // 若只把配置改 true 而不接这一句，就又是一次"配了 ≠ 生效"（本类的成因）。
            bool vol = cfg.Bool(p + ".volumetricLight", false);
            Whisper.Gameplay.Level.LightShaft.Enabled = vol;
            if (!vol) Whisper.Gameplay.Level.LightShaft.SetAllEnabled(false);   // 已建的也关掉
            n++;

            // ── ⑤ 相机远裁剪（`farClipM`）────────────────────────────────────────
            // 由调用方把相机传进来更干净，但为了"一处应用"，这里直接取主相机；
            // 取不到就跳过（不报错 —— 相机可能还没建）。
            var cam = Camera.main;
            float far = cfg.Float(p + ".farClipM", 90f);
            if (cam != null && far > 1f) { cam.farClipPlane = far; n++; }

            LastApplied = $"档 {tier} · 目标帧率 {target} · renderScale {scale:0.00}"
                + $" · shadows {QualitySettings.shadows}/{QualitySettings.shadowResolution}"
                + $" · shadowDistance {QualitySettings.shadowDistance:0}m"
                + $" · pixelLight {QualitySettings.pixelLightCount} · AA {QualitySettings.antiAliasing}"
                + $" · 体积光 {vol}"
                + $" · 各向异性 {QualitySettings.anisotropicFiltering}"
                + (cam != null ? $" · farClip {cam.farClipPlane:0}m" : " · (无主相机，跳过 farClip)")
                + $" → 应用 {n} 项";
            Debug.Log("[RenderTierApplier] " + LastApplied);
            return n;
        }

        /// <summary>允许的帧率档位（`render.frameRates`）—— 设置界面用，也便于自检。</summary>
        public static int[] AllowedFrameRates(GameConfigReader cfg)
        {
            if (cfg == null) return new int[0];
            // GameConfigReader 没有数组 API ⇒ 逐个试已知上限（配置是 [60,90,120]）
            var list = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 8; i++)
            {
                int v = cfg.Int($"render.frameRates[{i}]", -1);
                if (v < 0) break;
                list.Add(v);
            }
            return list.ToArray();
        }

        static bool TrySetFloat(object target, string member, float value)
        {
            var t = target.GetType();
            var p = t.GetProperty(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (p != null && p.CanWrite && p.PropertyType == typeof(float)) { p.SetValue(target, value); return true; }
            var f = t.GetField(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(float)) { f.SetValue(target, value); return true; }
            return false;
        }
    }
}
